using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Repositories;
using Magpie.Core;
using Magpie.Models;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Storage;
using Magpie.Sdk.Services;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core;

[TestFixture]
public sealed class RootFolderServiceTests
{
    private static readonly string Root = OperatingSystem.IsWindows() ? @"C:\" : "/";

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
        _fileSystem.Setup(fileSystem => fileSystem.MatchesAnyFileType(
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<IReadOnlyList<string>?>()))
            .Returns(true);
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
        _fileSystem.Setup(fileSystem => fileSystem.DirectoryExists(Path.Combine(Root, "Tv"))).Returns(true);
        _rootFolderRepository
            .Setup(repository => repository.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new RootFolder
                {
                    Id = Guid.NewGuid(),
                    Name = "Television",
                    Path = Path.Combine(Root, "Tv"),
                    ContentType = "movies"
                }
            ]);
        var create = new RootFolderCreate
        {
            Name = "Television",
            Path = Path.Combine(Root, "Tv") + Path.DirectorySeparatorChar,
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
            Path = Path.Combine(Root, "media", "movies"),
            ContentType = "movies"
        };
        _fileSystem.Setup(fileSystem => fileSystem.DirectoryExists(Path.Combine(Root, "media", "movies")))
            .Returns(true);
        _rootFolderRepository
            .Setup(repository => repository.CreateAsync(
                It.IsAny<RootFolder>(),
                actingUserId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(created);
        var create = new RootFolderCreate
        {
            Name = "Movies",
            Path = Path.Combine(Root, "media", "shows", "..", "movies"),
            ContentType = "movies",
            IsDefault = true,
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
                    folder.Path == Path.Combine(Root, "media", "movies") &&
                    folder.ContentType == "movies"),
                actingUserId,
                It.IsAny<CancellationToken>()), Times.Once);
        }
    }

    [Test]
    public void CreateAsync_Throws_WhenDirectoryDoesNotExist()
    {
        // Arrange
        _fileSystem.Setup(fileSystem => fileSystem.DirectoryExists(Path.Combine(Root, "missing"))).Returns(false);
        var create = new RootFolderCreate
        {
            Name = "Movies",
            Path = Path.Combine(Root, "missing"),
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
    [Platform("Win", Reason = "Only Windows compares paths without case.")]
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
    public void CreateAsync_Throws_WhenContentTypeIsNotRegistered()
    {
        // Arrange
        _fileSystem.Setup(fileSystem => fileSystem.DirectoryExists(Path.Combine(Root, "media"))).Returns(true);
        var create = new RootFolderCreate
        {
            Name = "Unknown",
            Path = Path.Combine(Root, "media"),
            ContentType = "unknown"
        };

        // Act
        var exception = Assert.ThrowsAsync<ArgumentException>(() => _subject.CreateAsync(create, null));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception!.ParamName, Is.EqualTo("ContentType"));
            Assert.That(exception.Message, Does.StartWith("The content type unknown is not registered."));
            _rootFolderRepository.Verify(repository => repository.CreateAsync(
                It.IsAny<RootFolder>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }
    }

    [Test]
    public async Task CreateAsync_CreatesRootFolder_WhenTheContentTypeHasNoDefaultYet()
    {
        // Arrange
        _fileSystem.Setup(fileSystem => fileSystem.DirectoryExists(Path.Combine(Root, "media", "movies")))
            .Returns(true);
        _rootFolderRepository.Setup(repository => repository.CreateAsync(
                It.IsAny<RootFolder>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((RootFolder folder, Guid? _, CancellationToken _) => folder);
        var create = new RootFolderCreate
        {
            Name = "Movies",
            Path = Path.Combine(Root, "media", "movies"),
            ContentType = "movies",
            IsDefault = false,
            Defaults = new()
            {
                ["movie"] = new RootFolderDefault
                {
                    QualityTierListId = Guid.Parse("01010101-0101-0101-0101-010101010101")
                }
            }
        };

        // Act
        await _subject.CreateAsync(create, null).ConfigureAwait(false);

        // Assert
        _rootFolderRepository.Verify(repository => repository.CreateAsync(
            It.Is<RootFolder>(rootFolder => rootFolder.ContentType == "movies"),
            It.IsAny<Guid?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task CreateAsync_CreatesRootFolder_WhenContentTypeAlreadyHasADefault()
    {
        // Arrange
        _fileSystem.Setup(fileSystem => fileSystem.DirectoryExists(Path.Combine(Root, "media", "movies-2")))
            .Returns(true);
        _rootFolderRepository
            .Setup(repository => repository.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new RootFolder
                {
                    Id = Guid.NewGuid(),
                    Name = "Movies",
                    Path = Path.Combine(Root, "media", "movies"),
                    ContentType = "movies",
                    IsDefault = true
                }
            ]);
        _rootFolderRepository.Setup(repository => repository.CreateAsync(
                It.IsAny<RootFolder>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((RootFolder folder, Guid? _, CancellationToken _) => folder);
        var create = new RootFolderCreate
        {
            Name = "Movies 2",
            Path = Path.Combine(Root, "media", "movies-2"),
            ContentType = "movies",
            IsDefault = false,
            Defaults = new()
            {
                ["movie"] = new RootFolderDefault
                {
                    QualityTierListId = Guid.Parse("01010101-0101-0101-0101-010101010101")
                }
            }
        };

        // Act
        var result = await _subject.CreateAsync(create, null).ConfigureAwait(false);

        // Assert
        Assert.That(result.IsDefault, Is.False);
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
        var patch = new RootFolderPatch { Name = new() { Value = name } };

        // Act
        var exception = Assert.ThrowsAsync<ArgumentException>(
            () => _subject.PatchAsync(rootFolderId, patch, null));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception!.ParamName, Is.EqualTo("Name"));
            Assert.That(exception.Message, Does.StartWith("Name is required."));
            _rootFolderRepository.Verify(repository => repository.PatchAsync(
                It.IsAny<RootFolderPatch>(),
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
        var patch = new RootFolderPatch { Path = new() { Value = path } };

        // Act
        var exception = Assert.ThrowsAsync<ArgumentException>(
            () => _subject.PatchAsync(rootFolderId, patch, null));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception!.ParamName, Is.EqualTo("Path"));
            Assert.That(exception.Message, Does.StartWith(message));
            _rootFolderRepository.Verify(repository => repository.PatchAsync(
                It.IsAny<RootFolderPatch>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }
    }

    [Test]
    public async Task PatchAsync_UpdatesName_WhenStoredPathDoesNotExist()
    {
        // Arrange
        var rootFolderId = Guid.Parse("77777777-7777-7777-7777-777777777777");
        _fileSystem.Setup(fileSystem => fileSystem.DirectoryExists(Path.Combine(Root, "missing"))).Returns(false);
        _rootFolderRepository
            .Setup(repository => repository.GetByIdAsync(rootFolderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RootFolder
            {
                Id = rootFolderId,
                Name = "Movies",
                Path = Path.Combine(Root, "missing"),
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
            .Setup(repository => repository.PatchAsync(
                It.IsAny<RootFolderPatch>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var patch = new RootFolderPatch
        {
            Name = new() { Value = "Films" },
            Path = new() { Value = Path.Combine(Root, "missing") }
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
            _rootFolderRepository.Verify(repository => repository.PatchAsync(
                It.Is<RootFolderPatch>(applied =>
                    applied.Id == rootFolderId &&
                    applied.Name!.Value == "Films" &&
                    applied.Path!.Value == Path.Combine(Root, "missing")),
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
                Path = Path.Combine(Root, "nas", "media") + Path.DirectorySeparatorChar,
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
            .Setup(repository => repository.PatchAsync(
                It.IsAny<RootFolderPatch>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var patch = new RootFolderPatch
        {
            Name = new() { Value = "Films" },
            Path = new() { Value = Path.Combine(Root, "nas", "media") + Path.DirectorySeparatorChar }
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
            _rootFolderRepository.Verify(repository => repository.PatchAsync(
                It.Is<RootFolderPatch>(applied =>
                    applied.Id == rootFolderId &&
                    applied.Name!.Value == "Films" &&
                    applied.Path!.Value == Path.Combine(Root, "nas", "media")),
                null,
                It.IsAny<CancellationToken>()), Times.Once);
        }
    }

    [Test]
    [Platform("Win", Reason = "Only Windows has UNC paths.")]
    public async Task PatchAsync_UpdatesName_WhenResentStoredUncPathHasTrailingSeparatorAndDoesNotExist()
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
            .Setup(repository => repository.PatchAsync(
                It.IsAny<RootFolderPatch>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var patch = new RootFolderPatch
        {
            Name = new() { Value = "Films" },
            Path = new() { Value = @"\\nas\media\" }
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
            _rootFolderRepository.Verify(repository => repository.PatchAsync(
                It.Is<RootFolderPatch>(applied =>
                    applied.Id == rootFolderId &&
                    applied.Name!.Value == "Films" &&
                    applied.Path!.Value == @"\\nas\media"),
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
                Path = Path.Combine(Root, "media"),
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
            .Setup(repository => repository.PatchAsync(
                It.IsAny<RootFolderPatch>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var patch = new RootFolderPatch { Path = new() { Value = Path.Combine(Root, "media") } };

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
            _rootFolderRepository.Verify(repository => repository.PatchAsync(
                It.Is<RootFolderPatch>(applied =>
                    applied.Id == rootFolderId && applied.Path!.Value == Path.Combine(Root, "media")),
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
            Path = Path.Combine(Root, "old"),
            ContentType = "series",
            Defaults = new()
            {
                ["series"] = new RootFolderDefault
                {
                    QualityTierListId = Guid.Parse("01010101-0101-0101-0101-010101010101")
                }
            }
        };
        _fileSystem.Setup(fileSystem => fileSystem.DirectoryExists(Path.Combine(Root, "media", "movies")))
            .Returns(true);
        _rootFolderRepository
            .Setup(repository => repository.GetByIdAsync(rootFolderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        _rootFolderRepository
            .Setup(repository => repository.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([existing]);
        _rootFolderRepository
            .Setup(repository => repository.PatchAsync(
                It.IsAny<RootFolderPatch>(),
                actingUserId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var patch = new RootFolderPatch
        {
            Name = new() { Value = "Films" },
            Path = new() { Value = Path.Combine(Root, "media", "shows", "..", "movies") },
            ContentType = new() { Value = "movies" },
            Defaults = new()
            {
                Value = new()
                {
                    ["movie"] = new RootFolderDefault
                    {
                        QualityTierListId = Guid.Parse("01010101-0101-0101-0101-010101010101")
                    }
                }
            }
        };

        // Act
        var result = await _subject.PatchAsync(rootFolderId, patch, actingUserId).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            _rootFolderRepository.Verify(repository => repository.PatchAsync(
                It.Is<RootFolderPatch>(applied =>
                    applied.Id == rootFolderId &&
                    applied.Name!.Value == "Films" &&
                    applied.Path!.Value == Path.Combine(Root, "media", "movies") &&
                    applied.ContentType!.Value == "movies"),
                actingUserId,
                It.IsAny<CancellationToken>()), Times.Once);
        }
    }

    [Test]
    public void PatchAsync_Throws_WhenDirectoryDoesNotExist()
    {
        // Arrange
        var rootFolderId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        _fileSystem.Setup(fileSystem => fileSystem.DirectoryExists(Path.Combine(Root, "missing"))).Returns(false);
        _rootFolderRepository
            .Setup(repository => repository.GetByIdAsync(rootFolderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RootFolder
            {
                Id = rootFolderId,
                Name = "Movies",
                Path = Path.Combine(Root, "media"),
                ContentType = "movies",
                Defaults = new()
                {
                    ["movie"] = new RootFolderDefault
                    {
                        QualityTierListId = Guid.Parse("01010101-0101-0101-0101-010101010101")
                    }
                }
            });
        var patch = new RootFolderPatch { Path = new() { Value = Path.Combine(Root, "missing") } };

        // Act
        var exception = Assert.ThrowsAsync<ArgumentException>(
            () => _subject.PatchAsync(rootFolderId, patch, null));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception!.ParamName, Is.EqualTo("Path"));
            Assert.That(exception.Message, Does.StartWith("Directory does not exist."));
            _rootFolderRepository.Verify(repository => repository.PatchAsync(
                It.IsAny<RootFolderPatch>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }
    }

    [Test]
    [Platform("Win", Reason = "Only Windows compares paths without case.")]
    public void PatchAsync_Throws_WhenPathMatchesAnotherStoredRootIgnoringCase()
    {
        // Arrange
        var rootFolderId = Guid.NewGuid();
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
                    Id = Guid.NewGuid(),
                    Name = "Existing",
                    Path = @"c:\MEDIA\MOVIES",
                    ContentType = "movies"
                }
            ]);
        var patch = new RootFolderPatch { Path = new() { Value = @"C:\media\movies" } };

        // Act
        var exception = Assert.ThrowsAsync<ArgumentException>(
            () => _subject.PatchAsync(rootFolderId, patch, null));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception!.ParamName, Is.EqualTo("Path"));
            Assert.That(exception.Message, Does.StartWith("Path is already configured as a root folder."));
            _rootFolderRepository.Verify(repository => repository.PatchAsync(
                It.IsAny<RootFolderPatch>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }
    }

    [Test]
    public void PatchAsync_Throws_WhenContentTypeIsNotRegistered()
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
            ContentType = new() { Value = "unknown" },
            Defaults = new()
            {
                Value = new()
                {
                    ["unknown"] = new RootFolderDefault
                    {
                        QualityTierListId = Guid.Parse("01010101-0101-0101-0101-010101010101")
                    }
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
            Assert.That(exception.Message, Does.StartWith("The content type unknown is not registered."));
            _rootFolderRepository.Verify(repository => repository.PatchAsync(
                It.IsAny<RootFolderPatch>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }
    }

    [Test]
    public async Task PatchAsync_AppliesThePatch_WhenTheOnlyDefaultOfAContentTypeIsCleared()
    {
        // Arrange
        var rootFolderId = Guid.NewGuid();
        _rootFolderRepository
            .Setup(repository => repository.GetByIdAsync(rootFolderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RootFolder
            {
                Id = rootFolderId,
                Name = "Movies",
                Path = @"C:\media",
                ContentType = "movies",
                IsDefault = true,
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
                    Id = rootFolderId,
                    Name = "Movies",
                    Path = @"C:\media",
                    ContentType = "movies",
                    IsDefault = true
                }
            ]);
        var patch = new RootFolderPatch { IsDefault = new() { Value = false } };

        // Act
        await _subject.PatchAsync(rootFolderId, patch, null).ConfigureAwait(false);

        // Assert
        _rootFolderRepository.Verify(repository => repository.PatchAsync(
            It.Is<RootFolderPatch>(applied => applied.Id == rootFolderId && !applied.IsDefault!.Value),
            It.IsAny<Guid?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task PatchAsync_UpdatesIsDefault_WhenAnotherDefaultExistsForContentType()
    {
        // Arrange
        var rootFolderId = Guid.NewGuid();
        var otherRootFolderId = Guid.NewGuid();
        _rootFolderRepository
            .Setup(repository => repository.GetByIdAsync(rootFolderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RootFolder
            {
                Id = rootFolderId,
                Name = "Movies",
                Path = @"C:\media",
                ContentType = "movies",
                IsDefault = true,
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
                    Id = rootFolderId,
                    Name = "Movies",
                    Path = @"C:\media",
                    ContentType = "movies",
                    IsDefault = true
                },
                new RootFolder
                {
                    Id = otherRootFolderId,
                    Name = "Movies 2",
                    Path = @"C:\media2",
                    ContentType = "movies",
                    IsDefault = true
                }
            ]);
        _rootFolderRepository
            .Setup(repository => repository.PatchAsync(
                It.IsAny<RootFolderPatch>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var patch = new RootFolderPatch { IsDefault = new() { Value = false } };

        // Act
        var result = await _subject.PatchAsync(rootFolderId, patch, null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            _rootFolderRepository.Verify(repository => repository.PatchAsync(
                It.Is<RootFolderPatch>(applied => applied.Id == rootFolderId && !applied.IsDefault!.Value),
                null,
                It.IsAny<CancellationToken>()), Times.Once);
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
            Path = Path.Combine(Root, "library"),
            ContentType = "publication",
            IsDefault = true,
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
            Path = Path.Combine(Root, "books"),
            ContentType = "book",
            IsDefault = true
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
            Path = Path.Combine(Root, "books"),
            ContentType = "book",
            IsDefault = true,
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
            Path = Path.Combine(Root, "books"),
            ContentType = "book",
            IsDefault = true,
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
        RootFolderPatch? updated = null;
        _rootFolderRepository.Setup(repository => repository.PatchAsync(
                It.IsAny<RootFolderPatch>(),
                null,
                It.IsAny<CancellationToken>()))
            .Callback((RootFolderPatch applied, Guid? _, CancellationToken _) => updated = applied)
            .ReturnsAsync(true);

        // Act
        var result = await _subject
            .PatchAsync(rootFolderId, new RootFolderPatch { ContentType = new() { Value = "audio" } }, null)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(updated!.ContentType!.Value, Is.EqualTo("audio"));
            Assert.That(updated.Defaults!.Value, Is.Empty);
        }
    }

    [Test]
    public async Task PatchAsync_SendsOnlyTheNamedField_WhenPatchOnlyChangesName()
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
                DefaultMonitoring = MonitoringMode.Monitored,
                RenameTemplate = "{Title}.{Ext}",
                Defaults = new()
                {
                    ["book"] = new() { QualityTierListId = tierListId }
                }
            });
        RootFolderPatch? updated = null;
        _rootFolderRepository.Setup(repository => repository.PatchAsync(
                It.IsAny<RootFolderPatch>(),
                null,
                It.IsAny<CancellationToken>()))
            .Callback((RootFolderPatch applied, Guid? _, CancellationToken _) => updated = applied)
            .ReturnsAsync(true);

        // Act
        var result = await _subject
            .PatchAsync(rootFolderId, new RootFolderPatch { Name = new() { Value = "New name" } }, null)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(updated!.Name!.Value, Is.EqualTo("New name"));
            Assert.That(updated.DefaultMonitoring, Is.Null);
            Assert.That(updated.RenameTemplate, Is.Null);
            Assert.That(updated.Defaults, Is.Null);
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
        RootFolderPatch? updated = null;
        _rootFolderRepository.Setup(repository => repository.PatchAsync(
                It.IsAny<RootFolderPatch>(),
                null,
                It.IsAny<CancellationToken>()))
            .Callback((RootFolderPatch applied, Guid? _, CancellationToken _) => updated = applied)
            .ReturnsAsync(true);
        var patch = new RootFolderPatch
        {
            ContentType = new() { Value = "audio" },
            Defaults = new()
            {
                Value = new()
                {
                    ["album"] = new RootFolderDefault { QualityTierListId = tierListId }
                }
            }
        };

        // Act
        var result = await _subject.PatchAsync(rootFolderId, patch, null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(updated, Is.Not.Null);
            Assert.That(updated!.ContentType!.Value, Is.EqualTo("audio"));
            Assert.That(updated.Defaults!.Value, Has.Count.EqualTo(1));
            Assert.That(updated.Defaults.Value.ContainsKey("album"), Is.True);
            Assert.That(updated.Defaults.Value["album"].QualityTierListId, Is.EqualTo(tierListId));
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
        var rootFolderId = Guid.NewGuid();
        _rootFolderRepository.Setup(repository => repository.GetByIdAsync(
                rootFolderId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RootFolder
            {
                Id = rootFolderId,
                Name = "Movies",
                Path = "/movies",
                ContentType = "movies",
                DefaultMonitoring = MonitoringMode.Monitored,
                RenameTemplate = "{Title}.{Ext}",
                Defaults = new()
                {
                    ["movie"] = new RootFolderDefault
                    {
                        QualityTierListId = Guid.Parse("01010101-0101-0101-0101-010101010101")
                    }
                }
            });
        RootFolderPatch? updated = null;
        _rootFolderRepository.Setup(repository => repository.PatchAsync(
                It.IsAny<RootFolderPatch>(),
                null,
                It.IsAny<CancellationToken>()))
            .Callback((RootFolderPatch applied, Guid? _, CancellationToken _) => updated = applied)
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
            Assert.That(updated!.DefaultMonitoring!.Value, Is.Null);
            Assert.That(updated.RenameTemplate!.Value, Is.Null);
        }
    }

    [Test]
    public async Task GetDiskSpaceAsync_ReturnsUnavailableFolder_WhenStorageCapacityIsUnavailable()
    {
        // Arrange
        var rootFolderId = Guid.NewGuid();
        _fileSystem.Setup(fileSystem => fileSystem.GetStorageCapacity(@"\\broken\share"))
            .Returns((StorageCapacity?)null);
        _rootFolderRepository
            .Setup(repository => repository.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new RootFolder
                {
                    Id = rootFolderId,
                    Name = "Broken",
                    Path = @"\\broken\share",
                    ContentType = "movies"
                }
            ]);
        _rootFolderRepository
            .Setup(repository => repository.GetContentSizesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, long> { [rootFolderId] = 7_000 });

        // Act
        var result = await _subject.GetDiskSpaceAsync().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Folders, Has.Length.EqualTo(1));
            Assert.That(result.Folders[0].TotalBytes, Is.EqualTo(0));
            Assert.That(result.Folders[0].FreeBytes, Is.EqualTo(0));
            Assert.That(result.Folders[0].ContentBytes, Is.EqualTo(7_000));
        }
    }

    [Test]
    public async Task GetDiskSpaceAsync_ReturnsUnavailableFolder_WhenTheDirectoryDoesNotExist()
    {
        // Arrange
        var rootFolderId = Guid.NewGuid();
        _fileSystem.Setup(fileSystem => fileSystem.DirectoryExists("/media/vanished")).Returns(false);
        _fileSystem.Setup(fileSystem => fileSystem.GetStorageCapacity("/media/vanished"))
            .Returns(new StorageCapacity { VolumeId = "parent", AvailableBytes = 400_000, TotalBytes = 1_000_000 });
        _rootFolderRepository
            .Setup(repository => repository.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new RootFolder
                {
                    Id = rootFolderId,
                    Name = "Vanished",
                    Path = "/media/vanished",
                    ContentType = "movies"
                }
            ]);
        _rootFolderRepository
            .Setup(repository => repository.GetContentSizesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, long> { [rootFolderId] = 7_000 });

        // Act
        var result = await _subject.GetDiskSpaceAsync().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Folders, Has.Length.EqualTo(1));
            Assert.That(result.Folders[0].TotalBytes, Is.EqualTo(0));
            Assert.That(result.Folders[0].FreeBytes, Is.EqualTo(0));
            Assert.That(result.Folders[0].UsedBytes, Is.EqualTo(0));
            Assert.That(result.Folders[0].ContentBytes, Is.EqualTo(7_000));
        }
    }

    [Test]
    public async Task GetDiskSpaceAsync_ReturnsTheCapacityAndContentBytesOfEachFolder_WhenOnlyOneFolderHoldsContent()
    {
        // Arrange
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        _fileSystem.Setup(fileSystem => fileSystem.GetStorageCapacity("/media/movies"))
            .Returns(new StorageCapacity { VolumeId = "first", AvailableBytes = 400_000, TotalBytes = 1_000_000 });
        _fileSystem.Setup(fileSystem => fileSystem.GetStorageCapacity("/media/shows"))
            .Returns(new StorageCapacity { VolumeId = "second", AvailableBytes = 50_000, TotalBytes = 200_000 });
        _rootFolderRepository
            .Setup(repository => repository.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new RootFolder { Id = firstId, Name = "Movies", Path = "/media/movies", ContentType = "movies" },
                new RootFolder { Id = secondId, Name = "Shows", Path = "/media/shows", ContentType = "series" }
            ]);
        _rootFolderRepository
            .Setup(repository => repository.GetContentSizesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, long> { [firstId] = 5_000_000_000 });

        // Act
        var result = await _subject.GetDiskSpaceAsync().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Folders, Has.Length.EqualTo(2));
            Assert.That(result.Folders[0].TotalBytes, Is.EqualTo(1_000_000));
            Assert.That(result.Folders[0].FreeBytes, Is.EqualTo(400_000));
            Assert.That(result.Folders[0].UsedBytes, Is.EqualTo(600_000));
            Assert.That(result.Folders[0].ContentBytes, Is.EqualTo(5_000_000_000));
            Assert.That(result.Folders[1].TotalBytes, Is.EqualTo(200_000));
            Assert.That(result.Folders[1].FreeBytes, Is.EqualTo(50_000));
            Assert.That(result.Folders[1].UsedBytes, Is.EqualTo(150_000));
            Assert.That(result.Folders[1].ContentBytes, Is.EqualTo(0));
        }
    }

    [Test]
    public async Task PatchAsync_ForwardsAutoImport_WhenItIsTheOnlyChange()
    {
        // Arrange
        var rootFolderId = Guid.NewGuid();
        _rootFolderRepository.Setup(repository => repository.GetByIdAsync(
                rootFolderId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RootFolder
            {
                Id = rootFolderId,
                Name = "Movies",
                Path = "/movies",
                ContentType = "movies"
            });
        RootFolderPatch? updated = null;
        _rootFolderRepository.Setup(repository => repository.PatchAsync(
                It.IsAny<RootFolderPatch>(),
                null,
                It.IsAny<CancellationToken>()))
            .Callback((RootFolderPatch applied, Guid? _, CancellationToken _) => updated = applied)
            .ReturnsAsync(true);

        // Act
        var result = await _subject.PatchAsync(
            rootFolderId,
            new RootFolderPatch { AutoImport = new() { Value = true } },
            null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(updated!.AutoImport!.Value, Is.True);
        }
    }
}
