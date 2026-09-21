using System;

namespace Magpie.Extensions.Cardigann;

internal static class Constants
{
    internal const string ProviderTypeName = "cardigann";
    internal const string GitUrlSettingKey = "gitUrl";
    internal const string BranchSettingKey = "branch";
    internal const string DefinitionsPathSettingKey = "definitionsPath";
    internal const string DefaultGitUrl = "https://github.com/Jackett/Jackett.git";
    internal const string DefaultBranch = "master";
    internal const string DefaultDefinitionsPath = "src/Jackett.Common/Definitions";
    internal const string DefaultFeedName = "Cardigann community definitions";
    internal const string DuplicateFeedRepairId = "cardigann.config.dedup-feed";

    internal static readonly Guid ExtensionId = new("8B0F2A4C-1F3E-4A55-9E8E-2A6B9F1F0002");
}
