namespace Magpie.Core;

public static partial class ErrorCodes
{
    private const string UserCategory = $"{AuthArea}01";
    private const string CredentialCategory = $"{AuthArea}02";

    /// <summary>The requested user was not found.</summary>
    public const string UserNotFound = $"{UserCategory}0020";

    /// <summary>The user account is disabled.</summary>
    public const string UserDisabled = $"{UserCategory}0021";

    /// <summary>A user with the specified username already exists.</summary>
    public const string UsernameAlreadyExists = $"{UserCategory}0022";


    /// <summary>The provided credentials are invalid.</summary>
    public const string InvalidCredentials = $"{CredentialCategory}0010";

    /// <summary>The account is locked out due to too many failed attempts.</summary>
    public const string AccountLockedOut = $"{CredentialCategory}0011";

    /// <summary>The account is disabled and cannot be used.</summary>
    public const string AccountDisabled = $"{CredentialCategory}0012";
}
