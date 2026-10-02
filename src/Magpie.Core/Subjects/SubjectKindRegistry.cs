using System;
using System.Collections.Generic;

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
        _bySlug = new(StringComparer.Ordinal);
        var providerBySlug = new Dictionary<string, string?>(StringComparer.Ordinal);
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
                    "The subject kind registry ignored provider {Provider} because it failed to supply definitions",
                    provider.GetType().FullName);
                continue;
            }

            foreach (var definition in definitions)
            {
                if (definition is null || string.IsNullOrWhiteSpace(definition.Slug))
                {
                    continue;
                }

                if (providerBySlug.TryGetValue(definition.Slug, out var displaced))
                {
                    logger.LogWarning(
                        "Multiple subject kind definition providers declared slug {Slug}; the registry used " +
                        "{Winner} and ignored {Displaced}",
                        definition.Slug,
                        provider.GetType().FullName,
                        displaced);
                }

                _bySlug[definition.Slug] = definition;
                providerBySlug[definition.Slug] = provider.GetType().FullName;
            }
        }

        Definitions = [.. _bySlug.Values];
    }

    public IReadOnlyList<SubjectKindDefinition> Definitions { get; }

    public bool Contains(string slug) => !string.IsNullOrWhiteSpace(slug) && _bySlug.ContainsKey(slug);
}
