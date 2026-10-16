using System;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Repositories;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Paging;
using Magpie.Sdk.Services;

namespace Magpie.Core.Subjects;

internal sealed class SubjectService(
    ISubjectRepository subjects,
    IMetadataContainerRepository metadataContainers,
    ITransactionManager transactionManager) : ISubjectService
{
    public Task<PagedList<Subject>> QueryAsync(
        SubjectQuery query,
        PagingParameter paging,
        SortParameter? sort,
        CancellationToken cancellationToken = default) => subjects.QueryAsync(query, paging, sort, cancellationToken);

    public Task<Subject?> GetAsync(Guid subjectId, CancellationToken cancellationToken = default) =>
        subjects.GetByIdAsync(subjectId, cancellationToken);

    public async Task<bool> PatchAsync(
        Guid subjectId,
        SubjectPatch patch,
        Guid? actingUserId,
        CancellationToken cancellationToken = default)
    {
        if (patch.Tags is null && patch.PrimaryImageId is null)
        {
            return await subjects.GetByIdAsync(subjectId, cancellationToken).ConfigureAwait(false) is not null;
        }

        return await subjects
            .PatchAsync(
                new SubjectRowPatch { Id = subjectId, Tags = patch.Tags, PrimaryImageId = patch.PrimaryImageId },
                actingUserId,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<bool> DeleteAsync(
        Guid subjectId,
        Guid? actingUserId,
        CancellationToken cancellationToken = default)
    {
        return await transactionManager.ExecuteWithTransactionAsync(
            async ct =>
            {
                var subject = await subjects.GetByIdAsync(subjectId, ct).ConfigureAwait(false);
                if (subject is null)
                {
                    return false;
                }

                var deleted = await subjects.DeleteAsync(subjectId, actingUserId, ct).ConfigureAwait(false);
                if (!deleted)
                {
                    return false;
                }

                await metadataContainers
                    .RemoveContainersAsync([subject.MetadataContainerId], ct)
                    .ConfigureAwait(false);

                return true;
            },
            cancellationToken).ConfigureAwait(false);
    }
}
