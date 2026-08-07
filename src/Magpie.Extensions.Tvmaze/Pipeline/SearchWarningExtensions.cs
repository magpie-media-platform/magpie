namespace Magpie.Extensions.Tvmaze.Pipeline;

internal static class SearchWarningExtensions
{
    internal static string UnsupportedOperatorWarning(this string filterDescription)
    {
        return Unsupported(
            $"tvmaze cannot evaluate the requested operator for {filterDescription}; that criterion was not" +
                " applied.",
            filterDescription);
    }

    internal static string EmptyCriterionWarning(this string filterDescription)
    {
        return $"tvmaze received {filterDescription} without a usable value; that criterion was not applied.";
    }

    internal static string RelationshipPositivePassThroughWarning(
        this int groupNumber,
        string detail)
    {
        return Unsupported(
            $"tvmaze cannot evaluate {detail} in positive relationship group {groupNumber}; that alternative was" +
                " retained as a safe superset for core evaluation.",
            "Relationship");
    }

    internal static string RelationshipExclusionNotAppliedWarning(
        this int exclusionNumber,
        string detail)
    {
        return Unsupported(
            $"tvmaze cannot safely evaluate {detail} in relationship exclusion {exclusionNumber}; that exclusion" +
                " was not applied by the provider.",
            "Relationship");
    }

    internal static string ImagePositivePassThroughWarning(this int groupNumber, string detail)
    {
        return Unsupported(
            $"tvmaze cannot evaluate {detail} in positive image group {groupNumber}; that alternative was" +
                " retained as a safe superset for core evaluation.",
            "Image");
    }

    internal static string ImageExclusionNotAppliedWarning(this int exclusionNumber, string detail)
    {
        return Unsupported(
            $"tvmaze cannot safely evaluate {detail} in image exclusion {exclusionNumber}; that exclusion" +
                " was not applied by the provider.",
            "Image");
    }

    private static string Unsupported(string diagnostic, string filter) =>
        diagnostic;
}
