using System;
using System.CommandLine;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Builder;

namespace Magpie.CommandLine.ApiProxy;

/// <summary>
/// The command extensions for the Magpie API proxy.
/// </summary>
public static class CommandExtensions
{
    /// <summary>
    /// Adds the subcommands that proxy to the Magpie HTTP API.
    /// </summary>
    /// <param name="rootCommand">The parent command</param>
    /// <param name="hostFactory">The factory that creates the in-process web application</param>
    public static void AddApiProxyCommands(
        this Command rootCommand,
        Func<CancellationToken, Task<WebApplication>> hostFactory)
    {
        foreach (var groupEntries in MagpieGeneratedCommandManifest.Entries
            .GroupBy(entry => entry.Group, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
        {
            var groupCommand = FindSubcommand(rootCommand, groupEntries.Key);
            if (groupCommand is null)
            {
                var description = groupEntries
                    .Select(entry => entry.GroupDescription)
                    .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
                    ?? $"Work with {groupEntries.Key}";
                groupCommand = new(groupEntries.Key, description);
                rootCommand.Subcommands.Add(groupCommand);
            }

            foreach (var entry in groupEntries.OrderBy(value => value.Command, StringComparer.OrdinalIgnoreCase))
            {
                if (FindSubcommand(groupCommand, entry.Command) is null)
                {
                    groupCommand.Subcommands.Add(new ApiGeneratedCommand(entry, hostFactory));
                }
            }
        }
    }

    private static Command? FindSubcommand(Command command, string name)
    {
        return command.Subcommands.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase));
    }
}
