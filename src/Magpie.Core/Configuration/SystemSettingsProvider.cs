using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Configuration;
using Magpie.Abstractions.Repositories;

namespace Magpie.Core.Configuration;

internal sealed class SystemSettingsProvider(ISettingsRepository repository) : ISystemSettingsProvider
{
    public async Task<ISettingsGroup> GetSettingsGroupAsync(string groupName, CancellationToken cancellationToken)
    {
        var group = await repository.GetByIdAsync(new(null, groupName), cancellationToken).ConfigureAwait(false);

        return new SettingsGroupWrapper(repository, group ?? new()
        {
            Name = groupName
        });
    }

    /// <inheritdoc />
    public async Task<IEnumerable<ISettingsGroup>> GetAllSettingsGroupsAsync(CancellationToken cancellationToken)
    {
        return
            from g in await repository.ListAllAsync(cancellationToken).ConfigureAwait(false)
            where g.ExtensionId == null
            select new SettingsGroupWrapper(repository, g);
    }
}
