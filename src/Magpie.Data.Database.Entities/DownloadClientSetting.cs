using System;

namespace Magpie.Data.Database.Entities;

public class DownloadClientSetting
{
    public Guid Id { get; set; }
    public Guid DownloadClientId { get; set; }
    public required string Key { get; set; }
    public string? Value { get; set; }

    public required DownloadClient DownloadClient { get; set; }
}
