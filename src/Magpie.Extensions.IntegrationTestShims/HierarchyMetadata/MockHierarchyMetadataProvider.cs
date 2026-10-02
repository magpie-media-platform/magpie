using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Extensions.IntegrationTestShims.Shared;
using Magpie.Models;
using Magpie.Models.Data;
using Magpie.Sdk.Content;

namespace Magpie.Extensions.IntegrationTestShims.HierarchyMetadata;

internal sealed class MockHierarchyMetadataProvider(string slug) : IContentHierarchyMetadataRelationshipProvider
{
    private readonly AuditWriter _audit = new(slug);
    private readonly LatencyInjector _latency = new(slug);
    private readonly ScenarioReader _scenarios = new(slug);

    public string ProviderType { get; } = slug;

    public IEnumerable<string> SupportedContentTypes => ["*"];

    public async Task<ContentHierarchyMetadataResult?> TryGetMetadataAsync(
        MetadataFetchRequest request,
        CancellationToken cancellationToken = default)
    {
        var contentHierarchyItem = request.Item;
        var existingMetadata = request.ExistingMetadata;
        var ancestorLanguagePreferences = request.AncestorLanguagePreferences;
        var defaultLanguagePreference = request.DefaultLanguagePreference;
        var ancestorMetadata = request.AncestorMetadata;
        var externalId = ResolveExternalId(existingMetadata);

        const string method = "tryGetMetadataAsync";
        await _latency.DelayAsync(method, cancellationToken).ConfigureAwait(false);

        var contribution = await _scenarios
            .ReadAsync<MetadataContribution>(method, externalId, cancellationToken)
            .ConfigureAwait(false);

        await _audit
            .WriteAsync(
                method,
                new
                {
                    contentHierarchyItem,
                    existingMetadata,
                    ancestorLanguagePreferences,
                    defaultLanguagePreference,
                    ancestorMetadata,
                },
                contribution,
                cancellationToken)
            .ConfigureAwait(false);

        var children = await DiscoverChildrenAsync(
            contentHierarchyItem,
            existingMetadata,
            externalId,
            cancellationToken)
            .ConfigureAwait(false);

        return new ContentHierarchyMetadataResult { Contribution = contribution, Children = children };
    }

    private async Task<IReadOnlyList<ChildHierarchyDefinition>> DiscoverChildrenAsync(
        ContentHierarchyItem parent,
        IEnumerable<MetadataContribution> existingMetadata,
        string? externalId,
        CancellationToken cancellationToken)
    {
        const string method = "expandChildrenAsync";
        await _latency.DelayAsync(method, cancellationToken).ConfigureAwait(false);

        var response = await _scenarios
            .ReadAsync<List<ChildHierarchyDefinition>>(method, externalId, cancellationToken)
            .ConfigureAwait(false);

        await _audit
            .WriteAsync(method, new { parent, existingMetadata }, response, cancellationToken)
            .ConfigureAwait(false);

        return response ?? [];
    }

    private string? ResolveExternalId(IEnumerable<MetadataContribution> existingMetadata)
    {
        return existingMetadata
            .Where(contribution => string.Equals(contribution.ProviderType, ProviderType, StringComparison.Ordinal))
            .OrderBy(contribution => contribution.Ordinal)
            .SelectMany(contribution => contribution.ExternalIds.OrderBy(externalId => externalId.Ordinal))
            .Select(externalId => externalId.Value)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    }

    public async Task<IReadOnlyList<SubjectRelationshipDefinition>> GetRelationshipsAsync(
        MetadataRelationshipRequest request,
        CancellationToken cancellationToken = default)
    {
        var item = request.Item;
        var contribution = request.Contribution;
        var existingMetadata = request.ExistingMetadata;
        var ancestorMetadata = request.AncestorMetadata;
        var externalId = ResolveExternalId(existingMetadata);

        const string method = "expandRelatedSubjectsAsync";
        await _latency.DelayAsync(method, cancellationToken).ConfigureAwait(false);

        var response = await _scenarios
            .ReadAsync<List<SubjectRelationshipDefinition>>(method, externalId, cancellationToken)
            .ConfigureAwait(false);

        await _audit
            .WriteAsync(
                method,
                new { item, contribution, existingMetadata, ancestorMetadata },
                response,
                cancellationToken)
            .ConfigureAwait(false);

        return response;
    }
}
