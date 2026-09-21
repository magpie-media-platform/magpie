using System;
using System.CommandLine;
using System.Reflection;

namespace Magpie.App.Commands;

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
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ??
            version;

#pragma warning disable RS0030
        Console.WriteLine($"Magpie {informationalVersion}");
#pragma warning restore RS0030

        return 0;
    }
}
