using System;
using System.Threading;
using System.Threading.Tasks;

namespace Magpie.Extensions.OpenLibrary.QueryEngine;

internal interface IQueryCache
{
    Task<SourcePage> GetSourcePageAsync(
        string key,
        bool exactClass,
        Func<CancellationToken, Task<SourcePage>> factory,
        CancellationToken cancellationToken);

    Task<WorkResolution> GetResolutionAsync(
        string key,
        Func<CancellationToken, Task<WorkResolution>> factory,
        CancellationToken cancellationToken);

    Task<AuthorLookup> GetAuthorAsync(
        string key,
        Func<CancellationToken, Task<AuthorLookup>> factory,
        CancellationToken cancellationToken);

    Task<EditionLookup> GetEditionAsync(
        string key,
        Func<CancellationToken, Task<EditionLookup>> factory,
        CancellationToken cancellationToken);
}
