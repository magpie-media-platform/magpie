using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Extensions.IntegrationTestShims.Shared;
using Magpie.Models;
using Magpie.Models.Data;
using Magpie.Sdk.Content;

namespace Magpie.Extensions.IntegrationTestShims.HierarchyMetadata;

internal sealed class MockHierarchyMetadataProvider : IContentHierarchyMetadataRelationshipProvider
{
    private readonly AuditWriter _audit;
    private readonly LatencyInjector _latency;
    private readonly ScenarioReader _scenarios;

    public MockHierarchyMetadataProvider()
        : this(MockHierarchyMetadataProviderType.Primary)
    {
    }

    public MockHierarchyMetadataProvider(string slug)
    {
        if (string.IsNullOrWhiteSpace(slug))
        {
            throw new ArgumentException("Slug must be non-empty.", nameof(slug));
        }

        ProviderType = slug;
        _audit = new AuditWriter(slug);
        _latency = new LatencyInjector(slug);
        _scenarios = new ScenarioReader(slug);
    }

    public string ProviderType { get; }

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

        const string method = "tryGetMetadataAsync";
        await _latency.DelayAsync(method, cancellationToken).ConfigureAwait(false);

        var contribution = await _scenarios
            .ReadAsync<MetadataContribution>(method, cancellationToken: cancellationToken)
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

        var children = await DiscoverChildrenAsync(contentHierarchyItem, existingMetadata, cancellationToken)
            .ConfigureAwait(false);

        return new ContentHierarchyMetadataResult { Contribution = contribution, Children = children };
    }

    private async Task<IReadOnlyList<ChildHierarchyDefinition>> DiscoverChildrenAsync(
        ContentHierarchyItem parent,
        IEnumerable<MetadataContribution> existingMetadata,
        CancellationToken cancellationToken)
    {
        const string method = "expandChildrenAsync";
        await _latency.DelayAsync(method, cancellationToken).ConfigureAwait(false);

        var response = await _scenarios
            .ReadAsync<List<ChildHierarchyDefinition>>(method, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        await _audit
            .WriteAsync(method, new { parent, existingMetadata }, response, cancellationToken)
            .ConfigureAwait(false);

        return response ?? [];
    }

    public async Task<IReadOnlyList<SubjectRelationshipDefinition>> GetRelationshipsAsync(
        MetadataRelationshipRequest request,
        CancellationToken cancellationToken = default)
    {
        var item = request.Item;
        var contribution = request.Contribution;
        var existingMetadata = request.ExistingMetadata;
        var ancestorMetadata = request.AncestorMetadata;

        const string method = "expandRelatedSubjectsAsync";
        await _latency.DelayAsync(method, cancellationToken).ConfigureAwait(false);

        var response = await _scenarios
            .ReadAsync<List<SubjectRelationshipDefinition>>(method, cancellationToken: cancellationToken)
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
