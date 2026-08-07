namespace Magpie.Extensions.OpenLibrary;

internal sealed record BookHierarchy
{
    public required string ContentTypeSlug { get; init; }

    public required string BookLevelSlug { get; init; }
}
