using System;
using System.Collections.Generic;

namespace Magpie.Data.Database.Entities;

public class SettingsGroup
{
    public required Guid Id { get; set; }
    public required string Name { get; set; }
    public Guid? ExtensionId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public DateTimeOffset? ModifiedAt { get; set; }
    public Guid? ModifiedByUserId { get; set; }

    public Extension? Extension { get; set; }
    public ICollection<Setting> Settings { get; } = [];
}
