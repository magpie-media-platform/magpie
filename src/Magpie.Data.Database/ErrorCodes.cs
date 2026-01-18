namespace Magpie.Data.Database;

internal static class ErrorCodes
{
    private const string Prefix = "MP";
    private const string DbLayerArea = "03";
    private const string DbErrorCategory = "01";
    private const string DbValidationCategory = "02";

    public const string DbSettingsError = $"{Prefix}{DbLayerArea}{DbErrorCategory}0001";
    public const string DbSettingsGroupError = $"{Prefix}{DbLayerArea}{DbErrorCategory}0002";
    public const string DbSettingError = $"{Prefix}{DbLayerArea}{DbErrorCategory}0003";
    public const string DbApiKeyError = $"{Prefix}{DbLayerArea}{DbErrorCategory}0004";
    public const string DbUserError = $"{Prefix}{DbLayerArea}{DbErrorCategory}0005";
    public const string DbAuthProviderError = $"{Prefix}{DbLayerArea}{DbErrorCategory}0006";
    public const string DbRoleError = $"{Prefix}{DbLayerArea}{DbErrorCategory}0007";
    public const string DbPermissionError = $"{Prefix}{DbLayerArea}{DbErrorCategory}0008";
    public const string DbPermissionGroupError = $"{Prefix}{DbLayerArea}{DbErrorCategory}0009";
    public const string DbHistoryError = $"{Prefix}{DbLayerArea}{DbErrorCategory}0010";

    public const string DbSettingValidationError = $"{Prefix}{DbLayerArea}{DbValidationCategory}0001";
    public const string DbSettingsGroupValidationError = $"{Prefix}{DbLayerArea}{DbValidationCategory}0002";
}
