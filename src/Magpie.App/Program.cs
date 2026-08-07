using System.CommandLine;

using Magpie.App.Commands;
using Magpie.CommandLine.ApiProxy;
using Magpie.CommandLine.Dispatch;
using Magpie.Models;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;

var rootCommand = new RootCommand("magpie")
{
    new VersionCommand(),
    new RunCommand(),
    new SentinelCommand(),
};

rootCommand.AddApiProxyCommands(async cancellationToken =>
{
    var builder = WebApplication.CreateBuilder();

    builder.WebHost.UseTestServer();
    builder.AddMagpieCli();
    builder.AddMagpieSystem(new HostStatusInfo { Mode = HostMode.Default });

    await builder.MigrateMagpieDatabaseAsync(cancellationToken).ConfigureAwait(false);
    await builder.AddMagpieExtensionsAsync(cancellationToken).ConfigureAwait(false);

    var app = builder.Build();

    app.EnsureMagpieClusterBackplane();
    app.UseExceptionHandler();
    app.UseLocalSystemAuthentication();
    app.UseAuthorization();
    app.MapMagpieEndpoints();

    await app.DispatchLifecycleEventsAsync(cancellationToken).ConfigureAwait(false);
    await app.StartAsync(cancellationToken).ConfigureAwait(false);

    return app;
});

var parserConfiguration = new ParserConfiguration
{
    ResponseFileTokenReplacer = null
};

return await rootCommand.Parse(args, parserConfiguration).InvokeAsync().ConfigureAwait(false);
