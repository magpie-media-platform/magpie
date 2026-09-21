using System.Collections.Generic;

using Magpie.Models.Enums;
using Magpie.Models.Import;

using NUnit.Framework;

namespace Magpie.Test.Core.Import;

[TestFixture]
public sealed class LibraryImportCandidateGroupTests
{
    [TestCase("/media/tv/Bluey", LibraryImportCandidateGroupKind.Directory, "/media/tv/Bluey")]
    [TestCase("match:movies:tmdb:tmdbId:603", LibraryImportCandidateGroupKind.Match, null)]
    [TestCase("query:movies:the matrix", LibraryImportCandidateGroupKind.Query, null)]
    public void SourcePath_ReportsTheDirectoryOfTheGroup_WhenTheGroupKeyNamesOne(
        string groupKey,
        LibraryImportCandidateGroupKind expectedKind,
        string? expectedSourcePath)
    {
        // Arrange
        var group = new LibraryImportCandidateGroup
        {
            GroupKey = groupKey,
            Title = "Bluey",
            ContentType = "tv",
            LeafCount = 1,
            TopLevelSlug = "show",
            LeafLevelSlug = "episode",
            LeafLevelDisplayName = "Episode",
            StatusCounts = new Dictionary<LibraryImportCandidateStatus, int>()
        };

        // Act
        var kind = group.Kind;
        var sourcePath = group.SourcePath;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(kind, Is.EqualTo(expectedKind));
            Assert.That(sourcePath, Is.EqualTo(expectedSourcePath));
        }
    }
}
