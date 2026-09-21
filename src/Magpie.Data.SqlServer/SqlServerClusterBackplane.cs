using System;
using System.Collections.Generic;
using System.Data;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

using Magpie.Models;
using Magpie.Sdk.Extensions;
using Magpie.Sdk.Services;

using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace Magpie.Data.SqlServer;

internal sealed class SqlServerClusterBackplane(
    string connectionString,
    TimeProvider timeProvider,
    ILogger<SqlServerClusterBackplane> logger)
    : IClusterBackplane
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan RetentionPeriod = TimeSpan.FromMinutes(5);
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private const string TableName = "system_clusterEvent";

    public async Task BroadcastEventAsync(MagpieEvent magpieEvent, CancellationToken cancellationToken = default)
    {
        var payload = JsonSerializer.Serialize(magpieEvent, SerializerOptions);

        var connection = new SqlConnection(connectionString);
        await using var connectionHandle = connection.ConfigureAwait(false);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        var command = new SqlCommand(
            "INSERT INTO " + TableName + " (Payload, PublishedAt) VALUES (@payload, @publishedAt)",
            connection);
        await using var commandHandle = command.ConfigureAwait(false);
        command.Parameters.Add("@payload", SqlDbType.NVarChar, -1).Value = payload;
        command.Parameters.Add("@publishedAt", SqlDbType.DateTime2).Value = timeProvider.GetUtcNow().UtcDateTime;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public IEventListener CreateListener()
    {
        return new PollingListener(connectionString, timeProvider, logger);
    }

    private sealed class PollingListener : IEventListener
    {
        private readonly string _connectionString;
        private readonly TimeProvider _timeProvider;
        private readonly ILogger _logger;
        private readonly Channel<MagpieEvent> _channel = Channel.CreateUnbounded<MagpieEvent>();
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _pollLoop;

        public PollingListener(string connectionString, TimeProvider timeProvider, ILogger logger)
        {
            _connectionString = connectionString;
            _timeProvider = timeProvider;
            _logger = logger;
            _pollLoop = Task.Run(() => PollAsync(_cts.Token), default);
        }

        private static async Task<long> ReadLatestSequenceAsync(
            SqlConnection connection,
            CancellationToken cancellationToken)
        {
            var command = new SqlCommand("SELECT ISNULL(MAX(Seq), 0) FROM " + TableName, connection);
            await using var commandHandle = command.ConfigureAwait(false);

            return (long)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!;
        }

        private async Task DeleteExpiredEventsAsync(
            SqlConnection connection,
            CancellationToken cancellationToken)
        {
            var command = new SqlCommand("DELETE FROM " + TableName + " WHERE PublishedAt < @threshold", connection);
            await using var commandHandle = command.ConfigureAwait(false);
            command.Parameters.Add("@threshold", SqlDbType.DateTime2).Value =
                _timeProvider.GetUtcNow().UtcDateTime - RetentionPeriod;
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
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
                await _pollLoop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }

            _cts.Dispose();
        }

        private async Task PollAsync(CancellationToken cancellationToken)
        {
            long? committed = null;
            var cursor = 0L;
            var lastCleanup = _timeProvider.GetUtcNow().UtcDateTime;
            var delay = PollInterval;
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var connection = new SqlConnection(_connectionString);
                    await using var connectionHandle = connection.ConfigureAwait(false);
                    await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

                    var latest = await ReadLatestSequenceAsync(connection, cancellationToken).ConfigureAwait(false);
                    if (committed == null)
                    {
                        cursor = latest;
                    }
                    else if (committed.Value > cursor)
                    {
                        await ReadNewEventsAsync(connection, cursor, committed.Value, cancellationToken)
                            .ConfigureAwait(false);
                        cursor = committed.Value;
                    }

                    committed = latest;

                    if (_timeProvider.GetUtcNow().UtcDateTime - lastCleanup >= CleanupInterval)
                    {
                        await DeleteExpiredEventsAsync(connection, cancellationToken).ConfigureAwait(false);
                        lastCleanup = _timeProvider.GetUtcNow().UtcDateTime;
                    }

                    delay = PollInterval;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, MaxRetryDelay.Ticks));
                    _logger.LogWarning(ex, "The SQL Server cluster backplane poll failed, so the " +
                        "listener retries in {Delay}.", delay);
                }

                try
                {
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }

            _channel.Writer.TryComplete();
        }

        private async Task ReadNewEventsAsync(
            SqlConnection connection,
            long cursor,
            long committed,
            CancellationToken cancellationToken)
        {
            var command = new SqlCommand(
                "SELECT Seq, Payload FROM " + TableName + " WHERE Seq > @cursor AND Seq <= @committed ORDER BY Seq",
                connection);
            await using var commandHandle = command.ConfigureAwait(false);
            command.Parameters.Add("@cursor", SqlDbType.BigInt).Value = cursor;
            command.Parameters.Add("@committed", SqlDbType.BigInt).Value = committed;

            var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            await using var readerHandle = reader.ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var sequence = reader.GetInt64(0);

                try
                {
                    var magpieEvent = JsonSerializer.Deserialize<MagpieEvent>(reader.GetString(1), SerializerOptions);
                    if (magpieEvent is not null)
                    {
                        _channel.Writer.TryWrite(magpieEvent);
                    }
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(
                        ex,
                        "The listener discarded an unreadable cluster backplane relay payload at sequence {Sequence}.",
                        sequence);
                }
            }
        }
    }
}
