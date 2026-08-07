using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Extensions.OpenLibrary.Mapping;
using Magpie.Extensions.OpenLibrary.QueryEngine;
using Magpie.Models.Data;
using Magpie.Sdk.Content;

using Microsoft.Extensions.Logging;

namespace Magpie.Extensions.OpenLibrary;

internal sealed class AuthorSubjectMetadataProvider(
    ISearchEngine searchEngine,
    TimeProvider timeProvider,
    ILogger<AuthorSubjectMetadataProvider> logger) : ISubjectMetadataProvider
{
    public IEnumerable<string> SupportedSubjectKinds => [Constants.PersonSubjectKind];

    public async Task<MetadataContribution?> TryGetMetadataAsync(
        Subject subject,
        IEnumerable<MetadataContribution> existingMetadata,
        string defaultLanguagePreference,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(subject.Kind, Constants.PersonSubjectKind, StringComparison.Ordinal))
        {
            return null;
        }

        var authorOlid = existingMetadata.FindExternalId(Constants.ProviderType);
        authorOlid = authorOlid.NormalizeOlid('A');
        if (authorOlid is null)
        {
            return null;
        }

        try
        {
            var author = await searchEngine.GetAuthorAsync(authorOlid, cancellationToken).ConfigureAwait(false);
            return author is null
                ? null
                : author.ToAuthorContribution(authorOlid, timeProvider.GetUtcNow().UtcDateTime);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException or InvalidOperationException)
        {
            logger.LogWarning(ex, "Open Library author metadata refresh failed");
            return null;
        }
    }
}
