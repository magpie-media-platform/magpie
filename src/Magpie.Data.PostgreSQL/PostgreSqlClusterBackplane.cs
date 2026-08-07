using System;
using System.Collections.Generic;
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

    public async Task BroadcastEventAsync(MagpieEvent magpieEvent, CancellationToken cancellationToken = default)
    {
        var payload = JsonSerializer.Serialize(magpieEvent, SerializerOptions);
        if (Encoding.UTF8.GetByteCount(payload) > MaxPayloadBytes)
        {
            logger.LogError(
                "The event {EventName} ({EventId}) is larger than the PostgreSQL notification limit. The " +
                "backplane discarded the event, so the peers did not receive it.",
                magpieEvent.Name,
                magpieEvent.Id);
            return;
        }

        var connection = new NpgsqlConnection(connectionString);
        await using var connectionHandle = connection.ConfigureAwait(false);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

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

    private sealed class NotificationListener : IEventListener
    {
        private readonly string _connectionString;
        private readonly ILogger _logger;
        private readonly Channel<MagpieEvent> _channel = Channel.CreateUnbounded<MagpieEvent>();
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _listenLoop;

        public NotificationListener(string connectionString, ILogger logger)
        {
            _connectionString = new NpgsqlConnectionStringBuilder(connectionString)
            {
                KeepAlive = (int)KeepAliveInterval.TotalSeconds,
            }.ConnectionString;
            _logger = logger;
            _listenLoop = Task.Run(() => ListenAsync(_cts.Token), default);
        }

        public IAsyncEnumerable<MagpieEvent> ReadAllAsync(CancellationToken cancellationToken = default)
        {
            return _channel.Reader.ReadAllAsync(cancellationToken);
        }

        public void Dispose()
        {
            _cts.Cancel();
            _channel.Writer.TryComplete();
        }

        public async ValueTask DisposeAsync()
        {
            Dispose();

            try
            {
                await _listenLoop.ConfigureAwait(false);
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
                        "The PostgreSQL cluster backplane listener disconnected. The listener will retry in {Delay}.",
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

            _channel.Writer.TryComplete();
        }

        private void OnNotification(object sender, NpgsqlNotificationEventArgs e)
        {
            try
            {
                var magpieEvent = JsonSerializer.Deserialize<MagpieEvent>(e.Payload, SerializerOptions);
                if (magpieEvent is not null)
                {
                    _channel.Writer.TryWrite(magpieEvent);
                }
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "The listener discarded an unreadable cluster backplane notification payload.");
            }
        }
    }
}
