using System.CommandLine;

var rootCommand = new RootCommand("magpie");

return await rootCommand.Parse(args)
    .InvokeAsync()
    .ConfigureAwait(false);
