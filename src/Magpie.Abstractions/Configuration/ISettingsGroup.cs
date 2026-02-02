using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Magpie.Abstractions.Configuration;

/// <summary>
/// A group of settings.
/// </summary>
public interface ISettingsGroup
{
    /// <summary>
    /// The name of the settings group.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// The settings in the group.
    /// </summary>
    IDictionary<string, string> Settings { get; }

    /// <summary>
    /// Save the settings group.
    /// </summary>
    /// <param name="currentUserId">The id of the user making the change.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><see langword="true"/> if there were changes to save; otherwise, <see langword="false"/>.</returns>
    Task<bool> SaveAsync(Guid? currentUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reload the settings group from the underlying store.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// <see langword="true"/> if there were changes from the underlying store; otherwise, <see langword="false"/>.
    /// </returns>
    Task<bool> ReloadAsync(CancellationToken cancellationToken = default);
}
