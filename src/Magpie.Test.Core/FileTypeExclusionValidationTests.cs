using System;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Repositories;
using Magpie.Core;
using Magpie.Core.Caching;
using Magpie.Models.Data;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core;

[TestFixture]
public sealed class FileTypeExclusionValidationTests
{
    private Mock<IBlockedFileTypeRepository> _exclusions = null!;
    private MemoryCacheStore _store = null!;
    private MemoryCacheFactory _cacheFactory = null!;
    private FileSystem _subject = null!;

    [SetUp]
    public void SetUp()
    {
        _exclusions = new Mock<IBlockedFileTypeRepository>();
        _exclusions
            .Setup(repository => repository.CreateAsync(
                It.IsAny<BlockedFileType>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((BlockedFileType exclusion, Guid? _, CancellationToken _) => exclusion);
        _store = new MemoryCacheStore(TimeProvider.System);
        _cacheFactory = new MemoryCacheFactory(_store);
        _subject = new FileSystem(
            _exclusions.Object,
            Mock.Of<IMimeMappingRepository>(),
            _cacheFactory,
            NullLogger<FileSystem>.Instance);
    }

    [TearDown]
    public void TearDown()
    {
        _cacheFactory.Dispose();
        _store.Dispose();
    }

    [TestCase("not a valid pattern /", "not a valid pattern / is not a valid MIME type.")]
    [TestCase("video/", "video/ is not a valid MIME type.")]
    [TestCase("/mp4", "/mp4 is not a valid MIME type.")]
    [TestCase("a/b/c", "a/b/c is not a valid MIME type.")]
    [TestCase("text/plain; charset=utf-8", "text/plain; charset=utf-8 is not a valid MIME type.")]
    [TestCase("video mp4/x", "video mp4/x is not a valid MIME type.")]
    public void CreateExclusionAsync_Throws_WhenMimeTypeIsMalformed(string pattern, string message)
    {
        // Act
        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                async () => await _subject.CreateExclusionAsync(pattern, null).ConfigureAwait(false),
                Throws.ArgumentException.With.Message.EqualTo(message));
            _exclusions.Verify(
                repository => repository.CreateAsync(
                    It.IsAny<BlockedFileType>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [TestCase("not a pattern", "not a pattern is not a valid file extension.")]
    [TestCase(".mkv ", ".mkv  is not a valid file extension.")]
    [TestCase(".mk v", ".mk v is not a valid file extension.")]
    [TestCase(".", ". is not a valid file extension.")]
    [TestCase("*.", "*. is not a valid file extension.")]
    [TestCase("a\\b", "a\\b is not a valid file extension.")]
    [TestCase("a:b", "a:b is not a valid file extension.")]
    [TestCase("mk?v", "mk?v is not a valid file extension.")]
    [TestCase("*mkv", "*mkv is not a valid file extension.")]
    [TestCase("*.mk*", "*.mk* is not a valid file extension.")]
    [TestCase("tar.gz", "tar.gz is not a valid file extension.")]
    [TestCase(".tar.gz", ".tar.gz is not a valid file extension.")]
    [TestCase("..mkv", "..mkv is not a valid file extension.")]
    [TestCase("*..mkv", "*..mkv is not a valid file extension.")]
    [TestCase(
        "mk\u0001v",
        "mk\u0001v is not a valid file extension.",
        TestName = "CreateExclusionAsync_Throws_WhenExtensionHasControlCharacter(U+0001 inside)")]
    [TestCase(
        "mk\u007fv",
        "mk\u007fv is not a valid file extension.",
        TestName = "CreateExclusionAsync_Throws_WhenExtensionHasControlCharacter(U+007F inside)")]
    [TestCase(
        "mk\u0080v",
        "mk\u0080v is not a valid file extension.",
        TestName = "CreateExclusionAsync_Throws_WhenExtensionHasControlCharacter(U+0080 inside)")]
    [TestCase(
        "\u0001mkv",
        "\u0001mkv is not a valid file extension.",
        TestName = "CreateExclusionAsync_Throws_WhenExtensionHasControlCharacter(U+0001 leading)")]
    public void CreateExclusionAsync_Throws_WhenExtensionIsMalformed(string pattern, string message)
    {
        // Act
        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                async () => await _subject.CreateExclusionAsync(pattern, null).ConfigureAwait(false),
                Throws.ArgumentException.With.Message.EqualTo(message));
            _exclusions.Verify(
                repository => repository.CreateAsync(
                    It.IsAny<BlockedFileType>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [TestCase("video/*", "Wildcard MIME type video/* is not supported.")]
    [TestCase("*/*", "Wildcard MIME type */* is not supported.")]
    [TestCase("*/mp4", "Wildcard MIME type */mp4 is not supported.")]
    [TestCase("video/x-*", "Wildcard MIME type video/x-* is not supported.")]
    public void CreateExclusionAsync_Throws_WhenMimeTypeIsWildcard(string pattern, string message)
    {
        // Act
        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                async () => await _subject.CreateExclusionAsync(pattern, null).ConfigureAwait(false),
                Throws.ArgumentException.With.Message.EqualTo(message));
            _exclusions.Verify(
                repository => repository.CreateAsync(
                    It.IsAny<BlockedFileType>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [TestCase(".mkv")]
    [TestCase("mkv")]
    [TestCase("*.mkv")]
    [TestCase("video/x-matroska")]
    [TestCase("application/epub+zip")]
    public async Task CreateExclusionAsync_StoresThePattern_WhenPatternIsWellFormed(string pattern)
    {
        // Act
        var created = await _subject.CreateExclusionAsync(pattern, null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(created.Pattern, Is.EqualTo(pattern));
            _exclusions.Verify(
                repository => repository.CreateAsync(
                    It.Is<BlockedFileType>(exclusion => exclusion.Pattern == pattern),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }

    [TestCase(".mkv", ".mkv", ".mkv is already a blocked file pattern.")]
    [TestCase(".mkv", ".MKV", ".MKV is already a blocked file pattern.")]
    [TestCase(
        "video/x-matroska",
        "Video/X-Matroska",
        "Video/X-Matroska is already a blocked file pattern.")]
    public void CreateExclusionAsync_Throws_WhenPatternIsAlreadyBlocked(string blocked, string pattern, string message)
    {
        // Arrange
        _exclusions
            .Setup(repository => repository.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([new BlockedFileType { Pattern = blocked }]);

        // Act
        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                async () => await _subject.CreateExclusionAsync(pattern, null).ConfigureAwait(false),
                Throws.ArgumentException.With.Message.EqualTo(message));
            _exclusions.Verify(
                repository => repository.CreateAsync(
                    It.IsAny<BlockedFileType>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [TestCase(".avi")]
    [TestCase("video/mp4")]
    public async Task CreateExclusionAsync_StoresThePattern_WhenADifferentPatternIsBlocked(string pattern)
    {
        // Arrange
        _exclusions
            .Setup(repository => repository.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([new BlockedFileType { Pattern = ".mkv" }]);

        // Act
        var created = await _subject.CreateExclusionAsync(pattern, null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(created.Pattern, Is.EqualTo(pattern));
            _exclusions.Verify(
                repository => repository.CreateAsync(
                    It.Is<BlockedFileType>(exclusion => exclusion.Pattern == pattern),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }
}
