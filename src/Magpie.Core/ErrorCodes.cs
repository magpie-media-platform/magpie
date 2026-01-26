namespace Magpie.Core;

/// <summary>
/// Defines error codes used throughout the application for consistent error identification.
/// </summary>
public static partial class ErrorCodes
{
    private const string Prefix = "MP";
    private const string DbArea = $"{Prefix}01";
    private const string AuthArea = $"{Prefix}02";
    private const string SecurityArea = $"{Prefix}03";
    private const string ExtensionsArea = $"{Prefix}04";
}
