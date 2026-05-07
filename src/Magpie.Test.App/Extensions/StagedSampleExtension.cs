using System;
using System.IO;
using System.Reflection;

namespace Magpie.Test.App.Extensions;

/// <summary>
/// Locates the staged Sample Indexer assembly that the test project's
/// <c>StageSampleExtension</c> MSBuild target copies into
/// <c>bin/.../TestExtensions/sample/</c>.
/// </summary>
/// <remarks>
/// Tests deliberately avoid a compile-time reference to <c>SampleIndexerExtension</c>:
/// referencing it would pull the assembly into the test AppDomain at startup, and
/// <see cref="System.Runtime.Loader.AssemblyLoadContext.Default"/>'s
/// <c>LoadFromAssemblyPath</c> would short-circuit to the already-loaded copy instead
/// of exercising the host's on-disk load path.
/// </remarks>
internal static class StagedSampleExtension
{
    /// <summary>
    /// The well-known stable id of the Sample Indexer extension. Hard-coded here so
    /// tests do not need a compile-time reference to <c>SampleIndexerExtension</c>.
    /// Must stay in sync with <c>SampleIndexerExtension.ExtensionId</c>.
    /// </summary>
    public static readonly Guid SampleExtensionId = new("8B0F2A4C-1F3E-4A55-9E8E-2A6B9F1F0001");

    /// <summary>The expected file name of the sample assembly.</summary>
    public const string SampleAssemblyFileName = "Magpie.Extensions.SampleIndexer.dll";

    /// <summary>
    /// Returns the absolute path to the staged sample DLL under the test output
    /// directory.
    /// </summary>
    public static string GetSampleAssemblyPath()
    {
        var testBinDir = Path.GetDirectoryName(typeof(StagedSampleExtension).Assembly.Location)!;
        var path = Path.Combine(testBinDir, "TestExtensions", "sample", SampleAssemblyFileName);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"Staged sample extension was not found at '{path}'. " +
                "The StageSampleExtension MSBuild target in Magpie.Test.App.csproj is " +
                "responsible for copying it from the SampleIndexer build output.",
                path);
        }
        return path;
    }
}
