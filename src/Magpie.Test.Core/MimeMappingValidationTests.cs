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
public sealed class MimeMappingValidationTests
{
    private Mock<IMimeMappingRepository> _mappings = null!;
    private MemoryCacheStore _store = null!;
    private MemoryCacheFactory _cacheFactory = null!;
    private FileSystem _subject = null!;

    [SetUp]
    public void SetUp()
    {
        _mappings = new Mock<IMimeMappingRepository>();
        _mappings
            .Setup(repository => repository.CreateAsync(
                It.IsAny<MimeMapping>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((MimeMapping mapping, Guid? _, CancellationToken _) => mapping);
        _store = new MemoryCacheStore(TimeProvider.System);
        _cacheFactory = new MemoryCacheFactory(_store);
        _subject = new FileSystem(
            Mock.Of<IBlockedFileTypeRepository>(),
            _mappings.Object,
            _cacheFactory,
            NullLogger<FileSystem>.Instance);
    }

    [TearDown]
    public void TearDown()
    {
        _cacheFactory.Dispose();
        _store.Dispose();
    }

    [TestCase("not-a-mime")]
    [TestCase("video/")]
    [TestCase("/mp4")]
    [TestCase("a/b/c")]
    [TestCase("text/plain; charset=utf-8")]
    [TestCase("video mp4/x")]
    public void UpsertMimeMappingAsync_Throws_WhenMimeTypeIsMalformed(string mimeType)
    {
        // Act
        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                async () => await _subject.UpsertMimeMappingAsync(".qaux", mimeType, null).ConfigureAwait(false),
                Throws.ArgumentException.With.Message.Contains("is not a valid MIME type"));
            _mappings.Verify(
                repository => repository.CreateAsync(
                    It.IsAny<MimeMapping>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [TestCase("application/x-foo", "application/x-foo")]
    [TestCase(" video/mp4 ", "video/mp4")]
    [TestCase("application/vnd.amazon.mobi8-ebook", "application/vnd.amazon.mobi8-ebook")]
    public async Task UpsertMimeMappingAsync_StoresTheMapping_WhenMimeTypeIsWellFormed(string mimeType, string expected)
    {
        // Act
        var mapping = await _subject.UpsertMimeMappingAsync(".qaux", mimeType, null).ConfigureAwait(false);

        // Assert
        Assert.That(mapping.MimeType, Is.EqualTo(expected));
    }
}
