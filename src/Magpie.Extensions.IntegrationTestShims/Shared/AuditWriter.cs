using System;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Magpie.Extensions.IntegrationTestShims.Shared;

internal sealed class AuditWriter(string pluginSlug)
{
    private const string RootEnvironmentVariable = "MAGPIE_TEST_FIXTURES_ROOT";
    private const string DefaultRoot = "/test-fixtures";

    private static readonly JsonSerializerOptions DefaultOptions =
        new(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
            ReferenceHandler = ReferenceHandler.IgnoreCycles,
        };

    private readonly string _pluginSlug = string.IsNullOrWhiteSpace(pluginSlug)
        ? throw new ArgumentException("Plugin slug must be non-empty.", nameof(pluginSlug))
        : pluginSlug;

    private readonly object _fileNameLock = new();
    private long _lastTimestampTicks;
    private int _sameMillisecondCounter;

    private static string Sanitize(string methodName)
    {
        var buffer = new char[methodName.Length];
        for (var i = 0; i < methodName.Length; i++)
        {
            var ch = methodName[i];
            buffer[i] = char.IsLetterOrDigit(ch) || ch == '-' || ch == '_' ? ch : '_';
        }

        return new string(buffer);
    }

    public async Task WriteAsync<TRequest, TResponse>(
        string methodName,
        TRequest request,
        TResponse response,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(methodName))
        {
            throw new ArgumentException("Method name must be non-empty.", nameof(methodName));
        }

        var auditRoot = AuditRoot();
        Directory.CreateDirectory(auditRoot);

        var record = new AuditRecord<TRequest, TResponse>
        {
            Plugin = _pluginSlug,
            Method = methodName,
            TimestampUtc = DateTime.UtcNow,
            Request = request,
            Response = response,
        };

        var finalPath = Path.Combine(auditRoot, BuildFileName(methodName, record.TimestampUtc));
        var tempPath = finalPath + ".tmp";

        using (var stream = new FileStream(
            tempPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 4096,
            useAsync: true))
        {
            await JsonSerializer
                .SerializeAsync(stream, record, DefaultOptions, cancellationToken)
                .ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        if (File.Exists(finalPath))
        {
            File.Delete(finalPath);
        }

        File.Move(tempPath, finalPath);
    }

    private string BuildFileName(string methodName, DateTime timestampUtc)
    {
        var stamp = timestampUtc.ToString("yyyyMMddHHmmssfff", CultureInfo.InvariantCulture);
        var ticks = timestampUtc.Ticks - timestampUtc.Ticks % TimeSpan.TicksPerMillisecond;
        int suffix;

        lock (_fileNameLock)
        {
            if (ticks == _lastTimestampTicks)
            {
                _sameMillisecondCounter++;
                suffix = _sameMillisecondCounter;
            }
            else
            {
                _lastTimestampTicks = ticks;
                _sameMillisecondCounter = 0;
                suffix = 0;
            }
        }

        var sanitizedMethod = Sanitize(methodName);
        return $"{stamp}-{suffix:D4}-{sanitizedMethod}.json";
    }

    private string AuditRoot()
    {
        var overrideRoot = Environment.GetEnvironmentVariable(RootEnvironmentVariable);
        var root = string.IsNullOrWhiteSpace(overrideRoot) ? DefaultRoot : overrideRoot!;
        return Path.Combine(root, _pluginSlug, "audit");
    }

    public sealed class AuditRecord<TRequest, TResponse>
    {
        public string Plugin { get; set; } = string.Empty;

        public string Method { get; set; } = string.Empty;

        public DateTime TimestampUtc { get; set; }

        public TRequest? Request { get; set; }

        public TResponse? Response { get; set; }
    }
}
