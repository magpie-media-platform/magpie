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
                    "The subject kind registry ignored provider {Provider} because it failed to supply " +
                    "definitions",
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
        var map = new Dictionary<string, (string Slug, SubjectKindDefinition Definition, Type ProviderType)>(
            StringComparer.Ordinal);
        foreach (var group in contributions
            .Where(contribution => !string.IsNullOrWhiteSpace(contribution.Slug))
            .GroupBy(contribution => contribution.Slug, StringComparer.Ordinal))
        {
            var members = group.ToList();
            if (members.Count > 1)
            {
                logger.LogWarning(
                    "Multiple subject kind definition providers declared slug {Slug}; the registry used " +
                    "{Winner} and ignored {Displaced}",
                    group.Key,
                    members[^1].ProviderType.FullName,
                    string.Join(
                        ", ",
                        members.Take(members.Count - 1).Select(member => member.ProviderType.FullName)));
            }

            map[group.Key] = members[^1];
        }

        return map;
    }

    public bool Contains(string slug) => !string.IsNullOrWhiteSpace(slug) && _bySlug.ContainsKey(slug);
}
