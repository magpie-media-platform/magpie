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
    public SentinelCommand() : base("sentinel", "Run Magpie under the sentinel supervisor")
    {
        SetAction(ExecuteAsync);
    }

    private static async Task<int> ExecuteAsync(ParseResult parseResult, CancellationToken cancellationToken)
    {
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        using var stopCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
        {
            e.Cancel = true;
            stopCts.Cancel();
        }

        Console.CancelKeyPress += OnCancelKeyPress;
        try
        {
            return await RunLoopAsync(stopCts.Token).ConfigureAwait(false);
        }
        finally
        {
            Console.CancelKeyPress -= OnCancelKeyPress;
        }
    }

    private static async Task<int> RunLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            using var child = StartChild();
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

            if (exitCode == MagpieExitCodes.Shutdown)
            {
                return 0;
            }
        }

        return 0;
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
