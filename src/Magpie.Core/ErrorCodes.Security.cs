namespace Magpie.Core;

public static partial class ErrorCodes
{
    private const string KeyStoreCategory = $"{SecurityArea}01";
    private const string ApiKeyErrorCategory = $"{SecurityArea}03";
    private const string TokenErrorCategory = $"{SecurityArea}04";

    /// <summary>The requested key was not found in the key store.</summary>
    public const string KeyNotFound = $"{KeyStoreCategory}0001";

    /// <summary>A key with the specified identifier already exists.</summary>
    public const string KeyAlreadyExists = $"{KeyStoreCategory}0002";

    /// <summary>An error occurred while generating a key.</summary>
    public const string KeyGenerationError = $"{KeyStoreCategory}0003";

    /// <summary>The key type does not match the expected type.</summary>
    public const string KeyTypeMismatch = $"{KeyStoreCategory}0004";

    /// <summary>Decryption of the data failed.</summary>
    public const string DecryptionFailed = $"{KeyStoreCategory}0005";


    /// <summary>The API key is invalid.</summary>
    public const string ApiKeyInvalid = $"{ApiKeyErrorCategory}0010";

    /// <summary>The API key was not found.</summary>
    public const string ApiKeyNotFound = $"{ApiKeyErrorCategory}0011";

    /// <summary>The API key is disabled.</summary>
    public const string ApiKeyDisabled = $"{ApiKeyErrorCategory}0012";

    /// <summary>The API key has expired.</summary>
    public const string ApiKeyExpired = $"{ApiKeyErrorCategory}0013";

    /// <summary>The user associated with the API key was not found.</summary>
    public const string ApiKeyUserNotFound = $"{ApiKeyErrorCategory}0014";

    /// <summary>The user associated with the API key is disabled.</summary>
    public const string ApiKeyUserDisabled = $"{ApiKeyErrorCategory}0015";

    /// <summary>An error occurred during API key authentication.</summary>
    public const string ApiKeyAuthError = $"{ApiKeyErrorCategory}0016";


    /// <summary>An error occurred while generating a token.</summary>
    public const string TokenGenerationError = $"{TokenErrorCategory}0001";

    /// <summary>Token validation failed.</summary>
    public const string TokenValidationError = $"{TokenErrorCategory}0002";

    /// <summary>An error occurred with the refresh token.</summary>
    public const string RefreshTokenError = $"{TokenErrorCategory}0003";

    /// <summary>The refresh token has expired.</summary>
    public const string RefreshTokenExpired = $"{TokenErrorCategory}0004";

    /// <summary>The refresh token was not found.</summary>
    public const string RefreshTokenNotFound = $"{TokenErrorCategory}0006";
}
