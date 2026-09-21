using System.Runtime.Versioning;

using Magpie.Abstractions;
using Magpie.Models;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.WindowsServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Magpie.App.Hosting;

[SupportedOSPlatform("windows")]
internal sealed class RestartAwareWindowsServiceLifetime(
    HostStatusInfo hostStatus,
    IHostEnvironment environment,
    IHostApplicationLifetime applicationLifetime,
    ILoggerFactory loggerFactory,
    IOptions<HostOptions> hostOptions,
    IOptions<WindowsServiceLifetimeOptions> windowsServiceOptions)
    : WindowsServiceLifetime(environment, applicationLifetime, loggerFactory, hostOptions, windowsServiceOptions)
{
    private const int RecoveryRequestedWin32ExitCode = 1;

    protected override void OnStop()
    {
        if (hostStatus.ShutdownExitCode is MagpieExitCodes.Restart or MagpieExitCodes.StartupFailed)
        {
            ExitCode = RecoveryRequestedWin32ExitCode;
        }

        base.OnStop();
    }
}
