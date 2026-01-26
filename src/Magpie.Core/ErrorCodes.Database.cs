namespace Magpie.Core;

public static partial class ErrorCodes
{
    private const string DbErrorCategory = $"{DbArea}01";
    private const string DbValidationCategory = $"{DbArea}02";

    /// <summary>An error occurred while accessing settings.</summary>
    public const string DbSettingsError = $"{DbErrorCategory}0001";

    /// <summary>An error occurred while accessing the settings group.</summary>
    public const string DbSettingsGroupError = $"{DbErrorCategory}0002";

    /// <summary>An error occurred while accessing a setting.</summary>
    public const string DbSettingError = $"{DbErrorCategory}0003";

    /// <summary>An error occurred while accessing API keys.</summary>
    public const string DbApiKeyError = $"{DbErrorCategory}0004";

    /// <summary>An error occurred while accessing users.</summary>
    public const string DbUserError = $"{DbErrorCategory}0005";

    /// <summary>An error occurred while accessing auth providers.</summary>
    public const string DbAuthProviderError = $"{DbErrorCategory}0006";

    /// <summary>An error occurred while accessing roles.</summary>
    public const string DbRoleError = $"{DbErrorCategory}0007";

    /// <summary>An error occurred while accessing permissions.</summary>
    public const string DbPermissionError = $"{DbErrorCategory}0008";

    /// <summary>An error occurred while accessing permission groups.</summary>
    public const string DbPermissionGroupError = $"{DbErrorCategory}0009";

    /// <summary>An error occurred while accessing history records.</summary>
    public const string DbHistoryError = $"{DbErrorCategory}0010";

    /// <summary>An error occurred while accessing tags.</summary>
    public const string DbTagError = $"{DbErrorCategory}0011";

    /// <summary>An error occurred while accessing root folders.</summary>
    public const string DbRootFolderError = $"{DbErrorCategory}0012";

    /// <summary>An error occurred while accessing media servers.</summary>
    public const string DbMediaServerError = $"{DbErrorCategory}0013";

    /// <summary>An error occurred while accessing download clients.</summary>
    public const string DbDownloadClientError = $"{DbErrorCategory}0014";

    /// <summary>An error occurred while accessing indexer feeds.</summary>
    public const string DbIndexerFeedError = $"{DbErrorCategory}0015";

    /// <summary>An error occurred while accessing indexers.</summary>
    public const string DbIndexerError = $"{DbErrorCategory}0016";

    /// <summary>An error occurred while accessing quality tier lists.</summary>
    public const string DbQualityTierListError = $"{DbErrorCategory}0017";

    /// <summary>An error occurred while accessing content filters.</summary>
    public const string DbContentFilterError = $"{DbErrorCategory}0018";

    /// <summary>An error occurred while accessing metadata filters.</summary>
    public const string DbMetadataFilterError = $"{DbErrorCategory}0019";

    /// <summary>An error occurred while accessing wishlists.</summary>
    public const string DbWishlistError = $"{DbErrorCategory}0020";

    /// <summary>An error occurred while accessing content hierarchy levels.</summary>
    public const string DbContentHierarchyLevelError = $"{DbErrorCategory}0021";

    /// <summary>An error occurred while accessing indexer definitions.</summary>
    public const string DbIndexerDefinitionError = $"{DbErrorCategory}0022";

    /// <summary>An error occurred while accessing subjects.</summary>
    public const string DbSubjectError = $"{DbErrorCategory}0023";

    /// <summary>An error occurred while accessing releases.</summary>
    public const string DbReleaseError = $"{DbErrorCategory}0024";

    /// <summary>An error occurred while accessing downloads.</summary>
    public const string DbDownloadError = $"{DbErrorCategory}0025";

    /// <summary>An error occurred while accessing schedule triggers.</summary>
    public const string DbScheduleTriggerError = $"{DbErrorCategory}0026";

    /// <summary>An error occurred while accessing event triggers.</summary>
    public const string DbEventTriggerError = $"{DbErrorCategory}0027";

    /// <summary>An error occurred while accessing job executions.</summary>
    public const string DbJobExecutionError = $"{DbErrorCategory}0028";

    /// <summary>An error occurred while accessing extensions.</summary>
    public const string DbExtensionError = $"{DbErrorCategory}0029";

    /// <summary>An error occurred while accessing server configuration.</summary>
    public const string DbServerConfigError = $"{DbErrorCategory}0030";

    /// <summary>An error occurred while accessing metadata.</summary>
    public const string DbMetadataError = $"{DbErrorCategory}0031";

    /// <summary>An error occurred while accessing subject filters.</summary>
    public const string DbSubjectFilterError = $"{DbErrorCategory}0032";

    /// <summary>An error occurred while accessing refresh tokens.</summary>
    public const string DbRefreshTokenError = $"{DbErrorCategory}0033";


    /// <summary>A setting validation error occurred.</summary>
    public const string DbSettingValidationError = $"{DbValidationCategory}0034";

    /// <summary>A settings group validation error occurred.</summary>
    public const string DbSettingsGroupValidationError = $"{DbValidationCategory}0035";
}
