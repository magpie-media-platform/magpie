using System;
using System.Collections.Generic;

using Magpie.Models.Downloads;

namespace Magpie.Data.Database.Entities;

public class DownloadClient
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public required string Provider { get; set; }
    public bool Enabled { get; set; }
    public DownloadClientStatus Status { get; set; }
    public DateTimeOffset? LastChecked { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public DateTimeOffset? ModifiedAt { get; set; }
    public Guid? ModifiedByUserId { get; set; }

    public ICollection<DownloadClientSetting> Settings { get; } = [];
    public ICollection<DownloadClientTag> Tags { get; } = [];
    public ICollection<Download> Downloads { get; } = [];
}
