using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Repositories;
using Magpie.Core;
using Magpie.Core.Caching;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Sdk.Services;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core;

[TestFixture]
public sealed class FileSystemTests
{
    private MemoryCacheFactory _cacheFactory = null!;
    private MemoryCacheStore _cacheStore = null!;
    private Mock<IBlockedFileTypeRepository> _blockedFileTypes = null!;
    private Mock<IMimeMappingRepository> _mimeMappings = null!;
    private IFileSystem _subject = null!;
    private string _testRoot = null!;

    [SetUp]
    public void CreateTestRoot()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), "magpie-file-system-tests", Guid.NewGuid().ToString("N"));
    }

    [TearDown]
    public void DeleteTestRoot()
    {
        if (Directory.Exists(_testRoot))
        {
            Directory.Delete(_testRoot, recursive: true);
        }
    }

    [OneTimeSetUp]
    public void SetUp()
    {
        _cacheStore = new MemoryCacheStore(TimeProvider.System);
        _cacheFactory = new MemoryCacheFactory(_cacheStore);
        _blockedFileTypes = new Mock<IBlockedFileTypeRepository>();
        _mimeMappings = new Mock<IMimeMappingRepository>();
        _subject = new FileSystem(
            _blockedFileTypes.Object,
            _mimeMappings.Object,
            _cacheFactory,
            NullLogger<FileSystem>.Instance);
    }

    [OneTimeTearDown]
    public void TearDown()
    {
        _cacheFactory.Dispose();
        _cacheStore.Dispose();
    }

    [TestCase(null, FileKind.Unknown)]
    [TestCase("video/x-matroska", FileKind.Video)]
    [TestCase("AUDIO/FLAC", FileKind.Audio)]
    [TestCase("application/x-subrip", FileKind.Subtitle)]
    [TestCase("text/vtt", FileKind.Subtitle)]
    [TestCase("application/pdf", FileKind.Unknown)]
    public void ResolveFileKind_ReturnsMatchingKind_WhenMimeTypeProvided(string? mimeType, FileKind expected)
    {
        // Act
        var result = _subject.ResolveFileKind(mimeType);

        // Assert
        Assert.That(result, Is.EqualTo(expected));
    }

    [Test]
    public void IsPathWithin_RecognizesRootAndDescendants_WhenPathIsEqualWithinOrOutsideRoot()
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
    public void PathContainsLink_ReturnsTrue_WhenPathTraversesLink()
    {
        // Arrange
        var searchRoot = Path.Combine(_testRoot, "root");
        var outsidePath = Path.Combine(_testRoot, "outside");
        var linkPath = Path.Combine(searchRoot, "linked");
        var plainPath = Path.Combine(searchRoot, "plain");
        Directory.CreateDirectory(searchRoot);
        Directory.CreateDirectory(outsidePath);
        Directory.CreateDirectory(plainPath);
        File.WriteAllText(Path.Combine(outsidePath, "target.txt"), "target");
        File.WriteAllText(Path.Combine(plainPath, "target.txt"), "target");

        using var mklink = Process.Start(new ProcessStartInfo("cmd.exe",
            $"/c mklink /J \"{linkPath}\" \"{outsidePath}\"")
        { CreateNoWindow = true, UseShellExecute = false })!;
        mklink.WaitForExit();
        Assert.That(File.GetAttributes(linkPath).HasFlag(FileAttributes.ReparsePoint), Is.True);

        // Act
        var throughLink = _subject.PathContainsLink(searchRoot, Path.Combine(linkPath, "target.txt"));
        var throughPlainFolder = _subject.PathContainsLink(searchRoot, Path.Combine(plainPath, "target.txt"));
        var theRootItself = _subject.PathContainsLink(searchRoot, searchRoot);
        Directory.Delete(linkPath);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(throughLink, Is.True);
            Assert.That(throughPlainFolder, Is.False);
            Assert.That(theRootItself, Is.False);
        }
    }

    [Test]
    [SupportedOSPlatform("windows")]
    public void EnumerateFiles_ReturnsOnlyAccessibleFiles_WhenSubfolderIsInaccessible()
    {
        // Arrange
        var blocked = Path.Combine(_testRoot, "blocked");
        Directory.CreateDirectory(blocked);
        var visiblePath = Path.Combine(_testRoot, "visible.txt");
        File.WriteAllText(visiblePath, "visible");
        File.WriteAllText(Path.Combine(blocked, "hidden.txt"), "hidden");

        var blockedInfo = new DirectoryInfo(blocked);
        var security = blockedInfo.GetAccessControl();
        var denyRule = new FileSystemAccessRule(
            WindowsIdentity.GetCurrent().User!,
            FileSystemRights.ListDirectory | FileSystemRights.Read,
            AccessControlType.Deny);
        security.AddAccessRule(denyRule);
        blockedInfo.SetAccessControl(security);
        Assert.Throws<UnauthorizedAccessException>(() => Directory.GetFiles(blocked));

        // Act
        string[] result;
        try
        {
            result = _subject.EnumerateFiles(_testRoot).ToArray();
        }
        finally
        {
            security.RemoveAccessRule(denyRule);
            blockedInfo.SetAccessControl(security);
        }

        // Assert
        Assert.That(result, Is.EqualTo([visiblePath]));
    }

    [Test]
    public async Task BrowseAsync_ReturnsFoldersBeforeFiles_WhenDirectoryContainsFoldersAndFiles()
    {
        // Arrange
        Directory.CreateDirectory(Path.Combine(_testRoot, "b"));
        Directory.CreateDirectory(Path.Combine(_testRoot, "a"));
        await File.WriteAllTextAsync(Path.Combine(_testRoot, "d.txt"), "d").ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(_testRoot, "c.txt"), "c").ConfigureAwait(false);

        // Act
        var result = await _subject.BrowseAsync(_testRoot).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Select(entry => entry.Name), Is.EqualTo(["a", "b", "c.txt", "d.txt"]));
            Assert.That(result.Select(entry => entry.Type), Is.EqualTo(["folder", "folder", "file", "file"]));
            Assert.That(result.Take(2).Select(entry => entry.Size), Is.All.Null);
        }
    }

    [Test]
    public async Task CreateExclusionAsync_PersistsThePatternAndActor_WhenActorProvided()
    {
        // Arrange
        var userId = Guid.NewGuid();
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
    public async Task LookupMimeTypeAsync_ReusesTheCachedMapping_WhenMappingIsCached()
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
    public async Task UpsertMimeMappingAsync_RefreshesTheCachedMapping_WhenMappingIsCached()
    {
        // Arrange
        var userId = Guid.NewGuid();
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
    public async Task DeleteMimeMappingAsync_InvalidatesTheCachedMapping_WhenMappingIsCached()
    {
        // Arrange
        var userId = Guid.NewGuid();
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
    public void MatchesAnyFileType_RecognizesExtensionAndMimePatterns_WhenExtensionOrMimeMatches()
    {
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
    public void PhysicalOperations_CopiesMovesAndDeletesFiles_WhenFilesAreCopiedMovedAndDeleted()
    {
        // Arrange
        var source = Path.Combine(_testRoot, "source.txt");
        var copy = Path.Combine(_testRoot, "copy.txt");
        var moved = Path.Combine(_testRoot, "moved.txt");
        _subject.CreateDirectory(_testRoot);
        File.WriteAllText(source, "test");

        // Act
        _subject.Copy(source, copy);
        _subject.Move(copy, moved);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(_subject.DirectoryExists(_testRoot), Is.True);
            Assert.That(_subject.FileExists(source), Is.True);
            Assert.That(_subject.FileExists(copy), Is.False);
            Assert.That(_subject.FileExists(moved), Is.True);
            Assert.That(_subject.GetFileSize(moved), Is.EqualTo(4));
            Assert.That(_subject.EnumerateFiles(_testRoot), Has.Exactly(2).Items);
        }

        _subject.DeleteFile(moved);
        Assert.That(_subject.FileExists(moved), Is.False);
    }
}
