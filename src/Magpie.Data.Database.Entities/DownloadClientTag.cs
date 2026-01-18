using System;

namespace Magpie.Data.Database.Entities;

public class DownloadClientTag
{
    public Guid DownloadClientId { get; set; }
    public required string TagName { get; set; }

    public required DownloadClient DownloadClient { get; set; }
    public required Tag Tag { get; set; }
}
