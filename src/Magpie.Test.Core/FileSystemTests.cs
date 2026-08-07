using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Repositories;
using Magpie.Core;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Sdk.Services;

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core;

[TestFixture]
public class FileSystemTests
{
    private MemoryCache _cache = null!;
    private Mock<IBlockedFileTypeRepository> _blockedFileTypes = null!;
    private Mock<IMimeMappingRepository> _mimeMappings = null!;
    private IFileSystem _subject = null!;

    [OneTimeSetUp]
    public void SetUp()
    {
        _cache = new(new MemoryCacheOptions());
        _blockedFileTypes = new Mock<IBlockedFileTypeRepository>();
        _mimeMappings = new Mock<IMimeMappingRepository>();
        _subject = new FileSystem(
            _blockedFileTypes.Object,
            _mimeMappings.Object,
            _cache,
            NullLogger<FileSystem>.Instance);
    }

    [OneTimeTearDown]
    public void TearDown()
    {
        _cache.Dispose();
    }

    [TestCase(null, FileKind.Unknown)]
    [TestCase("video/x-matroska", FileKind.Video)]
    [TestCase("AUDIO/FLAC", FileKind.Audio)]
    [TestCase("application/x-subrip", FileKind.Subtitle)]
    [TestCase("text/vtt", FileKind.Subtitle)]
    [TestCase("application/pdf", FileKind.Unknown)]
    public void ResolveFileKind_Result_WhenMimeTypeProvided(string? mimeType, FileKind expected)
    {
        // Arrange

        // Act
        var result = _subject.ResolveFileKind(mimeType);

        // Assert
        Assert.That(result, Is.EqualTo(expected));
    }

    [Test]
    public void IsPathWithin_Result_WhenPathIsEqualWithinOrOutsideRoot()
    {
        // Arrange
        var root = Path.Combine(Path.GetTempPath(), "magpie-file-system-root");
        var child = Path.Combine(root, "child");
        var sibling = Path.Combine(Path.GetTempPath(), "magpie-file-system-root-other", "child");

        // Act
        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(_subject.IsPathWithin(root, root), Is.True);
            Assert.That(_subject.IsPathWithin(root, child), Is.True);
            Assert.That(_subject.IsPathWithin(root, sibling), Is.False);
            Assert.That(_subject.PathsEqual(root, root + Path.DirectorySeparatorChar), Is.True);
        }
    }

    [Test]
    public async Task BrowseAsync_Result_WhenDirectoryContainsFoldersAndFiles()
    {
        // Arrange
        var root = Path.Combine(Path.GetTempPath(), "magpie-file-system-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "b"));
        Directory.CreateDirectory(Path.Combine(root, "a"));
        await File.WriteAllTextAsync(Path.Combine(root, "d.txt"), "d").ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(root, "c.txt"), "c").ConfigureAwait(false);

        try
        {
            // Act
            var result = await _subject.BrowseAsync(root).ConfigureAwait(false);

            // Assert
            using (Assert.EnterMultipleScope())
            {
                Assert.That(result.Select(entry => entry.Name), Is.EqualTo(["a", "b", "c.txt", "d.txt"]));
                Assert.That(result.Select(entry => entry.Type), Is.EqualTo(["folder", "folder", "file", "file"]));
                Assert.That(result.Take(2).Select(entry => entry.Size), Is.All.Null);
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task CreateExclusionAsync_Result_WhenActorProvided()
    {
        // Arrange
        var userId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        _blockedFileTypes.Setup(repository => repository.CreateAsync(
                It.IsAny<BlockedFileType>(), userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((BlockedFileType model, Guid? _, CancellationToken _) => model);

        // Act
        var result = await _subject.CreateExclusionAsync("*.tmp", userId).ConfigureAwait(false);

        // Assert
        Assert.That(result.Pattern, Is.EqualTo("*.tmp"));
        _blockedFileTypes.Verify(repository => repository.CreateAsync(
            It.Is<BlockedFileType>(model => model.Pattern == "*.tmp"),
            userId,
            It.IsAny<CancellationToken>()));
    }

    [Test]
    public async Task LookupMimeTypeAsync_Result_WhenMappingIsCached()
    {
        // Arrange
        _mimeMappings.Setup(repository => repository.GetByIdAsync(".fs01", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MimeMapping { Extension = ".fs01", MimeType = "video/fs01" });

        // Act
        var first = await _subject.LookupMimeTypeAsync("/sample.FS01").ConfigureAwait(false);
        var second = await _subject.LookupMimeTypeAsync(".fs01").ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(first, Is.EqualTo("video/fs01"));
            Assert.That(second, Is.EqualTo("video/fs01"));
        }

        _mimeMappings.Verify(
            repository => repository.GetByIdAsync(".fs01", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task UpsertMimeMappingAsync_Result_WhenMappingIsCached()
    {
        // Arrange
        var userId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var mapping = new MimeMapping { Extension = ".upsertfs01", MimeType = "video/old" };
        _mimeMappings.Setup(repository => repository.GetByIdAsync(".upsertfs01", It.IsAny<CancellationToken>()))
            .ReturnsAsync(mapping);
        _mimeMappings.Setup(repository => repository.UpdateAsync(mapping, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        await _subject.LookupMimeTypeAsync(".upsertfs01").ConfigureAwait(false);

        // Act
        await _subject.UpsertMimeMappingAsync("upsertfs01", "video/new", userId).ConfigureAwait(false);
        var result = await _subject.LookupMimeTypeAsync(".upsertfs01").ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.EqualTo("video/new"));
        _mimeMappings.Verify(repository => repository.UpdateAsync(
            It.Is<MimeMapping>(value => value.MimeType == "video/new"),
            userId,
            It.IsAny<CancellationToken>()));
        _mimeMappings.Verify(
            repository => repository.GetByIdAsync(".upsertfs01", It.IsAny<CancellationToken>()),
            Times.Exactly(3));
    }

    [Test]
    public async Task DeleteMimeMappingAsync_Result_WhenMappingIsCached()
    {
        // Arrange
        var userId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var exists = true;
        _mimeMappings.Setup(repository => repository.GetByIdAsync(".deletefs01", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => exists
                ? new MimeMapping { Extension = ".deletefs01", MimeType = "video/delete" }
                : null);
        _mimeMappings.Setup(repository => repository.DeleteAsync(".deletefs01", userId, It.IsAny<CancellationToken>()))
            .Callback(() => exists = false)
            .ReturnsAsync(true);
        await _subject.LookupMimeTypeAsync(".deletefs01").ConfigureAwait(false);

        // Act
        await _subject.DeleteMimeMappingAsync("deletefs01", userId).ConfigureAwait(false);
        var result = await _subject.LookupMimeTypeAsync(".deletefs01").ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.Null);
        _mimeMappings.Verify(repository => repository.DeleteAsync(
            ".deletefs01",
            userId,
            It.IsAny<CancellationToken>()));
        _mimeMappings.Verify(
            repository => repository.GetByIdAsync(".deletefs01", It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Test]
    public void MatchesAnyFileType_Result_WhenExtensionOrMimeMatches()
    {
        // Arrange
        // Act
        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(_subject.MatchesAnyFileType("movie.mkv", null, [".mkv"]), Is.True);
            Assert.That(_subject.MatchesAnyFileType("movie.bin", "video/mp4", ["video/*"]), Is.True);
            Assert.That(_subject.MatchesAnyFileType("movie.bin", "audio/flac", ["video/*"]), Is.False);
        }
    }

    [Test]
    public void PhysicalOperations_Result_WhenFilesAreCopiedMovedAndDeleted()
    {
        // Arrange
        var root = Path.Combine(Path.GetTempPath(), "magpie-physical-file-system-tests", Guid.NewGuid().ToString("N"));
        var source = Path.Combine(root, "source.txt");
        var copy = Path.Combine(root, "copy.txt");
        var moved = Path.Combine(root, "moved.txt");
        _subject.CreateDirectory(root);
        File.WriteAllText(source, "test");

        try
        {
            // Act
            _subject.Copy(source, copy);
            _subject.Move(copy, moved);

            // Assert
            using (Assert.EnterMultipleScope())
            {
                Assert.That(_subject.DirectoryExists(root), Is.True);
                Assert.That(_subject.FileExists(source), Is.True);
                Assert.That(_subject.FileExists(copy), Is.False);
                Assert.That(_subject.FileExists(moved), Is.True);
                Assert.That(_subject.GetFileSize(moved), Is.EqualTo(4));
                Assert.That(_subject.EnumerateFiles(root), Has.Exactly(2).Items);
            }

            _subject.DeleteFile(moved);
            Assert.That(_subject.FileExists(moved), Is.False);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
