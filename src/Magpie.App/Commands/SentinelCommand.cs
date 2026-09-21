using System;
using System.CommandLine;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;

using Microsoft.Extensions.Logging;

namespace Magpie.App.Commands;

internal sealed class SentinelCommand : Command
{
    private const int MaxConsecutiveFailedStarts = 5;

    private static readonly TimeSpan FirstRelaunchDelay = TimeSpan.FromSeconds(5);

    private static readonly TimeSpan MaxRelaunchDelay = TimeSpan.FromMinutes(1);

    private static readonly TimeSpan StableRunDuration = TimeSpan.FromMinutes(1);

    public SentinelCommand() : base("sentinel", "Run Magpie under the sentinel supervisor")
    {
        SetAction(ExecuteAsync);
    }

    private static int CarryFailedStarts(int failedStarts, TimeSpan uptime)
    {
        return uptime >= StableRunDuration ? 0 : failedStarts;
    }

    private static TimeSpan ResolveRelaunchDelay(int attempt)
    {
        var seconds = FirstRelaunchDelay.TotalSeconds * Math.Pow(2, attempt - 1);

        return seconds < MaxRelaunchDelay.TotalSeconds ? TimeSpan.FromSeconds(seconds) : MaxRelaunchDelay;
    }

    private static async Task<int> ExecuteAsync(ParseResult parseResult, CancellationToken cancellationToken)
    {
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var logger = loggerFactory.CreateLogger<SentinelCommand>();
        using var stopCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
        {
            e.Cancel = true;
            stopCts.Cancel();
        }

        Console.CancelKeyPress += OnCancelKeyPress;
        try
        {
            return await RunLoopAsync(logger, stopCts.Token).ConfigureAwait(false);
        }
        finally
        {
            Console.CancelKeyPress -= OnCancelKeyPress;
        }
    }

    private static async Task<int> RunLoopAsync(ILogger logger, CancellationToken cancellationToken)
    {
        var failedStarts = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            using var child = StartChild();
            var startedAt = Stopwatch.GetTimestamp();
            try
            {
                await child.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                StopChild(child);
                return 0;
            }

            var exitCode = child.ExitCode;
            var uptime = Stopwatch.GetElapsedTime(startedAt);
            failedStarts = CarryFailedStarts(failedStarts, uptime);

            if (exitCode == MagpieExitCodes.Restart)
            {
                failedStarts = 0;
                continue;
            }

            if (exitCode is MagpieExitCodes.Shutdown or 0)
            {
                return 0;
            }

            failedStarts++;
            if (failedStarts >= MaxConsecutiveFailedStarts)
            {
                logger.LogCritical(
                    "The host exited unexpectedly {FailedStarts} times with exit code {ExitCode}, so the " +
                    "sentinel stops",
                    failedStarts,
                    exitCode);

                return exitCode;
            }

            var delay = ResolveRelaunchDelay(failedStarts);
            logger.LogError(
                "The host exited unexpectedly with exit code {ExitCode}, so the sentinel will relaunch it in " +
                "{DelaySeconds} seconds on attempt {Attempt} of {MaxAttempts}",
                exitCode,
                delay.TotalSeconds,
                failedStarts,
                MaxConsecutiveFailedStarts);

            if (!await DelayRelaunchAsync(delay, cancellationToken).ConfigureAwait(false))
            {
                return 0;
            }
        }

        return 0;
    }

    private static async Task<bool> DelayRelaunchAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return false;
        }

        return true;
    }

    private static Process StartChild()
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = ResolveProcessPath(),
            UseShellExecute = false,
            RedirectStandardInput = false,
            RedirectStandardOutput = false,
            RedirectStandardError = false
        };

        startInfo.ArgumentList.Add("run");
        startInfo.ArgumentList.Add("--sentinel-supervised");

        var child = new Process { StartInfo = startInfo };
        try
        {
            if (!child.Start())
            {
                child.Dispose();
                throw new InvalidOperationException("Failed to start child host process.");
            }
        }
        catch
        {
            child.Dispose();
            throw;
        }

        return child;
    }

    private static void StopChild(Process child)
    {
        if (child.HasExited)
        {
            return;
        }

        child.Kill(entireProcessTree: true);
    }

    private static string ResolveProcessPath()
    {
        if (!string.IsNullOrWhiteSpace(Environment.ProcessPath))
        {
            return Environment.ProcessPath;
        }

        using var current = Process.GetCurrentProcess();
        return current.MainModule?.FileName ?? throw new InvalidOperationException("Unable to resolve process path.");
    }
}
