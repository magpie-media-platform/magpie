namespace Magpie.Extensions.OpenLibrary;

internal sealed record ProviderSettings
{
    public const string ContactKey = "contact";

    public const string GroupName = "magpie.openlibrary";

    public string? Contact { get; init; }
}
