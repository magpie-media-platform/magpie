using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Extensions.OpenLibrary.Mapping;
using Magpie.Extensions.OpenLibrary.QueryEngine;
using Magpie.Models;
using Magpie.Models.Data;
using Magpie.Sdk.Content;

using Microsoft.Extensions.Logging;

namespace Magpie.Extensions.OpenLibrary;

internal sealed class ContentMetadataProvider(
    IEnumerable<IContentTypeDefinitionProvider> contentTypeDefinitionProviders,
    ISearchEngine searchEngine,
    TimeProvider timeProvider,
    ILogger<ContentMetadataProvider> logger) : IContentHierarchyMetadataRelationshipProvider
{
    private const int ContributorCap = 10;

    private static readonly Regex LegacyContribution = new(
        "^(?<name>[^()]+?)\\s*\\((?<role>[^()]+)\\)$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private readonly IReadOnlyList<BookHierarchy> _hierarchies = contentTypeDefinitionProviders.ToBookHierarchies();

    public string ProviderType => Constants.ProviderType;

    public IEnumerable<string> SupportedContentTypes =>
        _hierarchies.Select(hierarchy => hierarchy.ContentTypeSlug).Distinct(StringComparer.Ordinal);

    public async Task<ContentHierarchyMetadataResult?> TryGetMetadataAsync(
        MetadataFetchRequest request,
        CancellationToken cancellationToken = default)
    {
        var hierarchy = _hierarchies.FindBookHierarchy(request.Item.ContentType, request.Item.Level);
        if (hierarchy is null)
        {
            return null;
        }

        try
        {
            return await GetBookMetadataAsync(request, hierarchy, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException or InvalidOperationException)
        {
            logger.LogWarning(ex, "Open Library metadata refresh failed");
            return null;
        }
    }

    public async Task<IReadOnlyList<SubjectRelationshipDefinition>> GetRelationshipsAsync(
        MetadataRelationshipRequest request,
        CancellationToken cancellationToken = default)
    {
        var item = request.Item;
        var hierarchy = _hierarchies.FindBookHierarchy(item.ContentType, item.Level);
        if (hierarchy is null)
        {
            return [];
        }

        var contributions = new[] { request.Contribution }.Concat(request.ExistingMetadata).ToArray();
        var source = Constants.ProviderType;
        var value = contributions.FindExternalId(source);
        if (value is null)
        {
            source = Constants.IsbnSource;
            value = contributions.FindExternalId(source);
        }

        if (value is null)
        {
            return [];
        }

        try
        {
            var resolution = await searchEngine.ResolveWorkAsync(
                new QueryRequest
                {
                    Intent = QueryIntent.WorkResolution,
                    Hierarchy = hierarchy,
                    Source = source,
                    Value = value,
                    LanguagePreference = item.LanguagePreferences.FirstOrDefault(preference =>
                        !string.IsNullOrWhiteSpace(preference))
                        ?? request.Contribution.Language
                            ?? string.Empty
                },
                cancellationToken).ConfigureAwait(false);
            if (resolution.Hit is null)
            {
                return [];
            }

            var edition = await searchEngine.GetRepresentativeEditionAsync(resolution.Hit, cancellationToken)
                .ConfigureAwait(false);
            return ToRelatedSubjects(resolution.Hit, edition);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException or InvalidOperationException)
        {
            logger.LogWarning(ex, "Open Library failed to expand related subjects");
            return [];
        }
    }

    private string PublisherUrlSlug(string publisher) => Uri.EscapeDataString(publisher.Replace(' ', '_'));

    private string ContributorRelationshipKind(string role)
    {
        return role.ToLowerInvariant() switch
        {
            "translator" => Constants.TranslatorRelationshipKind,
            "illustrator" => Constants.IllustratorRelationshipKind,
            "editor" => Constants.EditorRelationshipKind,
            _ => Constants.ContributorRelationshipKind
        };
    }

    private List<(string Role, string Name)> EditionContributors(EditionDto edition, ref int skipped)
    {
        var contributors = new List<(string Role, string Name)>();
        foreach (var contributor in edition.Contributors ?? [])
        {
            var role = contributor.Role?.Trim();
            var name = contributor.Name?.Trim();
            if (string.IsNullOrEmpty(name))
            {
                skipped++;
                continue;
            }

            contributors.Add((string.IsNullOrEmpty(role) ? "Contributor" : role, name));
        }

        foreach (var contribution in edition.Contributions ?? [])
        {
            var match = LegacyContribution.Match(contribution ?? string.Empty);
            if (!match.Success)
            {
                skipped++;
                continue;
            }

            contributors.Add((match.Groups["role"].Value.Trim(), match.Groups["name"].Value.Trim()));
        }

        return contributors;
    }

    private void AddSubjectFacets(
        List<SubjectRelationshipDefinition> result,
        IReadOnlyList<SubjectFacetPair> facets,
        string kind,
        string externalIdSource)
    {
        var added = 0;
        foreach (var facet in facets)
        {
            if (added == Constants.SubjectFacetCap)
            {
                break;
            }

            var identity = facet.FacetIdentity();
            if (string.IsNullOrEmpty(identity))
            {
                continue;
            }

            added++;
            var contribution = new MetadataContribution
            {
                ProviderType = Constants.ProviderType,
                Title = facet.Name,
                DateLastRefreshed = timeProvider.GetUtcNow().UtcDateTime
            };
            contribution.ExternalIds.Add(new ExternalId
            {
                Source = externalIdSource,
                Value = identity,
                Ordinal = 0
            });
            result.Add(new SubjectRelationshipDefinition
            {
                RelationshipKind = kind,
                RelationshipOrdinal = result.Count,
                SubjectKind = kind,
                SubjectName = facet.Name,
                SubjectExternalIds = new Dictionary<string, string>
                {
                    [externalIdSource] = identity
                },
                InitialSubjectContribution = contribution
            });
        }
    }

    private IReadOnlyList<SubjectRelationshipDefinition> ToRelatedSubjects(WorkHit hit, EditionDto? edition)
    {
        var result = ToAuthorRelationships(hit);
        result.AddRange(ToPublisherRelationships(hit, result.Count));
        AddSubjectFacets(result, hit.Persons, Constants.PersonSubjectKind, Constants.ProviderType);
        AddSubjectFacets(result, hit.Places, Constants.PlaceSubjectKind, Constants.ProviderType);
        AddSubjectFacets(result, hit.Times, Constants.TimeSubjectKind, Constants.ProviderType);
        if (edition is not null)
        {
            AddContributors(result, edition);
        }

        return result;
    }

    private List<SubjectRelationshipDefinition> ToAuthorRelationships(WorkHit hit)
    {
        var result = new List<SubjectRelationshipDefinition>(hit.Authors.Count);
        foreach (var author in hit.Authors)
        {
            var contribution = new MetadataContribution
            {
                ProviderType = Constants.ProviderType,
                Title = author.Name,
                DateLastRefreshed = timeProvider.GetUtcNow().UtcDateTime
            };
            contribution.ExternalIds.Add(new ExternalId
            {
                Source = Constants.ProviderType,
                Value = author.Olid,
                Ordinal = 0
            });
            result.Add(new SubjectRelationshipDefinition
            {
                RelationshipKind = Constants.AuthorRelationshipKind,
                RelationshipOrdinal = result.Count,
                SubjectKind = Constants.PersonSubjectKind,
                SubjectName = author.Name,
                SubjectExternalIds = new Dictionary<string, string>
                {
                    [Constants.ProviderType] = author.Olid
                },
                InitialSubjectContribution = contribution
            });
        }

        return result;
    }

    private void AddContributors(List<SubjectRelationshipDefinition> result, EditionDto edition)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var skipped = 0;
        foreach (var (role, name) in EditionContributors(edition, ref skipped))
        {
            var kind = ContributorRelationshipKind(role);
            counts.TryGetValue(kind, out var count);
            if (count >= ContributorCap)
            {
                continue;
            }

            var identity = name.NormalizeSubjectKey();
            if (string.IsNullOrEmpty(identity))
            {
                continue;
            }

            counts[kind] = count + 1;
            var contribution = new MetadataContribution
            {
                ProviderType = Constants.ProviderType,
                Title = name,
                DateLastRefreshed = timeProvider.GetUtcNow().UtcDateTime
            };
            contribution.ExternalIds.Add(new ExternalId
            {
                Source = Constants.ProviderType,
                Value = identity,
                Ordinal = 0
            });
            result.Add(new SubjectRelationshipDefinition
            {
                RelationshipKind = kind,
                RelationshipQualifier = role,
                RelationshipOrdinal = result.Count,
                SubjectKind = Constants.PersonSubjectKind,
                SubjectName = name,
                SubjectExternalIds = new Dictionary<string, string>
                {
                    [Constants.ProviderType] = identity
                },
                InitialSubjectContribution = contribution
            });
        }

        if (skipped > 0)
        {
            logger.LogDebug(
                "Open Library skipped {SkippedCount} unparsable legacy contribution strings",
                skipped);
        }
    }

    private List<SubjectRelationshipDefinition> ToPublisherRelationships(WorkHit hit, int firstOrdinal)
    {
        var result = new List<SubjectRelationshipDefinition>();
        foreach (var publisher in hit.Publishers)
        {
            if (result.Count == Constants.PublisherCap)
            {
                break;
            }

            var identity = publisher.NormalizeSubjectKey();
            if (string.IsNullOrEmpty(identity))
            {
                continue;
            }

            var contribution = new MetadataContribution
            {
                ProviderType = Constants.ProviderType,
                Title = publisher,
                DateLastRefreshed = timeProvider.GetUtcNow().UtcDateTime
            };
            contribution.ExternalIds.Add(new ExternalId
            {
                Source = Constants.ProviderType,
                Value = identity,
                Ordinal = 0
            });
            if (Uri.TryCreate(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "https://openlibrary.org/publishers/{0}",
                    PublisherUrlSlug(publisher)),
                UriKind.Absolute,
                out var publisherPage))
            {
                contribution.References.Add(new Reference
                {
                    Kind = "canonical",
                    Source = Constants.ProviderType,
                    Url = publisherPage,
                    DisplayName = "Open Library",
                    Ordinal = 0
                });
            }

            result.Add(new SubjectRelationshipDefinition
            {
                RelationshipKind = Constants.PublisherRelationshipKind,
                RelationshipOrdinal = firstOrdinal + result.Count,
                SubjectKind = Constants.PublisherSubjectKind,
                SubjectName = publisher,
                SubjectExternalIds = new Dictionary<string, string>
                {
                    [Constants.ProviderType] = identity
                },
                InitialSubjectContribution = contribution
            });
        }

        return result;
    }

    private async Task<ContentHierarchyMetadataResult?> GetBookMetadataAsync(
        MetadataFetchRequest request,
        BookHierarchy hierarchy,
        CancellationToken cancellationToken)
    {
        var source = Constants.ProviderType;
        var value = request.ExistingMetadata.FindExternalId(source);
        if (value is null)
        {
            source = Constants.IsbnSource;
            value = request.ExistingMetadata.FindExternalId(source);
        }

        if (value is null)
        {
            return null;
        }

        var resolution = await searchEngine.ResolveWorkAsync(
            new QueryRequest
            {
                Intent = QueryIntent.WorkResolution,
                Hierarchy = hierarchy,
                Source = source,
                Value = value,
                LanguagePreference = request.DefaultLanguagePreference
            },
            cancellationToken).ConfigureAwait(false);
        if (resolution.Hit is null)
        {
            return null;
        }

        var edition = await searchEngine.GetRepresentativeEditionAsync(resolution.Hit, cancellationToken)
            .ConfigureAwait(false);
        return new ContentHierarchyMetadataResult
        {
            Contribution = resolution.Hit.ToWorkContribution(
                request.DefaultLanguagePreference,
                timeProvider.GetUtcNow().UtcDateTime,
                edition),
            Children = []
        };
    }
}
