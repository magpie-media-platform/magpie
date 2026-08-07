using System;

using Magpie.Models.Arguments;
using Magpie.Models.Data;

namespace Magpie.Abstractions.Repositories;

/// <summary>
/// The repository contract for notification rules.
/// </summary>
public interface INotificationRuleRepository :
    IQueryRepository<NotificationRule, NotificationRuleQuery>,
    ICrudRepository<Guid, NotificationRule>;
