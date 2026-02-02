using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Configuration;
using Magpie.Abstractions.Repositories;

namespace Magpie.Core.Configuration;

internal sealed class ExtensionSettingsProvider(
    ISettingsRepository repository,
    Guid extensionId) : IExtensionSettingsProvider
{
    public Guid ExtensionId => extensionId;

    public async Task<ISettingsGroup> GetSettingsGroupAsync(string groupName, CancellationToken cancellationToken)
    {
        var group = await repository.GetByIdAsync(new(extensionId, groupName), cancellationToken).ConfigureAwait(false);

        return new SettingsGroupWrapper(repository, group ?? new()
        {
            ExtensionId = extensionId,
            Name = groupName
        });
    }

    public async Task<IEnumerable<ISettingsGroup>> GetAllSettingsGroupsAsync(CancellationToken cancellationToken)
    {
        return
            from g in await repository.ListAllAsync(cancellationToken).ConfigureAwait(false)
            where g.ExtensionId == extensionId
            select new SettingsGroupWrapper(repository, g);
    }
}
