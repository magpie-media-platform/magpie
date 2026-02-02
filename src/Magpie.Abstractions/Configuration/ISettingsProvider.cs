using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Magpie.Abstractions.Configuration;

/// <summary>
/// Provides access to settings.
/// </summary>
public interface ISettingsProvider
{
    /// <summary>
    /// Get a settings group by name.
    /// </summary>
    /// <param name="groupName"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    Task<ISettingsGroup> GetSettingsGroupAsync(string groupName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all settings groups.
    /// </summary>
    /// <param name="cancellationToken"></param>
    Task<IEnumerable<ISettingsGroup>> GetAllSettingsGroupsAsync(CancellationToken cancellationToken = default);
}
