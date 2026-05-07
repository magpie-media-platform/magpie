using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Integrations;
using Magpie.Models;
using Magpie.Models.Data;

namespace Magpie.Extensions.SampleIndexer;

/// <summary>
/// Hardcoded <see cref="IIndexerIntegration"/> used as a reference implementation and
/// a smoke test for the host extension loader.
/// </summary>
public sealed class SampleIndexerIntegration : IIndexerIntegration
{
    /// <inheritdoc />
    public string IndexerType => "sample";

    /// <inheritdoc />
    public Task<IEnumerable<Release>> SearchAsync(
        string query,
        IEnumerable<string>? categories,
        CancellationToken cancellationToken)
    {
        IEnumerable<Release> releases =
        [
            new Release
            {
                Protocol = "torrent",
                Title = $"Sample release for '{query}'",
                PublishDate = DateTime.UtcNow,
                Size = 1024L * 1024L * 100L,
                ExternalId = "sample-001"
            },
            new Release
            {
                Protocol = "torrent",
                Title = $"Another sample for '{query}'",
                PublishDate = DateTime.UtcNow,
                Size = 1024L * 1024L * 250L,
                ExternalId = "sample-002"
            }
        ];

        return Task.FromResult(releases);
    }

    /// <inheritdoc />
    public Task<ConnectionTestResult> TestConnectionAsync(
        IReadOnlyDictionary<string, string> settings,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new ConnectionTestResult
        {
            Success = true,
            Message = "Sample indexer is always reachable."
        });
    }
}
