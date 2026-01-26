using System;
using System.CommandLine;
using System.Reflection;

namespace Magpie.App.Commands;

/// <summary>
/// Command to display version information.
/// </summary>
internal sealed class VersionCommand : Command
{
    public VersionCommand() : base("version", "Show version information")
    {
        SetAction(Execute);
    }

    private static int Execute(ParseResult parseResult)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var version = assembly.GetName().Version?.ToString() ?? "0.0.0";
        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? version;

        Console.WriteLine($"Magpie {informationalVersion}");

        return 0;
    }
}
