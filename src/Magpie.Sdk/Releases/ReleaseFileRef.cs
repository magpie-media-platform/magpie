namespace Magpie.Sdk.Releases;

/// <summary>
/// The key and file name of an imported file for a leaf node.
/// </summary>
/// <param name="Key">The key of the imported file</param>
/// <param name="FileName">The name of the imported file</param>
public sealed record ReleaseFileRef(string Key, string FileName);
