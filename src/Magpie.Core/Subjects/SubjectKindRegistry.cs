using System;
using System.Collections.Generic;
using System.Linq;

using Magpie.Models;
using Magpie.Sdk.Content;
using Magpie.Sdk.Services;

using Microsoft.Extensions.Logging;

namespace Magpie.Core.Subjects;

internal sealed class SubjectKindRegistry : ISubjectKindRegistry
{
    private readonly Dictionary<string, SubjectKindDefinition> _bySlug;

    public SubjectKindRegistry(
        IEnumerable<ISubjectKindDefinitionProvider> providers,
        ILogger<SubjectKindRegistry> logger)
    {
        ArgumentNullException.ThrowIfNull(providers);
        ArgumentNullException.ThrowIfNull(logger);

        var contributions = new List<(string Slug, SubjectKindDefinition Definition, Type ProviderType)>();
        foreach (var provider in providers)
        {
            IEnumerable<SubjectKindDefinition> definitions;
            try
            {
                definitions = provider.GetSubjectKindDefinitions() ?? [];
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "Subject kind provider {Provider} failed to supply definitions. The registry ignored this provider",
                    provider.GetType().FullName);
                continue;
            }

            foreach (var definition in definitions)
            {
                if (definition is null || string.IsNullOrWhiteSpace(definition.Slug))
                {
                    continue;
                }

                contributions.Add((definition.Slug, definition, provider.GetType()));
            }
        }

        var winners = BuildProviderMap(contributions, logger);
        _bySlug = winners.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Definition);

        Definitions = [.. _bySlug.Values];
    }

    public IReadOnlyList<SubjectKindDefinition> Definitions { get; }

    private static Dictionary<string, (string Slug, SubjectKindDefinition Definition, Type ProviderType)>
        BuildProviderMap(
            IEnumerable<(string Slug, SubjectKindDefinition Definition, Type ProviderType)> contributions,
            ILogger logger)
    {
        var map = new Dictionary<string, (string Slug, SubjectKindDefinition Definition, Type ProviderType)>();
        foreach (var group in contributions
            .Where(contribution => !string.IsNullOrWhiteSpace(contribution.Slug))
            .GroupBy(contribution => contribution.Slug))
        {
            var members = group.ToList();
            var winner = members[^1];
            if (members.Count > 1)
            {
                logger.LogWarning(
                    "Multiple {ItemLabel} declared {KeyLabel} {Key}. " +
                    "The registry used {Winner} and ignored {Displaced}",
                    "subject kind definition providers",
                    "slug",
                    group.Key,
                    winner.ProviderType.FullName,
                    string.Join(", ", members.Take(members.Count - 1).Select(member => member.ProviderType.FullName)));
            }

            map[group.Key] = winner;
        }

        return map;
    }

    public bool Contains(string slug) => !string.IsNullOrWhiteSpace(slug) && _bySlug.ContainsKey(slug);
}
