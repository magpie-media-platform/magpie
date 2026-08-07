using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models;
using Magpie.Models.Data;

namespace Magpie.Sdk.Notifications;

/// <summary>
/// A provider that delivers events to an external surface.
/// </summary>
public interface INotificationProvider
{
    /// <summary>
    /// The provider type.
    /// </summary>
    string ProviderType { get; }

    /// <summary>
    /// The provider's settings schema.
    /// </summary>
    IReadOnlyList<SettingSchemaDefinition> SettingsSchema { get; }

    /// <summary>
    /// Sends a notification for an event.
    /// </summary>
    /// <param name="rule">The notification rule that matched</param>
    /// <param name="magpieEvent">The event to notify about</param>
    /// <param name="cancellationToken">The cancellation token</param>
    Task SendAsync(NotificationRule rule, MagpieEvent magpieEvent, CancellationToken cancellationToken = default);
}
