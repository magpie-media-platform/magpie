using System.CommandLine;

using Magpie.App.Commands;
using Magpie.App.Hosting;
using Magpie.CommandLine.ApiProxy;
using Magpie.CommandLine.Dispatch;
using Magpie.Models;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

var rootCommand = new RootCommand("magpie") { new VersionCommand(), new RunCommand(), new SentinelCommand() };

rootCommand.AddApiProxyCommands(async cancellationToken =>
{
    var builder = WebApplication.CreateBuilder();

    builder.WebHost.UseTestServer();
    builder.AddMagpieCli();
    builder.AddMagpieSystem(new HostStatusInfo { Mode = HostMode.Default });

    await builder.PrepareMagpieDatabaseAsync(cancellationToken).ConfigureAwait(false);
    await builder.AddMagpieExtensionsAsync(cancellationToken).ConfigureAwait(false);

    var app = builder.Build();

    app.EnsureMagpieClusterBackplane();
    app.UseExceptionHandler();
    app.UseLocalSystemAuthentication();
    app.UseAuthorization();
    app.MapMagpieEndpoints();

    await app.Services.GetRequiredService<DatabaseStartupService>().RunAsync(cancellationToken)
        .ConfigureAwait(false);
    await app.StartAsync(cancellationToken).ConfigureAwait(false);

    return app;
});

var parserConfiguration = new ParserConfiguration { ResponseFileTokenReplacer = null };

var parseResult = rootCommand.Parse(args, parserConfiguration);
var invocationConfiguration = parseResult.CommandResult.Command is SentinelCommand
    ? new InvocationConfiguration { ProcessTerminationTimeout = null }
    : null;

return await parseResult.InvokeAsync(invocationConfiguration).ConfigureAwait(false);
