using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Repositories;
using Magpie.Core;
using Magpie.Models;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Sdk.Services;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core;

[TestFixture]
public sealed class RootFolderServiceTests
{
    private Mock<IContentTypeRegistry> _contentTypeRegistry = null!;
    private Mock<IFileSystem> _fileSystem = null!;
    private Mock<IQualityService> _qualityService = null!;
    private Mock<IRootFolderRepository> _rootFolderRepository = null!;
    private IRootFolderService _subject = null!;

    [SetUp]
    public void SetUp()
    {
        var movies = new ContentTypeDefinition
        {
            Slug = "movies",
            DisplayName = "Movies",
            LevelDefinitions =
            [
                new ContentHierarchyLevelDefinition
                {
                    Slug = "movie",
                    DisplayName = "Movie",
                    CustomMetadataFields = []
                }
            ]
        };
        var series = new ContentTypeDefinition
        {
            Slug = "series",
            DisplayName = "Series",
            LevelDefinitions =
            [
                new ContentHierarchyLevelDefinition
                {
                    Slug = "series",
                    DisplayName = "Series",
                    CustomMetadataFields = []
                }
            ]
        };
        var defaultQualityTierList = new QualityTierList
        {
            Id = Guid.Parse("01010101-0101-0101-0101-010101010101"),
            Name = "Universal"
        };
        _contentTypeRegistry = new Mock<IContentTypeRegistry>();
        _contentTypeRegistry.Setup(registry => registry.Exists("movies")).Returns(true);
        _contentTypeRegistry.Setup(registry => registry.GetDefinitionFor("movies")).Returns(movies);
        _contentTypeRegistry.Setup(registry => registry.GetDefinitionFor("series")).Returns(series);
        _contentTypeRegistry.SetupGet(registry => registry.Definitions).Returns([series, movies]);
        _fileSystem = new Mock<IFileSystem>();
        _fileSystem.Setup(fileSystem => fileSystem.DirectoryExists(It.IsAny<string>())).Returns(true);
        _fileSystem.Setup(fileSystem => fileSystem.GetFullPath(It.IsAny<string>()))
            .Returns((string path) => Path.GetFullPath(path));
        _fileSystem.Setup(fileSystem => fileSystem.PathsEqual(It.IsAny<string>(), It.IsAny<string>()))
            .Returns((string left, string right) => Path.GetRelativePath(left, right) == ".");
        _qualityService = new Mock<IQualityService>();
        _qualityService.Setup(service => service.ListTierListsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([defaultQualityTierList]);
        _rootFolderRepository = new Mock<IRootFolderRepository>();
        _rootFolderRepository
            .Setup(repository => repository.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _subject = new RootFolderService(
            _rootFolderRepository.Object,
            _fileSystem.Object,
            _contentTypeRegistry.Object,
            _qualityService.Object);
    }

    [TestCase("")]
    [TestCase("   ")]
    public void CreateAsync_Throws_WhenNameIsBlank(string name)
    {
        // Arrange
        var create = new RootFolderCreate
        {
            Name = name,
            Path = @"C:\media",
            ContentType = "movies"
        };

        // Act
        var exception = Assert.ThrowsAsync<ArgumentException>(() => _subject.CreateAsync(create, null));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception!.ParamName, Is.EqualTo("Name"));
            Assert.That(exception.Message, Does.StartWith("Name is required."));
            _rootFolderRepository.Verify(repository => repository.CreateAsync(
                It.IsAny<RootFolder>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }
    }

    [TestCase("", "Path is required.")]
    [TestCase("   ", "Path is required.")]
    [TestCase("media", "Path must be absolute.")]
    public void CreateAsync_Throws_WhenPathIsInvalid(string path, string message)
    {
        // Arrange
        var create = new RootFolderCreate
        {
            Name = "Movies",
            Path = path,
            ContentType = "movies"
        };

        // Act
        var exception = Assert.ThrowsAsync<ArgumentException>(() => _subject.CreateAsync(create, null));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception!.ParamName, Is.EqualTo("Path"));
            Assert.That(exception.Message, Does.StartWith(message));
            _rootFolderRepository.Verify(repository => repository.CreateAsync(
                It.IsAny<RootFolder>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }
    }

    [Test]
    public void CreateAsync_Throws_WhenPathMatchesStoredRootWithTrailingSeparator()
    {
        // Arrange
        _fileSystem.Setup(fileSystem => fileSystem.DirectoryExists(@"D:\Tv")).Returns(true);
        _rootFolderRepository
            .Setup(repository => repository.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new RootFolder
                {
                    Id = Guid.Parse("66666666-6666-6666-6666-666666666666"),
                    Name = "Television",
                    Path = @"D:\Tv",
                    ContentType = "movies"
                }
            ]);
        var create = new RootFolderCreate
        {
            Name = "Television",
            Path = @"D:\Tv\",
            ContentType = "movies"
        };

        // Act
        var exception = Assert.ThrowsAsync<ArgumentException>(() => _subject.CreateAsync(create, null));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception!.ParamName, Is.EqualTo("Path"));
            Assert.That(exception.Message, Does.StartWith("Path is already configured as a root folder."));
            _rootFolderRepository.Verify(repository => repository.CreateAsync(
                It.IsAny<RootFolder>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }
    }

    [Test]
    public async Task CreateAsync_CreatesRootFolderWithFullPath_WhenInputIsValid()
    {
        // Arrange
        var actingUserId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var created = new RootFolder
        {
            Id = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            Name = "Movies",
            Path = @"C:\media\movies",
            ContentType = "movies"
        };
        _fileSystem.Setup(fileSystem => fileSystem.DirectoryExists(@"C:\media\movies")).Returns(true);
        _rootFolderRepository
            .Setup(repository => repository.CreateAsync(
                It.IsAny<RootFolder>(),
                actingUserId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(created);
        var create = new RootFolderCreate
        {
            Name = "Movies",
            Path = @"C:\media\shows\..\movies",
            ContentType = "movies",
            Defaults = new()
            {
                ["movie"] = new RootFolderDefault
                {
                    QualityTierListId = Guid.Parse("01010101-0101-0101-0101-010101010101")
                }
            }
        };

        // Act
        var result = await _subject.CreateAsync(create, actingUserId).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.SameAs(created));
            _rootFolderRepository.Verify(repository => repository.CreateAsync(
                It.Is<RootFolder>(folder =>
                    folder.Name == "Movies" &&
                    folder.Path == @"C:\media\movies" &&
                    folder.ContentType == "movies"),
                actingUserId,
                It.IsAny<CancellationToken>()), Times.Once);
        }
    }

    [Test]
    public void CreateAsync_Throws_WhenDirectoryDoesNotExist()
    {
        // Arrange
        _fileSystem.Setup(fileSystem => fileSystem.DirectoryExists(@"C:\missing")).Returns(false);
        var create = new RootFolderCreate
        {
            Name = "Movies",
            Path = @"C:\missing",
            ContentType = "movies"
        };

        // Act
        var exception = Assert.ThrowsAsync<ArgumentException>(() => _subject.CreateAsync(create, null));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception!.ParamName, Is.EqualTo("Path"));
            Assert.That(exception.Message, Does.StartWith("Directory does not exist."));
            _rootFolderRepository.Verify(repository => repository.CreateAsync(
                It.IsAny<RootFolder>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }
    }

    [Test]
    public void CreateAsync_Throws_WhenPathMatchesStoredRootIgnoringCase()
    {
        // Arrange
        _fileSystem.Setup(fileSystem => fileSystem.DirectoryExists(@"C:\media\movies")).Returns(true);
        _rootFolderRepository
            .Setup(repository => repository.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new RootFolder
                {
                    Id = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
                    Name = "Existing",
                    Path = @"c:\MEDIA\MOVIES",
                    ContentType = "movies"
                }
            ]);
        var create = new RootFolderCreate
        {
            Name = "Movies",
            Path = @"C:\media\movies",
            ContentType = "movies"
        };

        // Act
        var exception = Assert.ThrowsAsync<ArgumentException>(() => _subject.CreateAsync(create, null));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception!.ParamName, Is.EqualTo("Path"));
            Assert.That(exception.Message, Does.StartWith("Path is already configured as a root folder."));
            _rootFolderRepository.Verify(repository => repository.CreateAsync(
                It.IsAny<RootFolder>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }
    }

    [Test]
    public void CreateAsync_ThrowsWithRegisteredTypes_WhenContentTypeIsNotRegistered()
    {
        // Arrange
        _fileSystem.Setup(fileSystem => fileSystem.DirectoryExists(@"C:\media")).Returns(true);
        var create = new RootFolderCreate
        {
            Name = "Unknown",
            Path = @"C:\media",
            ContentType = "unknown"
        };

        // Act
        var exception = Assert.ThrowsAsync<ArgumentException>(() => _subject.CreateAsync(create, null));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception!.ParamName, Is.EqualTo("ContentType"));
            Assert.That(exception.Message, Does.StartWith("Content type must be one of: movies, series."));
            _rootFolderRepository.Verify(repository => repository.CreateAsync(
                It.IsAny<RootFolder>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }
    }

    [TestCase("")]
    [TestCase("   ")]
    public void PatchAsync_Throws_WhenNameIsBlank(string name)
    {
        // Arrange
        var rootFolderId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
        _rootFolderRepository
            .Setup(repository => repository.GetByIdAsync(rootFolderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RootFolder
            {
                Id = rootFolderId,
                Name = "Movies",
                Path = @"C:\media",
                ContentType = "movies",
                Defaults = new()
                {
                    ["movie"] = new RootFolderDefault
                    {
                        QualityTierListId = Guid.Parse("01010101-0101-0101-0101-010101010101")
                    }
                }
            });
        var patch = new RootFolderPatch { Name = name };

        // Act
        var exception = Assert.ThrowsAsync<ArgumentException>(
            () => _subject.PatchAsync(rootFolderId, patch, null));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception!.ParamName, Is.EqualTo("Name"));
            Assert.That(exception.Message, Does.StartWith("Name is required."));
            _rootFolderRepository.Verify(repository => repository.UpdateAsync(
                It.IsAny<RootFolder>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }
    }

    [TestCase("", "Path is required.")]
    [TestCase("   ", "Path is required.")]
    [TestCase("media", "Path must be absolute.")]
    public void PatchAsync_Throws_WhenPathIsInvalid(string path, string message)
    {
        // Arrange
        var rootFolderId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
        _rootFolderRepository
            .Setup(repository => repository.GetByIdAsync(rootFolderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RootFolder
            {
                Id = rootFolderId,
                Name = "Movies",
                Path = @"C:\media",
                ContentType = "movies",
                Defaults = new()
                {
                    ["movie"] = new RootFolderDefault
                    {
                        QualityTierListId = Guid.Parse("01010101-0101-0101-0101-010101010101")
                    }
                }
            });
        var patch = new RootFolderPatch { Path = path };

        // Act
        var exception = Assert.ThrowsAsync<ArgumentException>(
            () => _subject.PatchAsync(rootFolderId, patch, null));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception!.ParamName, Is.EqualTo("Path"));
            Assert.That(exception.Message, Does.StartWith(message));
            _rootFolderRepository.Verify(repository => repository.UpdateAsync(
                It.IsAny<RootFolder>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }
    }

    [Test]
    public async Task PatchAsync_UpdatesName_WhenStoredPathDoesNotExist()
    {
        // Arrange
        var rootFolderId = Guid.Parse("77777777-7777-7777-7777-777777777777");
        _fileSystem.Setup(fileSystem => fileSystem.DirectoryExists(@"C:\missing")).Returns(false);
        _rootFolderRepository
            .Setup(repository => repository.GetByIdAsync(rootFolderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RootFolder
            {
                Id = rootFolderId,
                Name = "Movies",
                Path = @"C:\missing",
                ContentType = "movies",
                Defaults = new()
                {
                    ["movie"] = new RootFolderDefault
                    {
                        QualityTierListId = Guid.Parse("01010101-0101-0101-0101-010101010101")
                    }
                }
            });
        _rootFolderRepository
            .Setup(repository => repository.UpdateAsync(
                It.IsAny<RootFolder>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var patch = new RootFolderPatch
        {
            Name = "Films",
            Path = @"C:\missing"
        };

        // Act
        var result = await _subject.PatchAsync(rootFolderId, patch, null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            _fileSystem.Verify(
                fileSystem => fileSystem.DirectoryExists(It.IsAny<string>()),
                Times.Never);
            _rootFolderRepository.Verify(repository => repository.ListAllAsync(It.IsAny<CancellationToken>()),
                Times.Never);
            _rootFolderRepository.Verify(repository => repository.UpdateAsync(
                It.Is<RootFolder>(folder =>
                    folder.Id == rootFolderId &&
                    folder.Name == "Films" &&
                    folder.Path == @"C:\missing"),
                null,
                It.IsAny<CancellationToken>()), Times.Once);
        }
    }

    [Test]
    public async Task PatchAsync_UpdatesName_WhenResentStoredPathHasTrailingSeparatorAndDoesNotExist()
    {
        // Arrange
        var rootFolderId = Guid.Parse("88888888-8888-8888-8888-888888888888");
        _rootFolderRepository
            .Setup(repository => repository.GetByIdAsync(rootFolderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RootFolder
            {
                Id = rootFolderId,
                Name = "Movies",
                Path = @"\\nas\media\",
                ContentType = "movies",
                Defaults = new()
                {
                    ["movie"] = new RootFolderDefault
                    {
                        QualityTierListId = Guid.Parse("01010101-0101-0101-0101-010101010101")
                    }
                }
            });
        _rootFolderRepository
            .Setup(repository => repository.UpdateAsync(
                It.IsAny<RootFolder>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var patch = new RootFolderPatch
        {
            Name = "Films",
            Path = @"\\nas\media\"
        };

        // Act
        var result = await _subject.PatchAsync(rootFolderId, patch, null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            _fileSystem.Verify(
                fileSystem => fileSystem.DirectoryExists(It.IsAny<string>()),
                Times.Never);
            _rootFolderRepository.Verify(repository => repository.ListAllAsync(It.IsAny<CancellationToken>()),
                Times.Never);
            _rootFolderRepository.Verify(repository => repository.UpdateAsync(
                It.Is<RootFolder>(folder =>
                    folder.Id == rootFolderId &&
                    folder.Name == "Films" &&
                    folder.Path == @"\\nas\media"),
                null,
                It.IsAny<CancellationToken>()), Times.Once);
        }
    }

    [Test]
    public async Task PatchAsync_ReturnsTrue_WhenOnlyPathIsUnchanged()
    {
        // Arrange
        var rootFolderId = Guid.Parse("99999999-9999-9999-9999-999999999999");
        _rootFolderRepository
            .Setup(repository => repository.GetByIdAsync(rootFolderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RootFolder
            {
                Id = rootFolderId,
                Name = "Movies",
                Path = @"C:\media",
                ContentType = "movies",
                Defaults = new()
                {
                    ["movie"] = new RootFolderDefault
                    {
                        QualityTierListId = Guid.Parse("01010101-0101-0101-0101-010101010101")
                    }
                }
            });
        _rootFolderRepository
            .Setup(repository => repository.UpdateAsync(
                It.IsAny<RootFolder>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var patch = new RootFolderPatch { Path = @"C:\media" };

        // Act
        var result = await _subject.PatchAsync(rootFolderId, patch, null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            _fileSystem.Verify(
                fileSystem => fileSystem.DirectoryExists(It.IsAny<string>()),
                Times.Never);
            _rootFolderRepository.Verify(repository => repository.ListAllAsync(It.IsAny<CancellationToken>()),
                Times.Never);
            _rootFolderRepository.Verify(repository => repository.UpdateAsync(
                It.Is<RootFolder>(folder => folder.Id == rootFolderId && folder.Path == @"C:\media"),
                null,
                It.IsAny<CancellationToken>()), Times.Once);
        }
    }

    [Test]
    public async Task PatchAsync_UpdatesRootFolderWithFullPath_WhenInputIsValid()
    {
        // Arrange
        var rootFolderId = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");
        var actingUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var existing = new RootFolder
        {
            Id = rootFolderId,
            Name = "Movies",
            Path = @"C:\old",
            ContentType = "series",
            Defaults = new()
            {
                ["series"] = new RootFolderDefault
                {
                    QualityTierListId = Guid.Parse("01010101-0101-0101-0101-010101010101")
                }
            }
        };
        _fileSystem.Setup(fileSystem => fileSystem.DirectoryExists(@"C:\media\movies")).Returns(true);
        _rootFolderRepository
            .Setup(repository => repository.GetByIdAsync(rootFolderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        _rootFolderRepository
            .Setup(repository => repository.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([existing]);
        _rootFolderRepository
            .Setup(repository => repository.UpdateAsync(
                It.IsAny<RootFolder>(),
                actingUserId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var patch = new RootFolderPatch
        {
            Name = "Films",
            Path = @"C:\media\shows\..\movies",
            ContentType = "movies",
            Defaults = new()
            {
                ["movie"] = new RootFolderDefault
                {
                    QualityTierListId = Guid.Parse("01010101-0101-0101-0101-010101010101")
                }
            }
        };

        // Act
        var result = await _subject.PatchAsync(rootFolderId, patch, actingUserId).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            _rootFolderRepository.Verify(repository => repository.UpdateAsync(
                It.Is<RootFolder>(folder =>
                    folder.Id == rootFolderId &&
                    folder.Name == "Films" &&
                    folder.Path == @"C:\media\movies" &&
                    folder.ContentType == "movies"),
                actingUserId,
                It.IsAny<CancellationToken>()), Times.Once);
        }
    }

    [Test]
    public void PatchAsync_Throws_WhenDirectoryDoesNotExist()
    {
        // Arrange
        var rootFolderId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        _fileSystem.Setup(fileSystem => fileSystem.DirectoryExists(@"C:\missing")).Returns(false);
        _rootFolderRepository
            .Setup(repository => repository.GetByIdAsync(rootFolderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RootFolder
            {
                Id = rootFolderId,
                Name = "Movies",
                Path = @"C:\media",
                ContentType = "movies",
                Defaults = new()
                {
                    ["movie"] = new RootFolderDefault
                    {
                        QualityTierListId = Guid.Parse("01010101-0101-0101-0101-010101010101")
                    }
                }
            });
        var patch = new RootFolderPatch { Path = @"C:\missing" };

        // Act
        var exception = Assert.ThrowsAsync<ArgumentException>(
            () => _subject.PatchAsync(rootFolderId, patch, null));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception!.ParamName, Is.EqualTo("Path"));
            Assert.That(exception.Message, Does.StartWith("Directory does not exist."));
            _rootFolderRepository.Verify(repository => repository.UpdateAsync(
                It.IsAny<RootFolder>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }
    }

    [Test]
    public void PatchAsync_Throws_WhenPathMatchesAnotherStoredRootIgnoringCase()
    {
        // Arrange
        var rootFolderId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        _fileSystem.Setup(fileSystem => fileSystem.DirectoryExists(@"C:\media\movies")).Returns(true);
        _rootFolderRepository
            .Setup(repository => repository.GetByIdAsync(rootFolderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RootFolder
            {
                Id = rootFolderId,
                Name = "Movies",
                Path = @"C:\old",
                ContentType = "movies",
                Defaults = new()
                {
                    ["movie"] = new RootFolderDefault
                    {
                        QualityTierListId = Guid.Parse("01010101-0101-0101-0101-010101010101")
                    }
                }
            });
        _rootFolderRepository
            .Setup(repository => repository.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new RootFolder
                {
                    Id = Guid.Parse("44444444-4444-4444-4444-444444444444"),
                    Name = "Existing",
                    Path = @"c:\MEDIA\MOVIES",
                    ContentType = "movies"
                }
            ]);
        var patch = new RootFolderPatch { Path = @"C:\media\movies" };

        // Act
        var exception = Assert.ThrowsAsync<ArgumentException>(
            () => _subject.PatchAsync(rootFolderId, patch, null));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception!.ParamName, Is.EqualTo("Path"));
            Assert.That(exception.Message, Does.StartWith("Path is already configured as a root folder."));
            _rootFolderRepository.Verify(repository => repository.UpdateAsync(
                It.IsAny<RootFolder>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }
    }

    [Test]
    public void PatchAsync_ThrowsWithRegisteredTypes_WhenContentTypeIsNotRegistered()
    {
        // Arrange
        var rootFolderId = Guid.Parse("55555555-5555-5555-5555-555555555555");
        _rootFolderRepository
            .Setup(repository => repository.GetByIdAsync(rootFolderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RootFolder
            {
                Id = rootFolderId,
                Name = "Movies",
                Path = @"C:\media",
                ContentType = "movies",
                Defaults = new()
                {
                    ["movie"] = new RootFolderDefault
                    {
                        QualityTierListId = Guid.Parse("01010101-0101-0101-0101-010101010101")
                    }
                }
            });
        _contentTypeRegistry.Setup(registry => registry.GetDefinitionFor("unknown"))
            .Returns(new ContentTypeDefinition
            {
                Slug = "unknown",
                DisplayName = "Unknown",
                LevelDefinitions =
                [
                    new ContentHierarchyLevelDefinition
                    {
                        Slug = "unknown",
                        DisplayName = "Unknown",
                        CustomMetadataFields = []
                    }
                ]
            });
        var patch = new RootFolderPatch
        {
            ContentType = "unknown",
            Defaults = new()
            {
                ["unknown"] = new RootFolderDefault
                {
                    QualityTierListId = Guid.Parse("01010101-0101-0101-0101-010101010101")
                }
            }
        };

        // Act
        var exception = Assert.ThrowsAsync<ArgumentException>(
            () => _subject.PatchAsync(rootFolderId, patch, null));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception!.ParamName, Is.EqualTo("ContentType"));
            Assert.That(exception.Message, Does.StartWith("Content type must be one of: movies, series."));
            _rootFolderRepository.Verify(repository => repository.UpdateAsync(
                It.IsAny<RootFolder>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }
    }

    [Test]
    public async Task CreateAsync_ReturnsDistinctDefaults_WhenContentTypeHasMultipleRoots()
    {
        // Arrange
        var ebookTierListId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var audiobookTierListId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        _contentTypeRegistry.Setup(registry => registry.Exists("publication")).Returns(true);
        _contentTypeRegistry.Setup(registry => registry.GetDefinitionFor("publication"))
            .Returns(new ContentTypeDefinition
            {
                Slug = "publication",
                DisplayName = "Publication",
                LevelDefinitions =
                [
                    new ContentHierarchyLevelDefinition
                    {
                        Slug = "written",
                        DisplayName = "Written",
                        CustomMetadataFields = [],
                        AcceptedFileTypes = ["application/epub+zip"]
                    },
                    new ContentHierarchyLevelDefinition
                    {
                        Slug = "spoken",
                        DisplayName = "Spoken",
                        CustomMetadataFields = [],
                        AcceptedFileTypes = ["audio/*"]
                    }
                ]
            });
        _qualityService.Setup(service => service.ListTierListsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new QualityTierList
                {
                    Id = ebookTierListId,
                    Name = "Written",
                    MimeTypes = ["application/epub+zip"]
                },
                new QualityTierList
                {
                    Id = audiobookTierListId,
                    Name = "Spoken",
                    MimeTypes = ["audio/*"]
                }
            ]);
        RootFolder? created = null;
        _rootFolderRepository.Setup(repository => repository.CreateAsync(
                It.IsAny<RootFolder>(),
                null,
                It.IsAny<CancellationToken>()))
            .Callback((RootFolder folder, Guid? _, CancellationToken _) => created = folder)
            .ReturnsAsync((RootFolder folder, Guid? _, CancellationToken _) => folder);
        var create = new RootFolderCreate
        {
            Name = "Publications",
            Path = @"C:\library",
            ContentType = "publication",
            Defaults = new()
            {
                ["written"] = new RootFolderDefault { QualityTierListId = ebookTierListId },
                ["spoken"] = new RootFolderDefault { QualityTierListId = audiobookTierListId }
            }
        };

        // Act
        await _subject.CreateAsync(create, null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(created, Is.Not.Null);
            Assert.That(created!.Defaults, Has.Count.EqualTo(2));
            Assert.That(created.Defaults["written"].QualityTierListId, Is.EqualTo(ebookTierListId));
            Assert.That(created.Defaults["spoken"].QualityTierListId, Is.EqualTo(audiobookTierListId));
        }
    }

    [Test]
    public void CreateAsync_ThrowsArgumentException_WhenRootLevelDefaultIsMissing()
    {
        // Arrange
        _contentTypeRegistry.Setup(registry => registry.Exists("book")).Returns(true);
        _contentTypeRegistry.Setup(registry => registry.GetDefinitionFor("book"))
            .Returns(new ContentTypeDefinition
            {
                Slug = "book",
                DisplayName = "Book",
                LevelDefinitions =
                [
                    new ContentHierarchyLevelDefinition
                    {
                        Slug = "book",
                        DisplayName = "Book",
                        CustomMetadataFields = [],
                        AcceptedFileTypes = ["application/epub+zip"]
                    }
                ]
            });
        _qualityService.Setup(service => service.ListTierListsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var create = new RootFolderCreate
        {
            Name = "Books",
            Path = @"C:\books",
            ContentType = "book"
        };

        // Act
        var exception = Assert.ThrowsAsync<ArgumentException>(() => _subject.CreateAsync(create, null));

        // Assert
        Assert.That(exception!.Message, Does.Contain("Root level Book (book) requires"));
    }

    [Test]
    public void CreateAsync_ThrowsArgumentException_WhenTierListDoesNotExist()
    {
        // Arrange
        _contentTypeRegistry.Setup(registry => registry.Exists("book")).Returns(true);
        _contentTypeRegistry.Setup(registry => registry.GetDefinitionFor("book"))
            .Returns(new ContentTypeDefinition
            {
                Slug = "book",
                DisplayName = "Book",
                LevelDefinitions =
                [
                    new ContentHierarchyLevelDefinition
                    {
                        Slug = "book",
                        DisplayName = "Book",
                        CustomMetadataFields = [],
                        AcceptedFileTypes = ["application/epub+zip"]
                    }
                ]
            });
        _qualityService.Setup(service => service.ListTierListsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var create = new RootFolderCreate
        {
            Name = "Books",
            Path = @"C:\books",
            ContentType = "book",
            Defaults = new()
            {
                ["book"] = new RootFolderDefault
                {
                    QualityTierListId = Guid.Parse("55555555-5555-5555-5555-555555555555")
                }
            }
        };

        // Act
        var exception = Assert.ThrowsAsync<ArgumentException>(() => _subject.CreateAsync(create, null));

        // Assert
        Assert.That(exception!.Message, Does.Contain("does not exist"));
    }

    [Test]
    public async Task CreateAsync_ReturnsDefault_WhenTierListIsUniversal()
    {
        // Arrange
        var tierListId = Guid.Parse("77777777-7777-7777-7777-777777777777");
        _contentTypeRegistry.Setup(registry => registry.Exists("book")).Returns(true);
        _contentTypeRegistry.Setup(registry => registry.GetDefinitionFor("book"))
            .Returns(new ContentTypeDefinition
            {
                Slug = "book",
                DisplayName = "Book",
                LevelDefinitions =
                [
                    new ContentHierarchyLevelDefinition
                    {
                        Slug = "book",
                        DisplayName = "Book",
                        CustomMetadataFields = [],
                        AcceptedFileTypes = ["application/epub+zip"]
                    }
                ]
            });
        _qualityService.Setup(service => service.ListTierListsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new QualityTierList
                {
                    Id = tierListId,
                    Name = "Universal",
                    MimeTypes = []
                }
            ]);
        _rootFolderRepository.Setup(repository => repository.CreateAsync(
                It.IsAny<RootFolder>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((RootFolder folder, Guid? _, CancellationToken _) => folder);
        var create = new RootFolderCreate
        {
            Name = "Books",
            Path = @"C:\books",
            ContentType = "book",
            Defaults = new()
            {
                ["book"] = new RootFolderDefault { QualityTierListId = tierListId }
            }
        };

        // Act
        var result = await _subject.CreateAsync(create, null).ConfigureAwait(false);

        // Assert
        Assert.That(result.Defaults["book"].QualityTierListId, Is.EqualTo(tierListId));
    }

    [Test]
    public async Task PatchAsync_ClearsStaleDefaults_WhenContentTypeChangesWithoutReplacementDefaults()
    {
        // Arrange
        var rootFolderId = Guid.Parse("88888888-8888-8888-8888-888888888888");
        _contentTypeRegistry.Setup(registry => registry.Exists("audio")).Returns(true);
        _rootFolderRepository.Setup(repository => repository.GetByIdAsync(
                rootFolderId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RootFolder
            {
                Id = rootFolderId,
                Name = "Books",
                Path = "/library",
                ContentType = "book",
                Defaults = new()
                {
                    ["book"] = new RootFolderDefault
                    {
                        QualityTierListId = Guid.Parse("77777777-7777-7777-7777-777777777777")
                    }
                }
            });
        RootFolder? updated = null;
        _rootFolderRepository.Setup(repository => repository.UpdateAsync(
                It.IsAny<RootFolder>(),
                null,
                It.IsAny<CancellationToken>()))
            .Callback((RootFolder folder, Guid? _, CancellationToken _) => updated = folder)
            .ReturnsAsync(true);

        // Act
        var result = await _subject
            .PatchAsync(rootFolderId, new RootFolderPatch { ContentType = "audio" }, null)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(updated!.ContentType, Is.EqualTo("audio"));
            Assert.That(updated.Defaults, Is.Empty);
        }
    }

    [Test]
    public async Task PatchAsync_PreservesDefaults_WhenPatchOnlyChangesName()
    {
        // Arrange
        var rootFolderId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
        var tierListId = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");
        _rootFolderRepository.Setup(repository => repository.GetByIdAsync(
                rootFolderId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RootFolder
            {
                Id = rootFolderId,
                Name = "Old name",
                Path = "/library",
                ContentType = "book",
                DefaultMonitoring = true,
                RenameTemplate = "{Title}.{Ext}",
                Defaults = new()
                {
                    ["book"] = new() { QualityTierListId = tierListId }
                }
            });
        RootFolder? updated = null;
        _rootFolderRepository.Setup(repository => repository.UpdateAsync(
                It.IsAny<RootFolder>(),
                null,
                It.IsAny<CancellationToken>()))
            .Callback((RootFolder folder, Guid? _, CancellationToken _) => updated = folder)
            .ReturnsAsync(true);

        // Act
        var result = await _subject
            .PatchAsync(rootFolderId, new RootFolderPatch { Name = "New name" }, null)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(updated!.Name, Is.EqualTo("New name"));
            Assert.That(updated.DefaultMonitoring, Is.True);
            Assert.That(updated.RenameTemplate, Is.EqualTo("{Title}.{Ext}"));
            Assert.That(updated.Defaults, Has.Count.EqualTo(1));
            Assert.That(updated.Defaults.ContainsKey("book"), Is.True);
            Assert.That(updated.Defaults["book"].QualityTierListId, Is.EqualTo(tierListId));
        }
    }

    [Test]
    public async Task PatchAsync_ReturnsReconciledDefaults_WhenContentTypeChangesWithReplacementDefaults()
    {
        // Arrange
        var rootFolderId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var tierListId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
        _contentTypeRegistry.Setup(registry => registry.Exists("audio")).Returns(true);
        _rootFolderRepository.Setup(repository => repository.GetByIdAsync(
                rootFolderId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RootFolder
            {
                Id = rootFolderId,
                Name = "Library",
                Path = "/library",
                ContentType = "book",
                Defaults = new()
                {
                    ["book"] = new RootFolderDefault
                    {
                        QualityTierListId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd")
                    }
                }
            });
        _contentTypeRegistry.Setup(registry => registry.GetDefinitionFor("audio"))
            .Returns(new ContentTypeDefinition
            {
                Slug = "audio",
                DisplayName = "Audio",
                LevelDefinitions =
                [
                    new ContentHierarchyLevelDefinition
                    {
                        Slug = "album",
                        DisplayName = "Album",
                        AcceptedFileTypes = ["audio/*"],
                        CustomMetadataFields = []
                    }
                ]
            });
        _qualityService.Setup(service => service.ListTierListsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new QualityTierList
                {
                    Id = tierListId,
                    Name = "Audio",
                    MimeTypes = ["audio/*"]
                }
            ]);
        RootFolder? updated = null;
        _rootFolderRepository.Setup(repository => repository.UpdateAsync(
                It.IsAny<RootFolder>(),
                null,
                It.IsAny<CancellationToken>()))
            .Callback((RootFolder folder, Guid? _, CancellationToken _) => updated = folder)
            .ReturnsAsync(true);
        var patch = new RootFolderPatch
        {
            ContentType = "audio",
            Defaults = new()
            {
                ["album"] = new RootFolderDefault { QualityTierListId = tierListId }
            }
        };

        // Act
        var result = await _subject.PatchAsync(rootFolderId, patch, null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(updated, Is.Not.Null);
            Assert.That(updated!.ContentType, Is.EqualTo("audio"));
            Assert.That(updated.Defaults, Has.Count.EqualTo(1));
            Assert.That(updated.Defaults.ContainsKey("album"), Is.True);
            Assert.That(updated.Defaults["album"].QualityTierListId, Is.EqualTo(tierListId));
        }
    }

    [Test]
    public void Serialize_ReturnsOnlyQualityTierListId_WhenRootFolderHasMappings()
    {
        // Arrange
        var rootFolder = new RootFolder
        {
            Id = Guid.Parse("99999999-9999-9999-9999-999999999999"),
            Name = "Books",
            Path = "/books",
            ContentType = "book",
            Defaults = new()
            {
                ["book"] = new RootFolderDefault
                {
                    QualityTierListId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")
                }
            }
        };

        // Act
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(
            rootFolder,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var levelDefault = document.RootElement.GetProperty("defaults").GetProperty("book");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                levelDefault.GetProperty("qualityTierListId").GetGuid(),
                Is.EqualTo(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")));
            Assert.That(levelDefault.TryGetProperty("coveredLevelSlugs", out _), Is.False);
        }
    }

    [Test]
    public void Deserialize_ReturnsQualityTierListId_WhenJsonContainsObsoleteFields()
    {
        // Arrange
        const string json = """
            {
              "qualityTierListId": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
              "coveredLevelSlugs": ["book", "chapter"]
            }
            """;

        // Act
        var result = JsonSerializer.Deserialize<RootFolderDefault>(
            json,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        // Assert
        Assert.That(
            result,
            Has.Property(nameof(RootFolderDefault.QualityTierListId))
                .EqualTo(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")));
    }

    [Test]
    public async Task PatchAsync_AppliesNullableScalarPatches_WhenWrappersArePresent()
    {
        // Arrange
        var rootFolderId = Guid.Parse("abababab-abab-abab-abab-abababababab");
        _rootFolderRepository.Setup(repository => repository.GetByIdAsync(
                rootFolderId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RootFolder
            {
                Id = rootFolderId,
                Name = "Movies",
                Path = "/movies",
                ContentType = "movies",
                DefaultMonitoring = true,
                RenameTemplate = "{Title}.{Ext}",
                Defaults = new()
                {
                    ["movie"] = new RootFolderDefault
                    {
                        QualityTierListId = Guid.Parse("01010101-0101-0101-0101-010101010101")
                    }
                }
            });
        RootFolder? updated = null;
        _rootFolderRepository.Setup(repository => repository.UpdateAsync(
                It.IsAny<RootFolder>(),
                null,
                It.IsAny<CancellationToken>()))
            .Callback((RootFolder folder, Guid? _, CancellationToken _) => updated = folder)
            .ReturnsAsync(true);

        // Act
        var result = await _subject.PatchAsync(
            rootFolderId,
            new RootFolderPatch
            {
                DefaultMonitoring = new() { Value = null },
                RenameTemplate = new() { Value = null }
            },
            null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(updated!.DefaultMonitoring, Is.Null);
            Assert.That(updated.RenameTemplate, Is.Null);
        }
    }
}
