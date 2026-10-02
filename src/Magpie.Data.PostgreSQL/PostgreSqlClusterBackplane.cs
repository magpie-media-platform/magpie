using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

using Magpie.Models;
using Magpie.Sdk.Extensions;
using Magpie.Sdk.Services;

using Microsoft.Extensions.Logging;

using Npgsql;

namespace Magpie.Data.PostgreSQL;

internal sealed class PostgreSqlClusterBackplane(string connectionString, ILogger<PostgreSqlClusterBackplane> logger)
    : IClusterBackplane
{
    private static readonly TimeSpan InitialReconnectDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MaxReconnectDelay = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan KeepAliveInterval = TimeSpan.FromSeconds(30);
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private const string ChannelName = "magpie_cluster_events";
    private const string ListenCommandText = "LISTEN " + ChannelName;
    private const int MaxPayloadBytes = 7999;
    private const string RelayReferencePrefix = "@relay:";

    private const string RelayCommandText =
        """
        INSERT INTO "system_clusterEvent" ("Payload", "PublishedAt") VALUES (@payload, now()) RETURNING "Seq";
        DELETE FROM "system_clusterEvent" WHERE "PublishedAt" < now() - interval '5 minutes';
        """;

    public async Task BroadcastEventAsync(MagpieEvent magpieEvent, CancellationToken cancellationToken = default)
    {
        var payload = JsonSerializer.Serialize(magpieEvent, SerializerOptions);

        var connection = new NpgsqlConnection(connectionString);
        await using var connectionHandle = connection.ConfigureAwait(false);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        if (Encoding.UTF8.GetByteCount(payload) > MaxPayloadBytes)
        {
            payload = RelayReferencePrefix +
                await RelayPayloadAsync(connection, payload, cancellationToken).ConfigureAwait(false);
        }

        var command = new NpgsqlCommand("SELECT pg_notify(@channel, @payload)", connection);
        await using var commandHandle = command.ConfigureAwait(false);
        command.Parameters.AddWithValue("channel", ChannelName);
        command.Parameters.AddWithValue("payload", payload);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public IEventListener CreateListener()
    {
        return new NotificationListener(connectionString, logger);
    }

    private static async Task<long> RelayPayloadAsync(
        NpgsqlConnection connection,
        string payload,
        CancellationToken cancellationToken)
    {
        var command = new NpgsqlCommand(RelayCommandText, connection);
        await using var commandHandle = command.ConfigureAwait(false);
        command.Parameters.AddWithValue("payload", payload);

        return Convert.ToInt64(
            await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
            CultureInfo.InvariantCulture);
    }

    private sealed class NotificationListener : IEventListener
    {
        private readonly string _connectionString;
        private readonly ILogger _logger;
        private readonly Channel<MagpieEvent> _channel = Channel.CreateUnbounded<MagpieEvent>();
        private readonly Channel<string> _payloads = Channel.CreateUnbounded<string>();
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _listenLoop;
        private readonly Task _resolveLoop;

        public NotificationListener(string connectionString, ILogger logger)
        {
            _connectionString = new NpgsqlConnectionStringBuilder(connectionString)
            {
                KeepAlive = (int)KeepAliveInterval.TotalSeconds,
            }.ConnectionString;
            _logger = logger;
            _listenLoop = Task.Run(() => ListenAsync(_cts.Token), default);
            _resolveLoop = Task.Run(() => ResolvePayloadsAsync(_cts.Token), default);
        }

        public IAsyncEnumerable<MagpieEvent> ReadAllAsync(CancellationToken cancellationToken = default)
        {
            return _channel.Reader.ReadAllAsync(cancellationToken);
        }

        public void Dispose()
        {
            _cts.Cancel();
            _payloads.Writer.TryComplete();
            _channel.Writer.TryComplete();
        }

        public async ValueTask DisposeAsync()
        {
            Dispose();

            try
            {
                await _listenLoop.ConfigureAwait(false);
                await _resolveLoop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }

            _cts.Dispose();
        }

        private async Task ListenAsync(CancellationToken cancellationToken)
        {
            var reconnectDelay = InitialReconnectDelay;
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var connection = new NpgsqlConnection(_connectionString);
                    await using var connectionHandle = connection.ConfigureAwait(false);
                    connection.Notification += OnNotification;
                    await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

                    var command = new NpgsqlCommand(ListenCommandText, connection);
                    await using var commandHandle = command.ConfigureAwait(false);
                    await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

                    reconnectDelay = InitialReconnectDelay;
                    while (true)
                    {
                        await connection.WaitAsync(cancellationToken).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "Cluster backplane listener disconnected; retrying in {Delay}.",
                        reconnectDelay);

                    try
                    {
                        await Task.Delay(reconnectDelay, cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }

                    reconnectDelay = TimeSpan.FromTicks(Math.Min(reconnectDelay.Ticks * 2, MaxReconnectDelay.Ticks));
                }
            }

            _payloads.Writer.TryComplete();
        }

        private void OnNotification(object sender, NpgsqlNotificationEventArgs e)
        {
            _payloads.Writer.TryWrite(e.Payload);
        }

        private async Task ResolvePayloadsAsync(CancellationToken cancellationToken)
        {
            try
            {
                await foreach (var notified in _payloads.Reader
                    .ReadAllAsync(cancellationToken)
                    .ConfigureAwait(false))
                {
                    try
                    {
                        var payload = notified.StartsWith(RelayReferencePrefix, StringComparison.Ordinal)
                            ? await ReadRelayedPayloadAsync(notified, cancellationToken).ConfigureAwait(false)
                            : notified;
                        if (payload == null)
                        {
                            continue;
                        }

                        var magpieEvent = JsonSerializer.Deserialize<MagpieEvent>(payload, SerializerOptions);
                        if (magpieEvent is not null)
                        {
                            _channel.Writer.TryWrite(magpieEvent);
                        }
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(
                            ex,
                            "Discarded unreadable cluster backplane notification payload.");
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }

            _channel.Writer.TryComplete();
        }

        private async Task<string?> ReadRelayedPayloadAsync(string notified, CancellationToken cancellationToken)
        {
            var reference = notified[RelayReferencePrefix.Length..];
            if (!long.TryParse(reference, CultureInfo.InvariantCulture, out var sequence))
            {
                _logger.LogWarning("Discarded unreadable relay reference {Reference}.", reference);
                return null;
            }

            try
            {
                var connection = new NpgsqlConnection(_connectionString);
                await using var connectionHandle = connection.ConfigureAwait(false);
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

                var command = new NpgsqlCommand(
                    """SELECT "Payload" FROM "system_clusterEvent" WHERE "Seq" = @sequence""",
                    connection);
                await using var commandHandle = command.ConfigureAwait(false);
                command.Parameters.AddWithValue("sequence", sequence);
                var payload = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string;
                if (payload == null)
                {
                    _logger.LogWarning("No relayed cluster event {Sequence}.", sequence);
                }

                return payload;
            }
            catch (NpgsqlException ex)
            {
                _logger.LogWarning(ex, "Reading relayed cluster event {Sequence} failed.", sequence);
                return null;
            }
        }
    }
}
