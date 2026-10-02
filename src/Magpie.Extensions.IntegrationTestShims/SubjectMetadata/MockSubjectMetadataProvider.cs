using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Extensions.IntegrationTestShims.Shared;
using Magpie.Models.Data;
using Magpie.Sdk.Content;

namespace Magpie.Extensions.IntegrationTestShims.SubjectMetadata;

internal sealed class MockSubjectMetadataProvider : ISubjectMetadataProvider
{
    private const string MethodName = "tryGetMetadata";
    private const string ProviderTypeName = "subjectmetadata";

    private static readonly string[] SupportedSubjectKindValues = ["*"];

    private readonly AuditWriter _audit = new(ProviderTypeName);
    private readonly LatencyInjector _latency = new(ProviderTypeName);
    private readonly ScenarioReader _scenarios = new(ProviderTypeName);

    public IEnumerable<string> SupportedSubjectKinds => SupportedSubjectKindValues;

    public async Task<MetadataContribution?> TryGetMetadataAsync(
        Subject subject,
        IEnumerable<MetadataContribution> existingMetadata,
        string defaultLanguagePreference,
        CancellationToken cancellationToken = default)
    {
        await _latency.DelayAsync(MethodName, cancellationToken).ConfigureAwait(false);

        var response = await _scenarios
            .ReadAsync<MetadataContribution?>(MethodName, subject.Kind, cancellationToken)
            .ConfigureAwait(false);

        var request = new TryGetMetadataRequest
        {
            SubjectId = subject.Id,
            SubjectKind = subject.Kind,
            MetadataContainerId = subject.MetadataContainerId,
            DefaultLanguagePreference = defaultLanguagePreference,
            ExistingMetadata = [.. existingMetadata
                .Select(m => new ExistingMetadataDigest
                {
                    ProviderType = m.ProviderType,
                    Source = m.Source,
                })],
        };

        await _audit.WriteAsync(MethodName, request, response, cancellationToken).ConfigureAwait(false);

        return response;
    }

    internal sealed class TryGetMetadataRequest
    {
        public Guid SubjectId { get; set; }

        public string SubjectKind { get; set; } = string.Empty;

        public Guid MetadataContainerId { get; set; }

        public string DefaultLanguagePreference { get; set; } = string.Empty;

        public ExistingMetadataDigest[] ExistingMetadata { get; set; } = [];
    }

    internal sealed class ExistingMetadataDigest
    {
        public string ProviderType { get; set; } = string.Empty;

        public string? Source { get; set; }
    }
}
