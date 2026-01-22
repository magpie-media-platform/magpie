using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Data;
using Magpie.Data.Database;
using Magpie.Models;
using Magpie.Models.Result;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Magpie.Core.Data;

/// <summary>
/// Read-only repository for querying tags.
/// </summary>
public sealed class TagRepository(
    MagpieDbContext context,
    ILogger<TagRepository> logger) : ITagRepository
{
    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<Tag>>> GetAllAsync(CancellationToken cancellationToken)
    {
        try
        {
            var tags = await context.Tags
                .AsNoTracking()
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var result = tags.Select(t => t.ToModel()).ToImmutableList();

            return Result.Success<IReadOnlyList<Tag>>(result);
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(ex, "Error getting tags ({ExErrorCode})", errorCode ?? "");
            return Result.Fail<IReadOnlyList<Tag>>("Error retrieving tags", ErrorCodes.DbTagError, errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<Tag>>> GetByPrefixAsync(
        string prefix,
        CancellationToken cancellationToken)
    {
        try
        {
            var tags = await context.Tags
                .AsNoTracking()
                .Where(t => t.Name.StartsWith(prefix))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var result = tags.Select(t => t.ToModel()).ToImmutableList();

            return Result.Success<IReadOnlyList<Tag>>(result);
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(
                ex,
                "Error getting tags by prefix {Prefix} ({ExErrorCode})",
                prefix,
                errorCode ?? "");
            return Result.Fail<IReadOnlyList<Tag>>("Error retrieving tags", ErrorCodes.DbTagError, errorCode);
        }
    }
}
