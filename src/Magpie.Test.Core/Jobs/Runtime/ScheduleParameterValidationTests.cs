using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Jobs;
using Magpie.Abstractions.Repositories;
using Magpie.Core.Jobs;
using Magpie.Core.Jobs.Fulfillment;
using Magpie.Core.Jobs.Metadata;
using Magpie.Core.Jobs.Runtime;
using Magpie.Models;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Patch;
using Magpie.Sdk.Jobs;
using Magpie.Sdk.Services;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Jobs.Runtime;

[TestFixture]
public sealed class ScheduleParameterValidationTests
{
    private List<JobSpec> _specs = null!;
    private Mock<IScheduleTriggerRepository> _repository = null!;
    private IJobManager _subject = null!;

    public static IEnumerable<TestCaseData> EmptyMapCases =>
    [
        new(DownloadImportScanJob.Spec,
            """
            {"jobType":"placeholder","scope":1,"name":"Empty map","schedule":"0 0 1 1 *","parameters":null}
            """),
        new(DownloadImportScanJob.Spec,
            """
            {"jobType":"placeholder","scope":1,"name":"Empty map","schedule":"0 0 1 1 *"}
            """),
        new(DownloadImportScanJob.Spec,
            """
            {"jobType":"placeholder","scope":1,"name":"Empty map","schedule":"0 0 1 1 *","parameters":{}}
            """),
        new(LibraryImportMatchResolutionJob.Spec,
            """
            {"jobType":"placeholder","scope":1,"name":"Empty map","schedule":"0 0 1 1 *","parameters":null}
            """),
        new(LibraryImportMatchResolutionJob.Spec,
            """
            {"jobType":"placeholder","scope":1,"name":"Empty map","schedule":"0 0 1 1 *"}
            """),
        new(LibraryImportMatchResolutionJob.Spec,
            """
            {"jobType":"placeholder","scope":1,"name":"Empty map","schedule":"0 0 1 1 *","parameters":{}}
            """),
        new(RootFolderContentDiscoveryJob.Spec,
            """
            {"jobType":"placeholder","scope":1,"name":"Empty map","schedule":"0 0 1 1 *","parameters":null}
            """),
        new(RootFolderContentDiscoveryJob.Spec,
            """
            {"jobType":"placeholder","scope":1,"name":"Empty map","schedule":"0 0 1 1 *","parameters":{}}
            """),
    ];

    public static IEnumerable<string> EmptyMapRequests =>
    [
        """
        {"jobType":"placeholder","scope":1,"name":"Empty map","schedule":"0 0 1 1 *","parameters":null}
        """,
        """
        {"jobType":"placeholder","scope":1,"name":"Empty map","schedule":"0 0 1 1 *"}
        """,
        """
        {"jobType":"placeholder","scope":1,"name":"Empty map","schedule":"0 0 1 1 *","parameters":{}}
        """,
    ];

    public static IEnumerable<TestCaseData> GuidCases =>
    [
        new(IndexerFeedSyncItemJob.Spec, "feedId", "Parameter feedId must be a valid GUID."),
        new(MediaFileRenameJob.Spec, "mediaFileId", "Parameter mediaFileId must be a valid GUID."),
        new(WishlistRefreshItemJob.Spec, "wishlistId", "Parameter wishlistId must be a valid GUID."),
        new(ReleaseSearchItemJob.Spec, "contentHierarchyItemId",
            "Parameter contentHierarchyItemId must be a valid GUID."),
        new(ContentMetadataRefreshItemJob.Spec, "contentHierarchyItemId",
            "Parameter contentHierarchyItemId must be a valid GUID."),
        new(SubjectMetadataRefreshItemJob.Spec, "subjectId", "Parameter subjectId must be a valid GUID."),
        new(RootFolderContentDiscoveryJob.Spec, "rootFolderId", "Parameter rootFolderId must be a valid GUID."),
        new(RootFolderContentDiscoveryJob.Spec, "contentHierarchyItemId",
            "Parameter contentHierarchyItemId must be a valid GUID."),
        new(DownloadImportItemJob.Spec, "downloadId", "Parameter downloadId must be a valid GUID."),
        new(MediaServerRefreshJob.Spec, "contentHierarchyItemId",
            "Parameter contentHierarchyItemId must be a valid GUID."),
        new(MediaInfoExtractScanJob.Spec, "contentHierarchyItemId",
            "Parameter contentHierarchyItemId must be a valid GUID."),
        new(MediaFileReconciliationJob.Spec, "rootFolderId", "Parameter rootFolderId must be a valid GUID."),
        new(MediaFileReconciliationJob.Spec, "contentHierarchyItemId",
            "Parameter contentHierarchyItemId must be a valid GUID."),
    ];

    public static IEnumerable<TestCaseData> PatchCases =>
    [
        new("00000000-0000-0000-0000-000000000001", true,
            new ScheduleTriggerPatch
            {
                Parameters = new DictionaryPatch<string, string, string>
                {
                    Update = new Dictionary<string, string> { ["feedId"] = "not-a-guid" }
                }
            }),
        new("not-a-guid", true, new ScheduleTriggerPatch { Name = new() { Value = "Renamed" } }),
        new("not-a-guid", false, new ScheduleTriggerPatch { Enabled = new() { Value = true } }),
    ];

    [SetUp]
    public void SetUp()
    {
        _specs = [];
        _repository = new Mock<IScheduleTriggerRepository>();
        var transactions = new Mock<ITransactionManager>();
        transactions.Setup(manager => manager.ExecuteWithTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<bool>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<bool>> operation, CancellationToken token) => operation(token));
        _subject = new JobManager(Mock.Of<IJobRuntime>(),
            Mock.Of<IJobRegistry>(registry => registry.ListJobSpecs() == _specs),
            Mock.Of<IJobExecutionRepository>(), _repository.Object, Mock.Of<IEventTriggerRepository>(),
            Mock.Of<IJobConcurrencyScheduler>(), Mock.Of<IJobExecutionScopeFactory>(), TimeProvider.System,
            NullLogger<JobManager>.Instance, transactions.Object);
    }

    [TestCaseSource(nameof(EmptyMapCases))]
    public async Task CreateScheduleTriggerAsync_PersistsEmptyMap_WhenRequestHasNoParameterValues(
        JobSpec spec, string json)
    {
        // Arrange
        _specs.Add(spec);
        var create = JsonSerializer.Deserialize<ScheduleTriggerCreate>(json,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))! with
        { JobType = spec.JobType, Scope = spec.Scope };
        _repository.Setup(repository => repository.CreateAsync(It.IsAny<ScheduleTrigger>(),
            It.IsAny<Guid?>(), It.IsAny<CancellationToken>())).ReturnsAsync((ScheduleTrigger trigger,
                Guid? _, CancellationToken _) => trigger);

        // Act
        var result = await _subject.CreateScheduleTriggerAsync(create, Guid.Empty, Role.Admin).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Parameters, Is.Empty);
            Assert.That(JsonSerializer.Serialize(result.Parameters), Is.EqualTo("{}"));
        }

        _repository.Verify(repository => repository.CreateAsync(
            It.Is<ScheduleTrigger>(trigger => trigger.Parameters != null && trigger.Parameters.Count == 0),
            It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestCaseSource(nameof(EmptyMapRequests))]
    public void CreateScheduleTriggerAsync_RejectsEmptyMap_WhenJobRequiresParameter(string json)
    {
        // Arrange
        _specs.Add(IndexerFeedSyncItemJob.Spec);
        var create = JsonSerializer.Deserialize<ScheduleTriggerCreate>(json,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))! with
        {
            JobType = IndexerFeedSyncItemJob.Spec.JobType,
            Scope = IndexerFeedSyncItemJob.Spec.Scope
        };

        // Act
        var error = Assert.ThrowsAsync<MagpieException>(() =>
            _subject.CreateScheduleTriggerAsync(create, Guid.Empty, Role.Admin));

        // Assert
        Assert.That(error!.Message, Is.EqualTo("Required parameter feedId is missing."));
        _repository.Verify(repository => repository.CreateAsync(It.IsAny<ScheduleTrigger>(),
            It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task PatchScheduleTriggerAsync_PreservesMap_WhenRequestHasNullParameterPatch()
    {
        // Arrange
        _specs.Add(IndexerFeedSyncItemJob.Spec);
        var stored = new ScheduleTrigger
        {
            Id = Guid.Empty,
            JobType = IndexerFeedSyncItemJob.Spec.JobType,
            Scope = JobScope.Administrative,
            Name = "Original",
            Enabled = true,
            Schedule = "0 0 1 1 *",
            Parameters = new Dictionary<string, string> { ["feedId"] = "00000000-0000-0000-0000-000000000001" }
        };
        _repository.Setup(repository => repository.GetByIdAsync(Guid.Empty, It.IsAny<CancellationToken>()))
            .ReturnsAsync(stored);
        var patch = JsonSerializer.Deserialize<ScheduleTriggerPatch>(
            """{"parameters":null,"name":{"value":"Renamed"}}""",
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

        // Act
        await _subject.PatchScheduleTriggerAsync(Guid.Empty, patch, Guid.Empty, callerRole: Role.Admin)
            .ConfigureAwait(false);

        // Assert
        Assert.That(stored.Parameters["feedId"], Is.EqualTo("00000000-0000-0000-0000-000000000001"));
        _repository.Verify(repository => repository.PatchAsync(
            It.Is<ScheduleTriggerPatch>(applied => applied.Parameters == null && applied.Name!.Value == "Renamed"),
            It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public void PatchScheduleTriggerAsync_RejectsInvalidStoredMap_WhenRequestHasNullParameterPatch()
    {
        // Arrange
        _specs.Add(IndexerFeedSyncItemJob.Spec);
        var stored = new ScheduleTrigger
        {
            Id = Guid.Empty,
            JobType = IndexerFeedSyncItemJob.Spec.JobType,
            Scope = JobScope.Administrative,
            Name = "Original",
            Enabled = true,
            Schedule = "0 0 1 1 *",
            Parameters = new Dictionary<string, string> { ["feedId"] = "not-a-guid" }
        };
        _repository.Setup(repository => repository.GetByIdAsync(Guid.Empty, It.IsAny<CancellationToken>()))
            .ReturnsAsync(stored);
        var patch = JsonSerializer.Deserialize<ScheduleTriggerPatch>(
            """{"parameters":null,"name":{"value":"Renamed"}}""",
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

        // Act
        var error = Assert.ThrowsAsync<MagpieException>(() =>
            _subject.PatchScheduleTriggerAsync(Guid.Empty, patch, Guid.Empty, callerRole: Role.Admin));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(error!.Message, Is.EqualTo("Parameter feedId must be a valid GUID."));
            Assert.That(stored.Parameters["feedId"], Is.EqualTo("not-a-guid"));
            Assert.That(stored.Name, Is.EqualTo("Original"));
        }

        _repository.Verify(repository => repository.PatchAsync(It.IsAny<ScheduleTriggerPatch>(),
            It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestCaseSource(nameof(GuidCases))]
    public void CreateScheduleTriggerAsync_RejectsMalformedGuid_WhenJobDeclaresGuid(
        JobSpec spec, string parameter, string expected)
    {
        // Arrange
        _specs.Add(spec);
        var create = new ScheduleTriggerCreate
        {
            JobType = spec.JobType,
            Scope = spec.Scope,
            Name = "Invalid",
            Schedule = "0 0 1 1 *",
            Enabled = true,
            Parameters = new Dictionary<string, string> { [parameter] = "not-a-guid" }
        };

        // Act
        var error = Assert.ThrowsAsync<MagpieException>(() =>
            _subject.CreateScheduleTriggerAsync(create, Guid.Empty, Role.Admin));

        // Assert
        Assert.That(error!.Message, Is.EqualTo(expected));
        _repository.Verify(repository => repository.CreateAsync(It.IsAny<ScheduleTrigger>(),
            It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestCase("not-a-guid", "00000000-0000-0000-0000-000000000001", "Parameter releaseId must be a valid GUID.")]
    [TestCase("00000000-0000-0000-0000-000000000001", "not-a-guid",
        "Parameter contentHierarchyItemId must be a valid GUID.")]
    public void CreateScheduleTriggerAsync_RejectsMalformedGuid_WhenReleaseJobHasTwoRequiredIds(
        string releaseId, string contentId, string expected)
    {
        // Arrange
        _specs.Add(ReleaseDownloadItemJob.Spec);
        var create = new ScheduleTriggerCreate
        {
            JobType = ReleaseDownloadItemJob.Spec.JobType,
            Scope = JobScope.Content,
            Name = "Invalid",
            Schedule = "0 0 1 1 *",
            Enabled = true,
            Parameters = new Dictionary<string, string>
            {
                ["releaseId"] = releaseId,
                ["contentHierarchyItemId"] = contentId
            }
        };

        // Act
        var error = Assert.ThrowsAsync<MagpieException>(() =>
            _subject.CreateScheduleTriggerAsync(create, Guid.Empty, Role.Admin));

        // Assert
        Assert.That(error!.Message, Is.EqualTo(expected));
        _repository.Verify(repository => repository.CreateAsync(It.IsAny<ScheduleTrigger>(),
            It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestCaseSource(nameof(PatchCases))]
    public void PatchScheduleTriggerAsync_RejectsMalformedGuid_WhenEffectiveValueIsInvalid(
        string current, bool enabled, ScheduleTriggerPatch patch)
    {
        // Arrange
        _specs.Add(IndexerFeedSyncItemJob.Spec);
        var stored = new ScheduleTrigger
        {
            Id = Guid.Empty,
            JobType = IndexerFeedSyncItemJob.Spec.JobType,
            Scope = JobScope.Administrative,
            Name = "Original",
            Enabled = enabled,
            Schedule = "0 0 1 1 *",
            Parameters = new Dictionary<string, string> { ["feedId"] = current }
        };
        _repository.Setup(repository => repository.GetByIdAsync(Guid.Empty, It.IsAny<CancellationToken>()))
            .ReturnsAsync(stored);

        // Act
        var error = Assert.ThrowsAsync<MagpieException>(() =>
            _subject.PatchScheduleTriggerAsync(Guid.Empty, patch, Guid.Empty, callerRole: Role.Admin));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(error!.Message, Is.EqualTo("Parameter feedId must be a valid GUID."));
            Assert.That(stored.Parameters["feedId"], Is.EqualTo(current));
            Assert.That(stored.Name, Is.EqualTo("Original"));
            Assert.That(stored.Enabled, Is.EqualTo(enabled));
        }

        _repository.Verify(repository => repository.PatchAsync(It.IsAny<ScheduleTriggerPatch>(),
            It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestCase("00000000-0000-0000-0000-000000000001")]
    [TestCase("00000000000000000000000000000001")]
    [TestCase(" {ABCDEFFF-1234-5678-90AB-CDEF12345678} ")]
    public async Task CreateScheduleTriggerAsync_AcceptsGuid_WhenFormatIsAcceptedByHandler(string value)
    {
        // Arrange
        _specs.Add(IndexerFeedSyncItemJob.Spec);
        var create = new ScheduleTriggerCreate
        {
            JobType = IndexerFeedSyncItemJob.Spec.JobType,
            Scope = JobScope.Administrative,
            Name = "Valid",
            Schedule = "0 0 1 1 *",
            Enabled = true,
            Parameters = new Dictionary<string, string> { ["feedId"] = value }
        };
        _repository.Setup(repository => repository.CreateAsync(It.IsAny<ScheduleTrigger>(),
            It.IsAny<Guid?>(), It.IsAny<CancellationToken>())).ReturnsAsync((ScheduleTrigger trigger,
                Guid? _, CancellationToken _) => trigger);

        // Act
        await _subject.CreateScheduleTriggerAsync(create, Guid.Empty, Role.Admin).ConfigureAwait(false);

        // Assert
        _repository.Verify(repository => repository.CreateAsync(
            It.Is<ScheduleTrigger>(trigger => trigger.Parameters["feedId"] == value && trigger.Enabled),
            It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase(" ")]
    public async Task CreateScheduleTriggerAsync_AcceptsOptionalGuid_WhenValueIsEmpty(string? value)
    {
        // Arrange
        _specs.Add(MediaServerRefreshJob.Spec);
        var create = new ScheduleTriggerCreate
        {
            JobType = MediaServerRefreshJob.Spec.JobType,
            Scope = JobScope.Administrative,
            Name = "Valid",
            Schedule = "0 0 1 1 *",
            Enabled = true,
            Parameters = new Dictionary<string, string> { ["contentHierarchyItemId"] = value! }
        };
        _repository.Setup(repository => repository.CreateAsync(It.IsAny<ScheduleTrigger>(),
            It.IsAny<Guid?>(), It.IsAny<CancellationToken>())).ReturnsAsync((ScheduleTrigger trigger,
                Guid? _, CancellationToken _) => trigger);

        // Act
        await _subject.CreateScheduleTriggerAsync(create, Guid.Empty, Role.Admin).ConfigureAwait(false);

        // Assert
        _repository.Verify(repository => repository.CreateAsync(It.IsAny<ScheduleTrigger>(),
            It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase(" ")]
    [TestCase("0")]
    [TestCase("-1")]
    [TestCase("1.5")]
    [TestCase("2147483648")]
    public void CreateScheduleTriggerAsync_RejectsRecoveryMinutes_WhenValueIsNotPositiveInteger(string? value)
    {
        // Arrange
        _specs.Add(RootFolderContentDiscoveryJob.Spec);
        var create = new ScheduleTriggerCreate
        {
            JobType = RootFolderContentDiscoveryJob.Spec.JobType,
            Scope = JobScope.Content,
            Name = "Invalid",
            Schedule = "0 0 1 1 *",
            Enabled = true,
            Parameters = new Dictionary<string, string>
            {
                ["rootFolderId"] = "00000000-0000-0000-0000-000000000001",
                ["recoveryInactivityMinutes"] = value!
            }
        };

        // Act
        var error = Assert.ThrowsAsync<MagpieException>(() =>
            _subject.CreateScheduleTriggerAsync(create, Guid.Empty, Role.Admin));

        // Assert
        Assert.That(error!.Message, Is.EqualTo("Parameter recoveryInactivityMinutes must be a positive integer."));
        _repository.Verify(repository => repository.CreateAsync(It.IsAny<ScheduleTrigger>(),
            It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestCase("unknown")]
    [TestCase("99")]
    [TestCase("-1")]
    [TestCase("3")]
    [TestCase("2147483647")]
    [TestCase("2147483648")]
    public void CreateScheduleTriggerAsync_RejectsCollisionMode_WhenValueIsUnknown(string value)
    {
        // Arrange
        _specs.Add(DownloadImportItemJob.Spec);
        var create = new ScheduleTriggerCreate
        {
            JobType = DownloadImportItemJob.Spec.JobType,
            Scope = JobScope.Content,
            Name = "Invalid",
            Schedule = "0 0 1 1 *",
            Enabled = true,
            Parameters = new Dictionary<string, string>
            {
                ["downloadId"] = "00000000-0000-0000-0000-000000000001",
                ["collisionMode"] = value
            }
        };

        // Act
        var error = Assert.ThrowsAsync<MagpieException>(() =>
            _subject.CreateScheduleTriggerAsync(create, Guid.Empty, Role.Admin));

        // Assert
        Assert.That(error!.Message, Is.EqualTo("Parameter collisionMode must be fail, skip, or replace."));
        _repository.Verify(repository => repository.CreateAsync(It.IsAny<ScheduleTrigger>(),
            It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public void CreateScheduleTriggerAsync_ReturnsValidationError_WhenExtensionValidatorThrows()
    {
        // Arrange
        var spec = new JobSpec
        {
            JobType = "extension",
            DisplayName = "Extension",
            Category = "Test",
            Scope = JobScope.Administrative,
            SupportedTriggerModes = [JobTriggerMode.Schedule],
            Parameters =
            [
                new("feedId", null, DataType.String, true, null)
                    { ValidateValue = _ => throw new FormatException("Invalid extension value.") }
            ]
        };
        _specs.Add(spec);
        var create = new ScheduleTriggerCreate
        {
            JobType = "extension",
            Scope = JobScope.Administrative,
            Name = "Invalid",
            Schedule = "0 0 1 1 *",
            Enabled = true,
            Parameters = new Dictionary<string, string> { ["feedId"] = "text" }
        };

        // Act
        var error = Assert.ThrowsAsync<MagpieException>(() =>
            _subject.CreateScheduleTriggerAsync(create, Guid.Empty, Role.Admin));

        // Assert
        Assert.That(error!.Message, Is.EqualTo("Parameter feedId could not be validated."));
        _repository.Verify(repository => repository.CreateAsync(It.IsAny<ScheduleTrigger>(),
            It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public void Serialize_OmitsValidator_WhenParameterHasValidationCallback()
    {
        // Arrange
        var parameter = new JobParameter("feedId", null, DataType.String, true, null)
        {
            ValidateValue = value => Guid.TryParse(value, out _) ? null : "must be a valid GUID.",
            ValidateScheduleValue = (_, _, _) => null
        };

        // Act
        var json = JsonSerializer.Serialize(parameter);

        // Assert
        Assert.That(json, Is.EqualTo(
            "{\"Name\":\"feedId\",\"Description\":null,\"Type\":0,\"Required\":true,\"DefaultValue\":null}"));
    }

    [Test]
    public async Task CreateScheduleTriggerAsync_AcceptsString_WhenExtensionDoesNotDeclareValidation()
    {
        // Arrange
        _specs.Add(new JobSpec
        {
            JobType = "extension",
            DisplayName = "Extension",
            Category = "Test",
            Scope = JobScope.Administrative,
            SupportedTriggerModes = [JobTriggerMode.Schedule],
            Parameters = [new("feedId", null, DataType.String, true, null)],
            LockPartitionFields = ["feedId"],
            GuidLockPartitionFields = ["feedId"]
        });
        var create = new ScheduleTriggerCreate
        {
            JobType = "extension",
            Scope = JobScope.Administrative,
            Name = "Text ID",
            Schedule = "0 0 1 1 *",
            Enabled = true,
            Parameters = new Dictionary<string, string> { ["feedId"] = "not-a-guid" }
        };

        // Act
        await _subject.CreateScheduleTriggerAsync(create, Guid.Empty, Role.Admin).ConfigureAwait(false);

        // Assert
        _repository.Verify(repository => repository.CreateAsync(
            It.Is<ScheduleTrigger>(trigger => trigger.Parameters["feedId"] == "not-a-guid"),
            It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task CreateScheduleTriggerAsync_AcceptsDefaults_WhenOptionalParametersAreOmitted()
    {
        // Arrange
        _specs.Add(RootFolderContentDiscoveryJob.Spec);
        var create = new ScheduleTriggerCreate
        {
            JobType = RootFolderContentDiscoveryJob.Spec.JobType,
            Scope = JobScope.Content,
            Name = "Default",
            Schedule = "0 0 1 1 *",
            Enabled = true,
            Parameters = new Dictionary<string, string> { ["rootFolderId"] = "00000000-0000-0000-0000-000000000001" }
        };

        // Act
        await _subject.CreateScheduleTriggerAsync(create, Guid.Empty, Role.Admin).ConfigureAwait(false);

        // Assert
        _repository.Verify(repository => repository.CreateAsync(
            It.Is<ScheduleTrigger>(trigger => !trigger.Parameters.ContainsKey("recoveryInactivityMinutes")),
            It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase(" ")]
    public void CreateScheduleTriggerAsync_RejectsName_WhenNameIsNullOrBlank(string? name)
    {
        // Arrange
        _specs.Add(DownloadImportScanJob.Spec);
        var create = new ScheduleTriggerCreate
        {
            JobType = DownloadImportScanJob.Spec.JobType,
            Scope = DownloadImportScanJob.Spec.Scope,
            Name = name!,
            Schedule = "0 0 1 1 *"
        };

        // Act
        var error = Assert.ThrowsAsync<MagpieException>(() =>
            _subject.CreateScheduleTriggerAsync(create, Guid.Empty, Role.Admin));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(error!.Message, Is.EqualTo("Name is required."));
            _repository.Verify(repository => repository.CreateAsync(It.IsAny<ScheduleTrigger>(),
                It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
        }
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase(" ")]
    public void CreateScheduleTriggerAsync_RejectsJobType_WhenJobTypeIsNullOrBlank(string? jobType)
    {
        // Arrange
        _specs.Add(DownloadImportScanJob.Spec);
        var create = new ScheduleTriggerCreate
        {
            JobType = jobType!,
            Scope = DownloadImportScanJob.Spec.Scope,
            Name = "Nightly scan",
            Schedule = "0 0 1 1 *"
        };

        // Act
        var error = Assert.ThrowsAsync<MagpieException>(() =>
            _subject.CreateScheduleTriggerAsync(create, Guid.Empty, Role.Admin));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(error!.Message, Is.EqualTo("Job type is required."));
            _repository.Verify(repository => repository.CreateAsync(It.IsAny<ScheduleTrigger>(),
                It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
        }
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase(" ")]
    public void CreateScheduleTriggerAsync_RejectsSchedule_WhenScheduleIsNullOrBlank(string? schedule)
    {
        // Arrange
        _specs.Add(DownloadImportScanJob.Spec);
        var create = new ScheduleTriggerCreate
        {
            JobType = DownloadImportScanJob.Spec.JobType,
            Scope = DownloadImportScanJob.Spec.Scope,
            Name = "Nightly scan",
            Schedule = schedule!
        };

        // Act
        var error = Assert.ThrowsAsync<MagpieException>(() =>
            _subject.CreateScheduleTriggerAsync(create, Guid.Empty, Role.Admin));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(error!.Message, Is.EqualTo("Schedule is required."));
            _repository.Verify(repository => repository.CreateAsync(It.IsAny<ScheduleTrigger>(),
                It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
        }
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase(" ")]
    public void CreateScheduleTriggerAsync_RejectsCaller_WhenCallerLacksPermissionAndNameIsNullOrBlank(string? name)
    {
        // Arrange
        _specs.Add(DownloadImportScanJob.Spec);
        var create = new ScheduleTriggerCreate
        {
            JobType = DownloadImportScanJob.Spec.JobType,
            Scope = DownloadImportScanJob.Spec.Scope,
            Name = name!,
            Schedule = "0 0 1 1 *"
        };

        // Act
        var error = Assert.ThrowsAsync<MagpieException>(() =>
            _subject.CreateScheduleTriggerAsync(create, Guid.Empty, Role.Viewer));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(error!.Message, Is.EqualTo("The owner role cannot create this trigger for this job type."));
            _repository.Verify(repository => repository.CreateAsync(It.IsAny<ScheduleTrigger>(),
                It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
        }
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase(" ")]
    public void PatchScheduleTriggerAsync_RejectsName_WhenNameIsNullOrBlank(string? name)
    {
        // Arrange
        _specs.Add(DownloadImportScanJob.Spec);
        var stored = new ScheduleTrigger
        {
            Id = Guid.Empty,
            JobType = DownloadImportScanJob.Spec.JobType,
            Scope = DownloadImportScanJob.Spec.Scope,
            Name = "Original",
            Enabled = true,
            Schedule = "0 0 1 1 *",
            Parameters = []
        };
        _repository.Setup(repository => repository.GetByIdAsync(Guid.Empty, It.IsAny<CancellationToken>()))
            .ReturnsAsync(stored);
        var patch = new ScheduleTriggerPatch { Name = new() { Value = name! } };

        // Act
        var error = Assert.ThrowsAsync<MagpieException>(() =>
            _subject.PatchScheduleTriggerAsync(Guid.Empty, patch, Guid.Empty, callerRole: Role.Admin));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(error!.Message, Is.EqualTo("Name is required."));
            Assert.That(stored.Name, Is.EqualTo("Original"));
            _repository.Verify(repository => repository.PatchAsync(It.IsAny<ScheduleTriggerPatch>(),
                It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
        }
    }

    [Test]
    public async Task PatchScheduleTriggerAsync_PatchesTrigger_WhenNameIsValid()
    {
        // Arrange
        _specs.Add(DownloadImportScanJob.Spec);
        var stored = new ScheduleTrigger
        {
            Id = Guid.Empty,
            JobType = DownloadImportScanJob.Spec.JobType,
            Scope = DownloadImportScanJob.Spec.Scope,
            Name = "Original",
            Enabled = true,
            Schedule = "0 0 1 1 *",
            Parameters = []
        };
        _repository.Setup(repository => repository.GetByIdAsync(Guid.Empty, It.IsAny<CancellationToken>()))
            .ReturnsAsync(stored);
        var patch = new ScheduleTriggerPatch { Name = new() { Value = "Renamed" } };

        // Act
        await _subject.PatchScheduleTriggerAsync(Guid.Empty, patch, Guid.Empty, callerRole: Role.Admin)
            .ConfigureAwait(false);

        // Assert
        _repository.Verify(repository => repository.PatchAsync(
            It.Is<ScheduleTriggerPatch>(applied => applied.Name!.Value == "Renamed"),
            It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task CreateScheduleTriggerAsync_CreatesTrigger_WhenNameIsValid()
    {
        // Arrange
        _specs.Add(DownloadImportScanJob.Spec);
        var create = new ScheduleTriggerCreate
        {
            JobType = DownloadImportScanJob.Spec.JobType,
            Scope = DownloadImportScanJob.Spec.Scope,
            Name = "Nightly scan",
            Schedule = "0 0 1 1 *"
        };

        // Act
        await _subject.CreateScheduleTriggerAsync(create, Guid.Empty, Role.Admin).ConfigureAwait(false);

        // Assert
        _repository.Verify(repository => repository.CreateAsync(
            It.Is<ScheduleTrigger>(trigger => trigger.Name == "Nightly scan"),
            It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase(" ")]
    [TestCase("FAIL")]
    [TestCase("skip")]
    [TestCase("replace")]
    [TestCase("0")]
    [TestCase("1")]
    [TestCase("2")]
    public async Task CreateScheduleTriggerAsync_AcceptsCollisionMode_WhenHandlerAcceptsValue(string? value)
    {
        // Arrange
        _specs.Add(DownloadImportItemJob.Spec);
        var create = new ScheduleTriggerCreate
        {
            JobType = DownloadImportItemJob.Spec.JobType,
            Scope = JobScope.Content,
            Name = "Valid",
            Schedule = "0 0 1 1 *",
            Enabled = true,
            Parameters = new Dictionary<string, string>
            {
                ["downloadId"] = "00000000-0000-0000-0000-000000000001",
                ["collisionMode"] = value!
            }
        };

        // Act
        await _subject.CreateScheduleTriggerAsync(create, Guid.Empty, Role.Admin).ConfigureAwait(false);

        // Assert
        _repository.Verify(repository => repository.CreateAsync(It.IsAny<ScheduleTrigger>(),
            It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task PatchScheduleTriggerAsync_AcceptsRepair_WhenPatchReplacesInvalidGuid()
    {
        // Arrange
        _specs.Add(IndexerFeedSyncItemJob.Spec);
        _repository.Setup(repository => repository.GetByIdAsync(Guid.Empty, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScheduleTrigger
            {
                Id = Guid.Empty,
                JobType = IndexerFeedSyncItemJob.Spec.JobType,
                Scope = JobScope.Administrative,
                Name = "Invalid",
                Schedule = "0 0 1 1 *",
                Enabled = false,
                Parameters = new Dictionary<string, string> { ["feedId"] = "not-a-guid" }
            });
        var patch = new ScheduleTriggerPatch
        {
            Enabled = new() { Value = true },
            Parameters = new()
            {
                Add = new Dictionary<string, string> { ["feedId"] = "still-invalid" },
                Update = new Dictionary<string, string> { ["feedId"] = "00000000-0000-0000-0000-000000000001" }
            }
        };

        // Act
        await _subject.PatchScheduleTriggerAsync(Guid.Empty, patch, Guid.Empty, callerRole: Role.Admin)
            .ConfigureAwait(false);

        // Assert
        _repository.Verify(repository => repository.PatchAsync(
            It.Is<ScheduleTriggerPatch>(applied => applied.Enabled!.Value),
            It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task CreateEventTriggerAsync_AcceptsBinding_WhenScheduleParameterHasGuidValidator()
    {
        // Arrange
        _specs.Add(IndexerFeedSyncItemJob.Spec);
        var create = new EventTriggerCreate
        {
            JobType = IndexerFeedSyncItemJob.Spec.JobType,
            Scope = JobScope.Administrative,
            Name = "Bound ID",
            Enabled = true,
            EventFilter = new(),
            Parameters = new Dictionary<string, string> { ["feedId"] = "${event.feedId}" }
        };

        // Act
        await _subject.CreateEventTriggerAsync(create, Guid.Empty, Role.Admin).ConfigureAwait(false);

        // Assert
        Assert.That(create.Parameters["feedId"], Is.EqualTo("${event.feedId}"));
    }

    [TestCase("99")]
    [TestCase("-1")]
    public void PatchScheduleTriggerAsync_RejectsCollisionMode_WhenNumericMemberIsUndefined(string value)
    {
        // Arrange
        _specs.Add(DownloadImportItemJob.Spec);
        var stored = new ScheduleTrigger
        {
            Id = Guid.Empty,
            JobType = DownloadImportItemJob.Spec.JobType,
            Scope = JobScope.Content,
            Name = "Original",
            Enabled = true,
            Schedule = "0 0 1 1 *",
            Parameters = new Dictionary<string, string>
            {
                ["downloadId"] = "00000000-0000-0000-0000-000000000001",
                ["collisionMode"] = "2"
            }
        };
        _repository.Setup(repository => repository.GetByIdAsync(Guid.Empty, It.IsAny<CancellationToken>()))
            .ReturnsAsync(stored);
        var patch = new ScheduleTriggerPatch
        {
            Parameters = new() { Update = new Dictionary<string, string> { ["collisionMode"] = value } }
        };

        // Act
        var error = Assert.ThrowsAsync<MagpieException>(() =>
            _subject.PatchScheduleTriggerAsync(Guid.Empty, patch, Guid.Empty, callerRole: Role.Admin));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(error!.Message, Is.EqualTo("Parameter collisionMode must be fail, skip, or replace."));
            Assert.That(stored.Parameters["collisionMode"], Is.EqualTo("2"));
        }

        _repository.Verify(repository => repository.PatchAsync(It.IsAny<ScheduleTriggerPatch>(),
            It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
