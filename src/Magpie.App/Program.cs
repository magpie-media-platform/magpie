using System.CommandLine;
using System.Linq;

using Magpie.App.Commands;

var serveCommand = new ServeCommand();
var versionCommand = new VersionCommand();

var rootCommand = new RootCommand("magpie");
rootCommand.Subcommands.Add(serveCommand);
rootCommand.Subcommands.Add(versionCommand);

rootCommand.SetAction((parseResult, cancellationToken) => serveCommand
    .Parse([.. parseResult.Tokens.Select(t => t.Value)])
    .InvokeAsync(cancellationToken: cancellationToken));

return await rootCommand.Parse(args)
    .InvokeAsync()
    .ConfigureAwait(false);
