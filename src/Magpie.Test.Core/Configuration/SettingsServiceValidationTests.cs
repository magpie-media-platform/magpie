using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Repositories;
using Magpie.Core;
using Magpie.Models;
using Magpie.Models.Api;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Patch;
using Magpie.Sdk.Configuration;
using Magpie.Sdk.Integrations;
using Magpie.Sdk.Services;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Configuration;

[TestFixture]
public sealed class SettingsServiceValidationTests
{
    private Mock<ITransactionManager> _transactionManager = null!;
    private Mock<IContentHierarchyItemRepository> _hierarchy = null!;
    private ISettingsService _subject = null!;

    [SetUp]
    public void SetUp()
    {
        _hierarchy = new Mock<IContentHierarchyItemRepository>();
        _transactionManager = new Mock<ITransactionManager>();
        _transactionManager.Setup(manager => manager.ExecuteWithTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<bool>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<bool>> action, CancellationToken token) => action(token));
        _subject = new SettingsService(
            new Mock<ISettingsRepository>().Object,
            _hierarchy.Object,
            _transactionManager.Object,
            [],
            [],
            NullLogger<SettingsService>.Instance,
            Mock.Of<IExtensionRepository>(),
            Mock.Of<IServiceScopeFactory>());
    }

    [Test]
    public async Task ValidateAsync_ReturnsValidResultWithNoIssues_WhenAllValid()
    {
        // Arrange
        var schema = new List<SettingSchemaDefinition> {
            new() { Key = "host", Type = DataType.String, Required = true },
            new() { Key = "port", Type = DataType.Number },
            new() { Key = "useSsl", Type = DataType.Boolean },
            new() { Key = "since", Type = DataType.Date },
            new() { Key = "protocol", Type = DataType.String, Options = ["http", "https"] }
        };
        var settings = new Dictionary<string, string>
        {
            ["host"] = "example.test",
            ["port"] = "8080",
            ["useSsl"] = "true",
            ["since"] = "2025-01-01T00:00:00Z",
            ["protocol"] = "https"
        };

        // Act
        var result = await _subject
            .ValidateAsync(schema, settings, new object(), default)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsValid, Is.True);
            Assert.That(result.Issues, Is.Empty);
        }
    }

    [Test]
    public async Task ValidateAsync_ReportsKeyIssue_WhenMissingRequired()
    {
        // Arrange
        var schema = new List<SettingSchemaDefinition> {
            new() { Key = "host", Type = DataType.String, Required = true, DisplayName = "Host" }
        };
        var settings = new Dictionary<string, string>();

        // Act
        var result = await _subject
            .ValidateAsync(schema, settings, new object(), default)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Issues, Has.Count.EqualTo(1));
            Assert.That(result.Issues[0].Key, Is.EqualTo("host"));
            Assert.That(result.Issues[0].Message, Is.EqualTo("Host is required."));
        }
    }

    [Test]
    public async Task ValidateAsync_ReportsKeyIssue_WhenRequiredPresentButWhitespace()
    {
        // Arrange
        var schema = new List<SettingSchemaDefinition> {
            new() { Key = "apiKey", Type = DataType.String, Required = true }
        };
        var settings = new Dictionary<string, string> { ["apiKey"] = "   " };

        // Act
        var result = await _subject
            .ValidateAsync(schema, settings, new object(), default)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Issues, Has.Count.EqualTo(1));
            Assert.That(result.Issues[0].Key, Is.EqualTo("apiKey"));
        }
    }

    [Test]
    public async Task ValidateAsync_FallsBackToKeyWhenDisplayNameMissing_WhenRequiredMissing()
    {
        // Arrange
        var schema = new List<SettingSchemaDefinition> {
            new() { Key = "apiKey", Type = DataType.String, Required = true }
        };
        var settings = new Dictionary<string, string>();

        // Act
        var result = await _subject
            .ValidateAsync(schema, settings, new object(), default)
            .ConfigureAwait(false);

        // Assert
        Assert.That(result.Issues[0].Message, Is.EqualTo("apiKey is required."));
    }

    [Test]
    public async Task ValidateAsync_ReportsTypeIssue_WhenNumberWithNonNumericValue()
    {
        // Arrange
        var schema = new List<SettingSchemaDefinition> {
            new() { Key = "port", Type = DataType.Number, DisplayName = "Port" }
        };
        var settings = new Dictionary<string, string> { ["port"] = "abc" };

        // Act
        var result = await _subject
            .ValidateAsync(schema, settings, new object(), default)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Issues, Has.Count.EqualTo(1));
            Assert.That(result.Issues[0].Key, Is.EqualTo("port"));
            Assert.That(result.Issues[0].Message, Is.EqualTo("Port must be a number."));
        }
    }

    [Test]
    public async Task ValidateAsync_ReportsTypeIssue_WhenBooleanWithBadValue()
    {
        // Arrange
        var schema = new List<SettingSchemaDefinition> {
            new() { Key = "useSsl", Type = DataType.Boolean, DisplayName = "Use SSL" }
        };
        var settings = new Dictionary<string, string> { ["useSsl"] = "yes" };

        // Act
        var result = await _subject
            .ValidateAsync(schema, settings, new object(), default)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Issues[0].Message, Is.EqualTo("Use SSL must be true or false."));
        }
    }

    [TestCase("true")]
    [TestCase("TRUE")]
    [TestCase("false")]
    [TestCase("False")]
    [TestCase("0")]
    [TestCase("1")]
    public async Task ValidateAsync_PassesValidation_WhenBooleanWithAcceptableValues(string value)
    {
        // Arrange
        var schema = new List<SettingSchemaDefinition> {
            new() { Key = "useSsl", Type = DataType.Boolean }
        };
        var settings = new Dictionary<string, string> { ["useSsl"] = value };

        // Act
        var result = await _subject
            .ValidateAsync(schema, settings, new object(), default)
            .ConfigureAwait(false);

        // Assert
        Assert.That(result.IsValid, Is.True);
    }

    [Test]
    public async Task ValidateAsync_ReportsTypeIssue_WhenDateWithBadValue()
    {
        // Arrange
        var schema = new List<SettingSchemaDefinition> {
            new() { Key = "since", Type = DataType.Date, DisplayName = "Since" }
        };
        var settings = new Dictionary<string, string> { ["since"] = "not-a-date" };

        // Act
        var result = await _subject
            .ValidateAsync(schema, settings, new object(), default)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Issues[0].Message, Is.EqualTo("Since must be a valid date."));
        }
    }

    [Test]
    public async Task ValidateAsync_ReportsIssueWithJoinedOptions_WhenOptionsMismatch()
    {
        // Arrange
        var schema = new List<SettingSchemaDefinition> {
            new()
            {
                Key = "protocol",
                Type = DataType.String,
                DisplayName = "Protocol",
                Options = ["http", "https"]
            }
        };
        var settings = new Dictionary<string, string> { ["protocol"] = "ftp" };

        // Act
        var result = await _subject
            .ValidateAsync(schema, settings, new object(), default)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Issues[0].Message, Is.EqualTo("Protocol must be one of: http, https."));
        }
    }

    [Test]
    public async Task ValidateAsync_IsCaseInsensitive_WhenOptionsMatch()
    {
        // Arrange
        var schema = new List<SettingSchemaDefinition> {
            new() { Key = "protocol", Type = DataType.String, Options = ["http", "https"] }
        };
        var settings = new Dictionary<string, string> { ["protocol"] = "HTTPS" };

        // Act
        var result = await _subject
            .ValidateAsync(schema, settings, new object(), default)
            .ConfigureAwait(false);

        // Assert
        Assert.That(result.IsValid, Is.True);
    }

    [Test]
    public async Task ValidateAsync_AcceptsUnknownKeys_WhenKeysAreNotInTheSchema()
    {
        // Arrange
        var schema = new List<SettingSchemaDefinition> {
            new() { Key = "host", Type = DataType.String }
        };
        var settings = new Dictionary<string, string>
        {
            ["host"] = "localhost",
            ["undeclared"] = "value"
        };

        // Act
        var result = await _subject
            .ValidateAsync(schema, settings, new object(), default)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsValid, Is.True);
            Assert.That(result.Issues, Is.Empty);
        }
    }

    [Test]
    public async Task ValidateAsync_SkipsTypeAndOptionsChecks_WhenOptionalEmptyValue()
    {
        // Arrange
        var schema = new List<SettingSchemaDefinition> {
            new() { Key = "port", Type = DataType.Number },
            new() { Key = "protocol", Type = DataType.String, Options = ["http", "https"] }
        };
        var settings = new Dictionary<string, string>
        {
            ["port"] = "",
            ["protocol"] = ""
        };

        // Act
        var result = await _subject
            .ValidateAsync(schema, settings, new object(), default)
            .ConfigureAwait(false);

        // Assert
        Assert.That(result.IsValid, Is.True);
    }

    [Test]
    public async Task ValidateAsync_IsCaseInsensitive_WhenKeyMatching()
    {
        // Arrange
        var schema = new List<SettingSchemaDefinition> {
            new() { Key = "apiKey", Type = DataType.String, Required = true }
        };
        var settings = new Dictionary<string, string> { ["ApiKey"] = "x" };

        // Act
        var result = await _subject
            .ValidateAsync(schema, settings, new object(), default)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsValid, Is.True);
            Assert.That(result.Issues, Is.Empty);
        }
    }

    [Test]
    public async Task ValidateAsync_TypeChecksRespectCaseInsensitivity_WhenKeyMatching()
    {
        // Arrange
        var schema = new List<SettingSchemaDefinition> {
            new() { Key = "port", Type = DataType.Number, DisplayName = "Port" }
        };
        var settings = new Dictionary<string, string> { ["PORT"] = "abc" };

        // Act
        var result = await _subject
            .ValidateAsync(schema, settings, new object(), default)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Issues, Has.Count.EqualTo(1));
            Assert.That(result.Issues[0].Key, Is.EqualTo("port"));
        }
    }

    [Test]
    public async Task ValidateAsync_ReturnsAllIssues_WhenMultipleFieldsAreInvalid()
    {
        // Arrange
        var schema = new List<SettingSchemaDefinition> {
            new() { Key = "host", Type = DataType.String, Required = true, DisplayName = "Host" },
            new() { Key = "port", Type = DataType.Number, DisplayName = "Port" }
        };
        var settings = new Dictionary<string, string> { ["port"] = "abc" };

        // Act
        var result = await _subject
            .ValidateAsync(schema, settings, new object(), default)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Issues, Has.Count.EqualTo(2));
            Assert.That(
                result.Issues,
                Has.Some.With.Property(nameof(IntegrationSettingsValidationIssue.Key)).EqualTo("host"));
            Assert.That(
                result.Issues,
                Has.Some.With.Property(nameof(IntegrationSettingsValidationIssue.Key)).EqualTo("port"));
        }
    }

    [Test]
    public async Task ValidateAsync_PassesAsTyped_WhenMappingsListEmptyArray()
    {
        // Arrange
        var schema = new List<SettingSchemaDefinition> {
            new() { Key = "mappings", Type = DataType.MappingsList, DisplayName = "Mappings" }
        };
        var settings = new Dictionary<string, string> { ["mappings"] = "[]" };

        // Act
        var result = await _subject
            .ValidateAsync(schema, settings, new object(), default)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsValid, Is.True);
            Assert.That(result.Issues, Is.Empty);
        }
    }

    [Test]
    public async Task ValidateAsync_ReportsTypeMismatch_WhenMappingsListNotJsonArray()
    {
        // Arrange
        var schema = new List<SettingSchemaDefinition> {
            new() { Key = "mappings", Type = DataType.MappingsList, DisplayName = "Mappings" }
        };
        var settings = new Dictionary<string, string> { ["mappings"] = "not-json" };

        // Act
        var result = await _subject
            .ValidateAsync(schema, settings, new object(), default)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Issues, Has.Count.EqualTo(1));
            Assert.That(result.Issues[0].Key, Is.EqualTo("mappings"));
            Assert.That(result.Issues[0].Message, Is.EqualTo("Mappings must be a JSON array of mapping rows."));
        }
    }

    [Test]
    public async Task ValidateAsync_ReportsRowScopedIssue_WhenMappingsListRowMissingRequiredColumn()
    {
        // Arrange
        var schema = new List<SettingSchemaDefinition> {
            new()
            {
                Key = "mappings",
                Type = DataType.MappingsList,
                DisplayName = "Mappings",
                RowSchema =
                [
                    new() { Key = "rootFolderPath", Type = DataType.String, DisplayName = "Root", Required = true },
                    new() { Key = "contentType", Type = DataType.String, DisplayName = "Type", Required = true },
                    new() { Key = "serverLibraryId", Type = DataType.String, DisplayName = "Library", Required = true }
                ]
            }
        };
        var settings = new Dictionary<string, string>
        {
            ["mappings"] =
                "[{\"rootFolderPath\":\"/type-a\",\"contentType\":\"type-a\",\"serverLibraryId\":\"\"}]"
        };

        // Act
        var result = await _subject
            .ValidateAsync(schema, settings, new object(), default)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Issues, Has.Count.EqualTo(1));
            Assert.That(result.Issues[0].Key, Is.EqualTo("mappings[0].serverLibraryId"));
            Assert.That(result.Issues[0].Message, Is.EqualTo("Mappings row 1: Library is required."));
        }
    }

    [Test]
    public async Task ValidateAsync_Passes_WhenMappingsListAllRequiredColumnsPresent()
    {
        // Arrange
        var schema = new List<SettingSchemaDefinition> {
            new()
            {
                Key = "mappings",
                Type = DataType.MappingsList,
                RowSchema =
                [
                    new() { Key = "rootFolderPath", Type = DataType.String, Required = true },
                    new() { Key = "contentType", Type = DataType.String, Required = true },
                    new() { Key = "serverLibraryId", Type = DataType.String, Required = true }
                ]
            }
        };
        var settings = new Dictionary<string, string>
        {
            ["mappings"] = "[{\"rootFolderPath\":\"/m\",\"contentType\":\"type-a\",\"serverLibraryId\":\"abc\"}]"
        };

        // Act
        var result = await _subject
            .ValidateAsync(schema, settings, new object(), default)
            .ConfigureAwait(false);

        // Assert
        Assert.That(result.IsValid, Is.True);
    }

    [Test]
    public async Task ValidateAsync_CombinesBaselineAndExtensionIssues_WhenIntegrationValidatorReturnsIssue()
    {
        // Arrange
        var schema = new List<SettingSchemaDefinition> {
            new() { Key = "host", Type = DataType.String, DisplayName = "Host", Required = true }
        };
        var settings = new Dictionary<string, string>();
        var extensionResult = new IntegrationSettingsValidationResult
        {
            IsValid = false,
            Issues = [new("apiKey", "apiKey is rejected.")]
        };
        var integration = new Mock<IValidatesIntegrationSettings>(MockBehavior.Strict);
        integration.Setup(i => i.ValidateSettingsAsync(settings, default))
            .ReturnsAsync(extensionResult);

        // Act
        var result = await _subject
            .ValidateAsync(schema, settings, integration.Object, default)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Issues, Has.Count.EqualTo(2));
            Assert.That(
                result.Issues,
                Has.Some.With.Property(nameof(IntegrationSettingsValidationIssue.Key)).EqualTo("host"));
            Assert.That(
                result.Issues,
                Has.Some.With.Property(nameof(IntegrationSettingsValidationIssue.Key)).EqualTo("apiKey"));
        }
    }

    [Test]
    public async Task ListGroupsAsync_UsesLoadedExtensionId_WhenLookingUpSettings()
    {
        // Arrange
        var definitionId = Guid.NewGuid();
        var loadedId = Guid.NewGuid();
        var groupName = "provider";
        var provider = new TestSettingsSchemaProvider
        {
            Groups =
            [
                new ExtensionSettingsGroupDefinition
                {
                    ExtensionId = definitionId,
                    Name = groupName,
                    SettingsSchema = [new SettingSchemaDefinition { Key = "apiKey", Type = DataType.String }]
                }
            ]
        };
        var settingsRepository = new Mock<ISettingsRepository>();
        settingsRepository.Setup(value => value.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        settingsRepository.Setup(value => value.GetByIdAsync(
            It.Is<SettingsGroupLookupKey>(key => key.ExtensionId == loadedId && key.Name == groupName),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                var storedGroup = new SettingsGroup
                {
                    Id = Guid.NewGuid(),
                    ExtensionId = loadedId,
                    Name = groupName
                };
                storedGroup.Settings.Add(new Setting { Key = "apiKey", Value = "secret" });
                return storedGroup;
            });
        var extensionRepository = new Mock<IExtensionRepository>();
        extensionRepository.Setup(value => value.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new Extension
                {
                    Id = loadedId,
                    Version = "1.0.0",
                    Name = "Provider",
                    FilePath = "provider.dll",
                    AssemblyName = "Magpie.Test.Core"
                }
            ]);
        var subject = new SettingsService(
            settingsRepository.Object,
            _hierarchy.Object,
            _transactionManager.Object,
            [provider],
            [],
            NullLogger<SettingsService>.Instance,
            extensionRepository.Object,
            Mock.Of<IServiceScopeFactory>());

        // Act
        var result = await subject.ListGroupsAsync().ConfigureAwait(false);
        var detail = result.Single(value => value.Group.Name == groupName);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(detail.Group.ExtensionId, Is.EqualTo(loadedId));
            Assert.That(detail.Group.Settings.Single().Value, Is.EqualTo("secret"));
        }

        settingsRepository.Verify(
            value => value.GetByIdAsync(
                new SettingsGroupLookupKey(definitionId, groupName),
                It.IsAny<CancellationToken>()),
            Times.Never());
    }

    [Test]
    public async Task GetExtensionGroupAsync_UsesLoadedExtensionId_WhenLookingUpSettings()
    {
        // Arrange
        var definitionId = Guid.NewGuid();
        var loadedId = Guid.NewGuid();
        var groupName = "provider";
        var provider = new TestSettingsSchemaProvider
        {
            Groups =
            [
                new ExtensionSettingsGroupDefinition
                {
                    ExtensionId = definitionId,
                    Name = groupName,
                    SettingsSchema = [new SettingSchemaDefinition { Key = "apiKey", Type = DataType.String }]
                }
            ]
        };
        var settingsRepository = new Mock<ISettingsRepository>();
        settingsRepository.Setup(value => value.GetByIdAsync(
            It.Is<SettingsGroupLookupKey>(key => key.ExtensionId == loadedId && key.Name == groupName),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                var storedGroup = new SettingsGroup
                {
                    Id = Guid.NewGuid(),
                    ExtensionId = loadedId,
                    Name = groupName
                };
                storedGroup.Settings.Add(new Setting { Key = "apiKey", Value = "secret" });
                return storedGroup;
            });
        var extensionRepository = new Mock<IExtensionRepository>();
        extensionRepository.Setup(value => value.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new Extension
                {
                    Id = loadedId,
                    Version = "1.0.0",
                    Name = "Provider",
                    FilePath = "provider.dll",
                    AssemblyName = "Magpie.Test.Core"
                }
            ]);
        using var services = new ServiceCollection()
            .AddSingleton(settingsRepository.Object)
            .AddSingleton(extensionRepository.Object)
            .BuildServiceProvider();
        var subject = new SettingsService(
            settingsRepository.Object,
            _hierarchy.Object,
            _transactionManager.Object,
            [provider],
            [],
            NullLogger<SettingsService>.Instance,
            extensionRepository.Object,
            services.GetRequiredService<IServiceScopeFactory>());

        // Act
        var result = await subject.GetExtensionGroupAsync(loadedId, groupName).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.Not.Null);
            Assert.That(result!.Group.ExtensionId, Is.EqualTo(loadedId));
            Assert.That(result.Group.Settings.Single().Value, Is.EqualTo("secret"));
        }

        settingsRepository.Verify(
            value => value.GetByIdAsync(
                new SettingsGroupLookupKey(definitionId, groupName),
                It.IsAny<CancellationToken>()),
            Times.Never());
    }

    [Test]
    public async Task PatchExtensionGroupAsync_UsesLoadedExtensionId_WhenWritingSettings()
    {
        // Arrange
        var definitionId = Guid.NewGuid();
        var loadedId = Guid.NewGuid();
        var groupName = "provider";
        var provider = new TestSettingsSchemaProvider
        {
            Groups =
            [
                new ExtensionSettingsGroupDefinition
                {
                    ExtensionId = definitionId,
                    Name = groupName,
                    SettingsSchema = [new SettingSchemaDefinition { Key = "apiKey", Type = DataType.String }]
                }
            ]
        };
        var settingsRepository = new Mock<ISettingsRepository>();
        settingsRepository
            .Setup(value => value.GetByIdAsync(
                It.Is<SettingsGroupLookupKey>(key => key.ExtensionId == loadedId && key.Name == groupName),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SettingsGroup { Id = Guid.NewGuid(), ExtensionId = loadedId, Name = groupName });
        settingsRepository.Setup(value => value.PatchAsync(
            It.IsAny<SettingsGroupPatch>(),
            It.IsAny<Guid>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var extensionRepository = new Mock<IExtensionRepository>();
        extensionRepository.Setup(value => value.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new Extension
                {
                    Id = loadedId,
                    Version = "1.0.0",
                    Name = "Provider",
                    FilePath = "provider.dll",
                    AssemblyName = "Magpie.Test.Core"
                }
            ]);
        var subject = new SettingsService(
            settingsRepository.Object,
            _hierarchy.Object,
            _transactionManager.Object,
            [provider],
            [],
            NullLogger<SettingsService>.Instance,
            extensionRepository.Object,
            Mock.Of<IServiceScopeFactory>());

        // Act
        var result = await subject.PatchExtensionGroupAsync(
            loadedId,
            groupName,
            new SettingsGroupPatch
            {
                Name = groupName,
                Settings = new DictionaryPatch<string, string, string>
                {
                    Add = new Dictionary<string, string> { ["apiKey"] = "secret" }
                }
            },
            Guid.NewGuid()).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.True);
        settingsRepository.Verify(
            value => value.PatchAsync(
                It.Is<SettingsGroupPatch>(patch => patch.ExtensionId == loadedId && patch.Name == groupName),
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()),
            Times.Once());
        settingsRepository.Verify(
            value => value.GetByIdAsync(
                new SettingsGroupLookupKey(definitionId, groupName),
                It.IsAny<CancellationToken>()),
            Times.Never());
    }

    [Test]
    public async Task TestGroupAsync_UsesDefinitionExtensionId_WhenSelectingConfigurationTester()
    {
        // Arrange
        var definitionId = Guid.NewGuid();
        var loadedId = Guid.NewGuid();
        var groupName = "provider";
        var provider = new TestSettingsSchemaProvider
        {
            Groups =
            [
                new ExtensionSettingsGroupDefinition
                {
                    ExtensionId = definitionId,
                    Name = groupName,
                    SettingsSchema = [new SettingSchemaDefinition { Key = "apiKey", Type = DataType.String }]
                }
            ]
        };
        var tester = new Mock<IExtensionConfigurationTester>();
        tester.SetupGet(value => value.ExtensionId).Returns(definitionId);
        tester.SetupGet(value => value.SettingsGroupName).Returns(groupName);
        tester.Setup(value => value.TestConfigurationAsync(
            It.IsAny<IReadOnlyDictionary<string, string>>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConfigurationTestResult { Success = true, Message = "ok" });
        var extensionRepository = new Mock<IExtensionRepository>();
        extensionRepository.Setup(value => value.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new Extension
                {
                    Id = loadedId,
                    Version = "1.0.0",
                    Name = "Provider",
                    FilePath = "provider.dll",
                    AssemblyName = "Magpie.Test.Core"
                }
            ]);
        var subject = new SettingsService(
            new Mock<ISettingsRepository>().Object,
            _hierarchy.Object,
            _transactionManager.Object,
            [provider],
            [tester.Object],
            NullLogger<SettingsService>.Instance,
            extensionRepository.Object,
            Mock.Of<IServiceScopeFactory>());

        // Act
        var result = await subject.TestGroupAsync(
            loadedId,
            groupName,
            new Dictionary<string, string> { ["apiKey"] = "secret" }).ConfigureAwait(false);

        // Assert
        Assert.That(result!.Success, Is.True);
        tester.Verify(
            value => value.TestConfigurationAsync(
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()),
            Times.Once());
    }

    [Test]
    public async Task ListGroupsAsync_SetsCanTest_WhenAConfigurationTesterIsRegistered()
    {
        // Arrange
        var definitionId = Guid.NewGuid();
        var loadedId = Guid.NewGuid();
        var groupName = "provider";
        var provider = new TestSettingsSchemaProvider
        {
            Groups =
            [
                new ExtensionSettingsGroupDefinition
                {
                    ExtensionId = definitionId,
                    Name = groupName,
                    SettingsSchema = [new SettingSchemaDefinition { Key = "apiKey", Type = DataType.String }]
                }
            ]
        };
        var settingsRepository = new Mock<ISettingsRepository>();
        settingsRepository.Setup(value => value.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        settingsRepository.Setup(value => value.GetByIdAsync(
            It.IsAny<SettingsGroupLookupKey>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync((SettingsGroup?)null);
        var extensionRepository = new Mock<IExtensionRepository>();
        extensionRepository.Setup(value => value.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new Extension
                {
                    Id = loadedId,
                    Version = "1.0.0",
                    Name = "Provider",
                    FilePath = "provider.dll",
                    AssemblyName = "Magpie.Test.Core"
                }
            ]);
        var tester = new Mock<IExtensionConfigurationTester>();
        tester.SetupGet(value => value.ExtensionId).Returns(definitionId);
        tester.SetupGet(value => value.SettingsGroupName).Returns(groupName);
        IExtensionConfigurationTester[] testers = [tester.Object];
        var subject = new SettingsService(
            settingsRepository.Object,
            _hierarchy.Object,
            _transactionManager.Object,
            [provider],
            testers,
            NullLogger<SettingsService>.Instance,
            extensionRepository.Object,
            Mock.Of<IServiceScopeFactory>());

        // Act
        var result = await subject.ListGroupsAsync().ConfigureAwait(false);
        var detail = result.Single(value => value.Group.Name == groupName);

        // Assert
        Assert.That(detail.CanTest, Is.True);
    }

    [Test]
    public async Task ListGroupsAsync_ClearsCanTest_WhenNoConfigurationTesterIsRegistered()
    {
        // Arrange
        var definitionId = Guid.NewGuid();
        var loadedId = Guid.NewGuid();
        var groupName = "provider";
        var provider = new TestSettingsSchemaProvider
        {
            Groups =
            [
                new ExtensionSettingsGroupDefinition
                {
                    ExtensionId = definitionId,
                    Name = groupName,
                    SettingsSchema = [new SettingSchemaDefinition { Key = "apiKey", Type = DataType.String }]
                }
            ]
        };
        var settingsRepository = new Mock<ISettingsRepository>();
        settingsRepository.Setup(value => value.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        settingsRepository.Setup(value => value.GetByIdAsync(
            It.IsAny<SettingsGroupLookupKey>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync((SettingsGroup?)null);
        var extensionRepository = new Mock<IExtensionRepository>();
        extensionRepository.Setup(value => value.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new Extension
                {
                    Id = loadedId,
                    Version = "1.0.0",
                    Name = "Provider",
                    FilePath = "provider.dll",
                    AssemblyName = "Magpie.Test.Core"
                }
            ]);
        var subject = new SettingsService(
            settingsRepository.Object,
            _hierarchy.Object,
            _transactionManager.Object,
            [provider],
            [],
            NullLogger<SettingsService>.Instance,
            extensionRepository.Object,
            Mock.Of<IServiceScopeFactory>());

        // Act
        var result = await subject.ListGroupsAsync().ConfigureAwait(false);
        var detail = result.Single(value => value.Group.Name == groupName);

        // Assert
        Assert.That(detail.CanTest, Is.False);
    }

    [Test]
    public async Task GetExtensionGroupAsync_SetsCanTest_WhenAConfigurationTesterIsRegistered()
    {
        // Arrange
        var definitionId = Guid.NewGuid();
        var loadedId = Guid.NewGuid();
        var groupName = "provider";
        var provider = new TestSettingsSchemaProvider
        {
            Groups =
            [
                new ExtensionSettingsGroupDefinition
                {
                    ExtensionId = definitionId,
                    Name = groupName,
                    SettingsSchema = [new SettingSchemaDefinition { Key = "apiKey", Type = DataType.String }]
                }
            ]
        };
        var settingsRepository = new Mock<ISettingsRepository>();
        settingsRepository.Setup(value => value.GetByIdAsync(
            It.IsAny<SettingsGroupLookupKey>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync((SettingsGroup?)null);
        var extensionRepository = new Mock<IExtensionRepository>();
        extensionRepository.Setup(value => value.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new Extension
                {
                    Id = loadedId,
                    Version = "1.0.0",
                    Name = "Provider",
                    FilePath = "provider.dll",
                    AssemblyName = "Magpie.Test.Core"
                }
            ]);
        var tester = new Mock<IExtensionConfigurationTester>();
        tester.SetupGet(value => value.ExtensionId).Returns(definitionId);
        tester.SetupGet(value => value.SettingsGroupName).Returns(groupName);
        IExtensionConfigurationTester[] testers = [tester.Object];
        using var services = new ServiceCollection()
            .AddSingleton(settingsRepository.Object)
            .AddSingleton(extensionRepository.Object)
            .BuildServiceProvider();
        var subject = new SettingsService(
            settingsRepository.Object,
            _hierarchy.Object,
            _transactionManager.Object,
            [provider],
            testers,
            NullLogger<SettingsService>.Instance,
            extensionRepository.Object,
            services.GetRequiredService<IServiceScopeFactory>());

        // Act
        var detail = await subject.GetExtensionGroupAsync(loadedId, groupName).ConfigureAwait(false);

        // Assert
        Assert.That(detail, Is.Not.Null);
        Assert.That(detail!.CanTest, Is.True);
    }

    [Test]
    public async Task GetExtensionGroupAsync_ClearsCanTest_WhenNoConfigurationTesterIsRegistered()
    {
        // Arrange
        var definitionId = Guid.NewGuid();
        var loadedId = Guid.NewGuid();
        var groupName = "provider";
        var provider = new TestSettingsSchemaProvider
        {
            Groups =
            [
                new ExtensionSettingsGroupDefinition
                {
                    ExtensionId = definitionId,
                    Name = groupName,
                    SettingsSchema = [new SettingSchemaDefinition { Key = "apiKey", Type = DataType.String }]
                }
            ]
        };
        var settingsRepository = new Mock<ISettingsRepository>();
        settingsRepository.Setup(value => value.GetByIdAsync(
            It.IsAny<SettingsGroupLookupKey>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync((SettingsGroup?)null);
        var extensionRepository = new Mock<IExtensionRepository>();
        extensionRepository.Setup(value => value.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new Extension
                {
                    Id = loadedId,
                    Version = "1.0.0",
                    Name = "Provider",
                    FilePath = "provider.dll",
                    AssemblyName = "Magpie.Test.Core"
                }
            ]);
        using var services = new ServiceCollection()
            .AddSingleton(settingsRepository.Object)
            .AddSingleton(extensionRepository.Object)
            .BuildServiceProvider();
        var subject = new SettingsService(
            settingsRepository.Object,
            _hierarchy.Object,
            _transactionManager.Object,
            [provider],
            [],
            NullLogger<SettingsService>.Instance,
            extensionRepository.Object,
            services.GetRequiredService<IServiceScopeFactory>());

        // Act
        var detail = await subject.GetExtensionGroupAsync(loadedId, groupName).ConfigureAwait(false);

        // Assert
        Assert.That(detail, Is.Not.Null);
        Assert.That(detail!.CanTest, Is.False);
    }

    [TestCase("monitoring", "system-default", 1)]
    [TestCase("monitoring", "other", 0)]
    [TestCase("other", "system-default", 0)]
    public async Task PatchSystemGroupAsync_RecomputesHierarchy_WhenMonitoringDefaultChanges(
        string group, string key, int expectedCalls)
    {
        // Arrange
        var repository = new Mock<ISettingsRepository>();
        repository.Setup(store => store.PatchAsync(
                It.IsAny<SettingsGroupPatch>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var subject = new SettingsService(
            repository.Object,
            _hierarchy.Object,
            _transactionManager.Object,
            [],
            [],
            NullLogger<SettingsService>.Instance,
            Mock.Of<IExtensionRepository>(),
            Mock.Of<IServiceScopeFactory>());
        var patch = new SettingsGroupPatch
        {
            Name = group,
            Settings = new() { Add = new Dictionary<string, string> { [key] = "false" } }
        };

        // Act
        await subject.PatchSystemGroupAsync(group, patch, Guid.NewGuid()).ConfigureAwait(false);

        // Assert
        _hierarchy.Verify(store => store.RecomputeAllEffectiveValuesAsync(It.IsAny<CancellationToken>()),
            Times.Exactly(expectedCalls));
    }

    [Test]
    public async Task PatchSystemGroupAsync_DoesNotRecomputeHierarchy_WhenOnlyMaxSpreadChanges()
    {
        // Arrange
        var repository = new Mock<ISettingsRepository>();
        repository.Setup(store => store.GetByIdAsync(
                new SettingsGroupLookupKey(null, "monitoring"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                var storedGroup = new SettingsGroup { Id = Guid.NewGuid(), Name = "monitoring" };
                storedGroup.Settings.Add(new Setting { Key = "system-default", Value = "false" });
                storedGroup.Settings.Add(new Setting { Key = "max-spread", Value = "0" });
                return storedGroup;
            });
        repository.Setup(store => store.PatchAsync(
                It.IsAny<SettingsGroupPatch>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var subject = new SettingsService(
            repository.Object,
            _hierarchy.Object,
            _transactionManager.Object,
            [],
            [],
            NullLogger<SettingsService>.Instance,
            Mock.Of<IExtensionRepository>(),
            Mock.Of<IServiceScopeFactory>());
        var patch = new SettingsGroupPatch
        {
            Name = "monitoring",
            Settings = new()
            {
                Update = new Dictionary<string, string> { ["system-default"] = "false", ["max-spread"] = "2" }
            }
        };

        // Act
        await subject.PatchSystemGroupAsync("monitoring", patch, Guid.NewGuid()).ConfigureAwait(false);

        // Assert
        repository.Verify(store => store.PatchAsync(
                It.Is<SettingsGroupPatch>(sent => sent.Settings.Add == null && sent.Settings.Remove == null &&
                    sent.Settings.Update!.Count == 1 && sent.Settings.Update["max-spread"] == "2"),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
        _hierarchy.Verify(store => store.RecomputeAllEffectiveValuesAsync(It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [TestCase("Home Lab", "Home Lab")]
    [TestCase("  Home Lab  ", "Home Lab")]
    [TestCase("", "Magpie")]
    [TestCase("   ", "Magpie")]
    public async Task GetInstanceNameAsync_ReturnsTrimmedNameOrDefault_WhenInstanceNameSettingExists(
        string storedName,
        string expectedName)
    {
        // Arrange
        var repository = new Mock<ISettingsRepository>();
        repository.Setup(store => store.GetByIdAsync(
                new SettingsGroupLookupKey(null, "general"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SettingsGroup
            {
                Name = "general",
                Settings = { new Setting { Key = "instanceName", Value = storedName } }
            });
        var subject = new SettingsService(
            repository.Object,
            _hierarchy.Object,
            _transactionManager.Object,
            [],
            [],
            NullLogger<SettingsService>.Instance,
            Mock.Of<IExtensionRepository>(),
            Mock.Of<IServiceScopeFactory>());

        // Act
        var name = await subject.GetInstanceNameAsync().ConfigureAwait(false);

        // Assert
        Assert.That(name, Is.EqualTo(expectedName));
    }

    [Test]
    public async Task GetInstanceNameAsync_ReturnsMagpie_WhenGeneralGroupIsMissing()
    {
        // Act
        var name = await _subject.GetInstanceNameAsync().ConfigureAwait(false);

        // Assert
        Assert.That(name, Is.EqualTo("Magpie"));
    }

    private sealed class TestSettingsSchemaProvider : IExtensionSettingsSchemaProvider
    {
        public IEnumerable<ExtensionSettingsGroupDefinition> Groups { get; set; } = [];

        public IEnumerable<ExtensionSettingsGroupDefinition> GetSettingsGroups() => Groups;
    }
}
