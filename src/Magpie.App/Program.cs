using System.CommandLine;

using Magpie.App.Commands;

var rootCommand = new RootCommand("magpie")
{
    new VersionCommand(),
    new RunCommand()
};

return await rootCommand.Parse(args)
    .InvokeAsync()
    .ConfigureAwait(false);
