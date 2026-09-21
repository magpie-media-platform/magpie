using System;
using System.CommandLine;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models;

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting.Systemd;
using Microsoft.Extensions.Hosting.WindowsServices;

namespace Magpie.App.Commands;

internal sealed class RunCommand : Command
{
    private readonly Option<bool> _sentinelSupervisedOption = new("--sentinel-supervised")
    {
        Hidden = true,
        DefaultValueFactory = _ => false
    };

    public RunCommand() : base("run", "Run the Magpie HTTP host")
    {
        Add(_sentinelSupervisedOption);
        SetAction(ExecuteAsync);
    }

    private async Task<int> ExecuteAsync(ParseResult parseResult, CancellationToken cancellationToken)
    {
        var hostStatus = new HostStatusInfo
        {
            Mode = parseResult.GetValue(_sentinelSupervisedOption) switch
            {
                true => HostMode.Sentinel,
                false when WindowsServiceHelpers.IsWindowsService() => HostMode.WindowsService,
                false when SystemdHelpers.IsSystemdService() => HostMode.Systemd,
                false => HostMode.Default
            }
        };

        var builder = WebApplication.CreateBuilder();

        builder.AddMagpie();
        builder.Services.AddMagpieAuthenticationScheme();
        builder.AddMagpieSystem(hostStatus);

        await builder.PrepareMagpieDatabaseAsync(cancellationToken).ConfigureAwait(false);
        await builder.AddMagpieExtensionsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);

        var app = builder.Build();

        app.EnsureMagpieClusterBackplane();

        app.UseMagpieRequestPipeline();

        await app.RunAsync().ConfigureAwait(false);

        return hostStatus.ShutdownExitCode ?? Environment.ExitCode;
    }
}
