using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Jobs;
using Magpie.Abstractions.Repositories;
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
public sealed class JobManagerTriggerAuthorizationTests
{
    private const string RoleRefusal = "The owner role cannot create this trigger for this job type.";

    private const string TargetRefusal = "A user trigger must target a specific item.";

    private const string Target = "contentHierarchyItemId";

    private static readonly Guid CallerId = new("00000000-0000-0000-0000-00000000000a");

    private static readonly Guid OtherUserId = new("00000000-0000-0000-0000-00000000000b");

    private Mock<IScheduleTriggerRepository> _scheduleTriggers = null!;
    private Mock<IEventTriggerRepository> _eventTriggers = null!;
    private IJobManager _subject = null!;

    public static IEnumerable<TestCaseData> ScheduleCreateAllowedCases
    {
        get
        {
            yield return new TestCaseData("Flagged", JobScope.User, Role.Requester);
            yield return new TestCaseData("FlaggedScheduleOnly", JobScope.User, Role.Requester);
            yield return new TestCaseData("Flagged", JobScope.Administrative, Role.Manager);
            yield return new TestCaseData("Unflagged", JobScope.User, Role.Manager);
            yield return new TestCaseData("Administrative", JobScope.Administrative, Role.Admin);
        }
    }

    public static IEnumerable<TestCaseData> ScheduleCreateRefusedCases
    {
        get
        {
            var literal = new Dictionary<string, string> { [Target] = "00000000-0000-0000-0000-000000000001" };
            yield return new TestCaseData("Unflagged", JobScope.User, Role.Requester, literal, RoleRefusal);
            yield return new TestCaseData("Flagged", JobScope.Administrative, Role.Requester, literal, RoleRefusal);
            yield return new TestCaseData("FlaggedEventOnly", JobScope.User, Role.Requester, literal, RoleRefusal);
            yield return new TestCaseData("Flagged", JobScope.System, Role.Requester, literal, RoleRefusal);
            yield return new TestCaseData("Flagged", JobScope.User, Role.Viewer, literal, RoleRefusal);
            yield return new TestCaseData("Administrative", JobScope.Administrative, Role.Manager, literal,
                RoleRefusal);
            yield return new TestCaseData("Builtin", JobScope.User, Role.Admin, literal,
                "Only Magpie can create, change, or delete built-in triggers.");
            yield return new TestCaseData("Flagged", JobScope.User, Role.Requester,
                new Dictionary<string, string>(), TargetRefusal);
            yield return new TestCaseData("Flagged", JobScope.User, Role.Requester,
                new Dictionary<string, string> { [Target] = " " }, TargetRefusal);
        }
    }

    public static IEnumerable<TestCaseData> EventCreateAllowedCases
    {
        get
        {
            yield return new TestCaseData("Flagged", Role.Requester,
                new Dictionary<string, string> { [Target] = "00000000-0000-0000-0000-000000000001" });
            yield return new TestCaseData("FlaggedEventOnly", Role.Requester,
                new Dictionary<string, string> { [Target] = "00000000-0000-0000-0000-000000000001" });
            yield return new TestCaseData("Flagged", Role.Manager,
                new Dictionary<string, string> { [Target] = "${event.contentHierarchyItemId}" });
            yield return new TestCaseData("Flagged", Role.Manager, new Dictionary<string, string>());
            yield return new TestCaseData("Unflagged", Role.Manager,
                new Dictionary<string, string> { [Target] = "${event.contentHierarchyItemId}" });
        }
    }

    public static IEnumerable<TestCaseData> EventCreateRefusedCases
    {
        get
        {
            var literal = new Dictionary<string, string> { [Target] = "00000000-0000-0000-0000-000000000001" };
            yield return new TestCaseData("Unflagged", JobScope.User, Role.Requester, literal, RoleRefusal);
            yield return new TestCaseData("Flagged", JobScope.Administrative, Role.Requester, literal, RoleRefusal);
            yield return new TestCaseData("FlaggedScheduleOnly", JobScope.User, Role.Requester, literal,
                RoleRefusal);
            yield return new TestCaseData("Flagged", JobScope.System, Role.Requester, literal, RoleRefusal);
            yield return new TestCaseData("Flagged", JobScope.User, Role.Viewer, literal, RoleRefusal);
            yield return new TestCaseData("Flagged", JobScope.User, Role.Requester,
                new Dictionary<string, string> { [Target] = "${event.contentHierarchyItemId}" }, TargetRefusal);
            yield return new TestCaseData("Flagged", JobScope.User, Role.Requester,
                new Dictionary<string, string>(), TargetRefusal);
            yield return new TestCaseData("Flagged", JobScope.User, Role.Requester,
                new Dictionary<string, string>
                {
                    [Target] = "00000000-0000-0000-0000-000000000001",
                    ["note"] = "${event.name}"
                },
                TargetRefusal);
        }
    }

    public static IEnumerable<TestCaseData> PatchAllowedCases
    {
        get
        {
            yield return new TestCaseData(Role.Requester,
                new DictionaryPatch<string, string, string>
                {
                    Update = new Dictionary<string, string> { [Target] = "00000000-0000-0000-0000-000000000002" }
                },
                true);
            yield return new TestCaseData(Role.Requester, null, false);
            yield return new TestCaseData(Role.Requester, null, true);
            yield return new TestCaseData(Role.Manager,
                new DictionaryPatch<string, string, string>
                {
                    Update = new Dictionary<string, string> { [Target] = "${event.contentHierarchyItemId}" }
                },
                true);
            yield return new TestCaseData(Role.Admin,
                new DictionaryPatch<string, string, string>
                {
                    Update = new Dictionary<string, string> { [Target] = "${event.contentHierarchyItemId}" }
                },
                true);
        }
    }

    public static IEnumerable<TestCaseData> PatchRefusedCases
    {
        get
        {
            yield return new TestCaseData(CallerId,
                new DictionaryPatch<string, string, string>
                {
                    Update = new Dictionary<string, string> { [Target] = "${event.contentHierarchyItemId}" }
                },
                TargetRefusal);
            yield return new TestCaseData(CallerId,
                new DictionaryPatch<string, string, string>
                {
                    Update = new Dictionary<string, string> { [Target] = "" }
                },
                TargetRefusal);
            yield return new TestCaseData(CallerId,
                new DictionaryPatch<string, string, string> { Remove = [Target] },
                TargetRefusal);
            yield return new TestCaseData(OtherUserId,
                new DictionaryPatch<string, string, string>
                {
                    Update = new Dictionary<string, string> { [Target] = "00000000-0000-0000-0000-000000000002" }
                },
                "A non-admin caller can modify only its own user triggers.");
        }
    }

    [SetUp]
    public void SetUp()
    {
        JobParameter[] parameters =
        [
            new(Target, null, DataType.String, true, null),
            new("note", null, DataType.String, false, null)
        ];
        JobSpec[] specs =
        [
            new()
            {
                JobType = "Flagged", DisplayName = "Flagged", Category = "Test", Scope = JobScope.Content,
                SupportedTriggerModes = [JobTriggerMode.Schedule, JobTriggerMode.Event],
                RequesterTriggerModes = [JobTriggerMode.Schedule, JobTriggerMode.Event], Parameters = parameters
            },
            new()
            {
                JobType = "FlaggedScheduleOnly", DisplayName = "FlaggedScheduleOnly", Category = "Test",
                Scope = JobScope.Content, SupportedTriggerModes = [JobTriggerMode.Schedule, JobTriggerMode.Event],
                RequesterTriggerModes = [JobTriggerMode.Schedule], Parameters = parameters
            },
            new()
            {
                JobType = "FlaggedEventOnly", DisplayName = "FlaggedEventOnly", Category = "Test",
                Scope = JobScope.Content, SupportedTriggerModes = [JobTriggerMode.Schedule, JobTriggerMode.Event],
                RequesterTriggerModes = [JobTriggerMode.Event], Parameters = parameters
            },
            new()
            {
                JobType = "Unflagged", DisplayName = "Unflagged", Category = "Test", Scope = JobScope.Content,
                SupportedTriggerModes = [JobTriggerMode.Schedule, JobTriggerMode.Event], Parameters = parameters
            },
            new()
            {
                JobType = "Administrative", DisplayName = "Administrative", Category = "Test",
                Scope = JobScope.Administrative,
                SupportedTriggerModes = [JobTriggerMode.Schedule, JobTriggerMode.Event], Parameters = parameters
            },
            new()
            {
                JobType = "Builtin", DisplayName = "Builtin", Category = "Test", Scope = JobScope.System,
                SupportedTriggerModes = [JobTriggerMode.Schedule, JobTriggerMode.Event], Parameters = parameters
            }
        ];

        _scheduleTriggers = new Mock<IScheduleTriggerRepository>();
        _scheduleTriggers
            .Setup(repository => repository.CreateAsync(
                It.IsAny<ScheduleTrigger>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ScheduleTrigger trigger, Guid? _, CancellationToken _) => trigger);
        _scheduleTriggers
            .Setup(repository => repository.PatchAsync(
                It.IsAny<ScheduleTriggerPatch>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _eventTriggers = new Mock<IEventTriggerRepository>();
        _eventTriggers
            .Setup(repository => repository.CreateAsync(
                It.IsAny<EventTrigger>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((EventTrigger trigger, Guid? _, CancellationToken _) => trigger);
        _eventTriggers
            .Setup(repository => repository.PatchAsync(
                It.IsAny<EventTriggerPatch>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var transactions = new Mock<ITransactionManager>();
        transactions.Setup(manager => manager.ExecuteWithTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<bool>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<bool>> operation, CancellationToken token) => operation(token));

        _subject = new JobManager(
            Mock.Of<IJobRuntime>(),
            Mock.Of<IJobRegistry>(registry => registry.ListJobSpecs() == specs),
            Mock.Of<IJobExecutionRepository>(),
            _scheduleTriggers.Object,
            _eventTriggers.Object,
            Mock.Of<IJobConcurrencyScheduler>(),
            Mock.Of<IJobExecutionScopeFactory>(),
            TimeProvider.System,
            NullLogger<JobManager>.Instance,
            transactions.Object);
    }

    [TestCaseSource(nameof(ScheduleCreateAllowedCases))]
    public async Task CreateScheduleTriggerAsync_CreatesTrigger_WhenRoleMayCreate(
        string jobType,
        JobScope scope,
        Role role)
    {
        // Arrange
        var create = new ScheduleTriggerCreate
        {
            JobType = jobType,
            Scope = scope,
            Name = "Personal",
            Schedule = "0 0 1 * *",
            ExecutionMode = JobExecutionMode.Queue,
            Enabled = false,
            Parameters = new Dictionary<string, string> { [Target] = "00000000-0000-0000-0000-000000000001" }
        };

        // Act
        var trigger = await _subject.CreateScheduleTriggerAsync(create, CallerId, role).ConfigureAwait(false);

        // Assert
        Assert.That(trigger.CreatedByUserId, Is.EqualTo(CallerId));
    }

    [TestCaseSource(nameof(ScheduleCreateRefusedCases))]
    public void CreateScheduleTriggerAsync_Throws_WhenRoleMayNotCreate(
        string jobType,
        JobScope scope,
        Role role,
        Dictionary<string, string> parameters,
        string expectedMessage)
    {
        // Arrange
        var create = new ScheduleTriggerCreate
        {
            JobType = jobType,
            Scope = scope,
            Name = "Personal",
            Schedule = "0 0 1 * *",
            ExecutionMode = JobExecutionMode.Queue,
            Enabled = false,
            Parameters = parameters
        };

        // Act
        var exception = Assert.ThrowsAsync<MagpieException>(async () =>
            await _subject.CreateScheduleTriggerAsync(create, CallerId, role).ConfigureAwait(false));

        // Assert
        Assert.That(exception!.Message, Is.EqualTo(expectedMessage));
        _scheduleTriggers.Verify(
            repository => repository.CreateAsync(
                It.IsAny<ScheduleTrigger>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [TestCaseSource(nameof(EventCreateAllowedCases))]
    public async Task CreateEventTriggerAsync_CreatesTrigger_WhenRoleMayCreate(
        string jobType,
        Role role,
        Dictionary<string, string> parameters)
    {
        // Arrange
        var create = new EventTriggerCreate
        {
            JobType = jobType,
            Scope = JobScope.User,
            Name = "Personal",
            EventFilter = new EventFilter(),
            Enabled = false,
            Parameters = parameters
        };

        // Act
        var trigger = await _subject.CreateEventTriggerAsync(create, CallerId, role).ConfigureAwait(false);

        // Assert
        Assert.That(trigger.CreatedByUserId, Is.EqualTo(CallerId));
    }

    [TestCaseSource(nameof(EventCreateRefusedCases))]
    public void CreateEventTriggerAsync_Throws_WhenRoleMayNotCreate(
        string jobType,
        JobScope scope,
        Role role,
        Dictionary<string, string> parameters,
        string expectedMessage)
    {
        // Arrange
        var create = new EventTriggerCreate
        {
            JobType = jobType,
            Scope = scope,
            Name = "Personal",
            EventFilter = new EventFilter(),
            Enabled = false,
            Parameters = parameters
        };

        // Act
        var exception = Assert.ThrowsAsync<MagpieException>(async () =>
            await _subject.CreateEventTriggerAsync(create, CallerId, role).ConfigureAwait(false));

        // Assert
        Assert.That(exception!.Message, Is.EqualTo(expectedMessage));
        _eventTriggers.Verify(
            repository => repository.CreateAsync(
                It.IsAny<EventTrigger>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [TestCaseSource(nameof(PatchAllowedCases))]
    public async Task PatchScheduleTriggerAsync_PatchesTrigger_WhenOwnerKeepsLiteralTarget(
        Role role,
        DictionaryPatch<string, string, string>? parameters,
        bool enabled)
    {
        // Arrange
        _scheduleTriggers
            .Setup(repository => repository.GetByIdAsync(
                new Guid("00000000-0000-0000-0000-0000000000f1"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScheduleTrigger
            {
                Id = new Guid("00000000-0000-0000-0000-0000000000f1"),
                JobType = "Flagged",
                Scope = JobScope.User,
                Name = "Personal",
                Schedule = "0 0 1 * *",
                ExecutionMode = JobExecutionMode.Queue,
                Enabled = true,
                Parameters = new Dictionary<string, string> { [Target] = "00000000-0000-0000-0000-000000000001" },
                CreatedByUserId = CallerId
            });
        var patch = new ScheduleTriggerPatch { Parameters = parameters, Enabled = new() { Value = enabled } };

        // Act
        var patched = await _subject.PatchScheduleTriggerAsync(
            new Guid("00000000-0000-0000-0000-0000000000f1"), patch, CallerId, role).ConfigureAwait(false);

        // Assert
        Assert.That(patched, Is.True);
    }

    [TestCaseSource(nameof(PatchRefusedCases))]
    public void PatchScheduleTriggerAsync_Throws_WhenRequesterLeavesLiteralTarget(
        Guid ownerId,
        DictionaryPatch<string, string, string> parameters,
        string expectedMessage)
    {
        // Arrange
        _scheduleTriggers
            .Setup(repository => repository.GetByIdAsync(
                new Guid("00000000-0000-0000-0000-0000000000f1"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScheduleTrigger
            {
                Id = new Guid("00000000-0000-0000-0000-0000000000f1"),
                JobType = "Flagged",
                Scope = JobScope.User,
                Name = "Personal",
                Schedule = "0 0 1 * *",
                ExecutionMode = JobExecutionMode.Queue,
                Enabled = false,
                Parameters = new Dictionary<string, string> { [Target] = "00000000-0000-0000-0000-000000000001" },
                CreatedByUserId = ownerId
            });
        var patch = new ScheduleTriggerPatch { Parameters = parameters };

        // Act
        var exception = Assert.ThrowsAsync<MagpieException>(async () =>
            await _subject.PatchScheduleTriggerAsync(
                new Guid("00000000-0000-0000-0000-0000000000f1"), patch, CallerId, Role.Requester)
                .ConfigureAwait(false));

        // Assert
        Assert.That(exception!.Message, Is.EqualTo(expectedMessage));
        _scheduleTriggers.Verify(
            repository => repository.PatchAsync(
                It.IsAny<ScheduleTriggerPatch>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [TestCase(Role.Requester, "00000000-0000-0000-0000-000000000002")]
    [TestCase(Role.Manager, "${event.contentHierarchyItemId}")]
    [TestCase(Role.Manager, "")]
    public async Task PatchEventTriggerAsync_PatchesTrigger_WhenRoleMayUseTarget(Role role, string target)
    {
        // Arrange
        _eventTriggers
            .Setup(repository => repository.GetByIdAsync(
                new Guid("00000000-0000-0000-0000-0000000000e1"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EventTrigger
            {
                Id = new Guid("00000000-0000-0000-0000-0000000000e1"),
                JobType = "Flagged",
                Scope = JobScope.User,
                Name = "Personal",
                EventFilter = new EventFilter(),
                Enabled = true,
                Parameters = new Dictionary<string, string> { [Target] = "00000000-0000-0000-0000-000000000001" },
                CreatedByUserId = CallerId
            });
        var patch = new EventTriggerPatch
        {
            Parameters = new DictionaryPatch<string, string, string>
            {
                Update = new Dictionary<string, string> { [Target] = target }
            }
        };

        // Act
        var patched = await _subject.PatchEventTriggerAsync(
            new Guid("00000000-0000-0000-0000-0000000000e1"), patch, CallerId, role).ConfigureAwait(false);

        // Assert
        Assert.That(patched, Is.True);
    }

    [TestCaseSource(nameof(PatchRefusedCases))]
    public void PatchEventTriggerAsync_Throws_WhenRequesterLeavesLiteralTarget(
        Guid ownerId,
        DictionaryPatch<string, string, string> parameters,
        string expectedMessage)
    {
        // Arrange
        _eventTriggers
            .Setup(repository => repository.GetByIdAsync(
                new Guid("00000000-0000-0000-0000-0000000000e1"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EventTrigger
            {
                Id = new Guid("00000000-0000-0000-0000-0000000000e1"),
                JobType = "Flagged",
                Scope = JobScope.User,
                Name = "Personal",
                EventFilter = new EventFilter(),
                Enabled = false,
                Parameters = new Dictionary<string, string> { [Target] = "00000000-0000-0000-0000-000000000001" },
                CreatedByUserId = ownerId
            });
        var patch = new EventTriggerPatch { Parameters = parameters };

        // Act
        var exception = Assert.ThrowsAsync<MagpieException>(async () =>
            await _subject.PatchEventTriggerAsync(
                new Guid("00000000-0000-0000-0000-0000000000e1"), patch, CallerId, Role.Requester)
                .ConfigureAwait(false));

        // Assert
        Assert.That(exception!.Message, Is.EqualTo(expectedMessage));
        _eventTriggers.Verify(
            repository => repository.PatchAsync(
                It.IsAny<EventTriggerPatch>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [TestCase("${event.contentHierarchyItemId}")]
    [TestCase("")]
    public void PatchScheduleTriggerAsync_Throws_WhenRequesterEnablesUntargetedTrigger(string storedTarget)
    {
        // Arrange
        _scheduleTriggers
            .Setup(repository => repository.GetByIdAsync(
                new Guid("00000000-0000-0000-0000-0000000000f1"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScheduleTrigger
            {
                Id = new Guid("00000000-0000-0000-0000-0000000000f1"),
                JobType = "Flagged",
                Scope = JobScope.User,
                Name = "Personal",
                Schedule = "0 0 1 * *",
                ExecutionMode = JobExecutionMode.Queue,
                Enabled = false,
                Parameters = new Dictionary<string, string> { [Target] = storedTarget },
                CreatedByUserId = CallerId
            });
        var patch = new ScheduleTriggerPatch { Enabled = new() { Value = true } };

        // Act
        var exception = Assert.ThrowsAsync<MagpieException>(async () =>
            await _subject.PatchScheduleTriggerAsync(
                new Guid("00000000-0000-0000-0000-0000000000f1"), patch, CallerId, Role.Requester)
                .ConfigureAwait(false));

        // Assert
        Assert.That(exception!.Message, Is.EqualTo(TargetRefusal));
        _scheduleTriggers.Verify(
            repository => repository.PatchAsync(
                It.IsAny<ScheduleTriggerPatch>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [TestCase("${event.contentHierarchyItemId}")]
    [TestCase("")]
    public async Task PatchScheduleTriggerAsync_PatchesTrigger_WhenRequesterDisablesUntargetedTrigger(
        string storedTarget)
    {
        // Arrange
        _scheduleTriggers
            .Setup(repository => repository.GetByIdAsync(
                new Guid("00000000-0000-0000-0000-0000000000f1"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScheduleTrigger
            {
                Id = new Guid("00000000-0000-0000-0000-0000000000f1"),
                JobType = "Flagged",
                Scope = JobScope.User,
                Name = "Personal",
                Schedule = "0 0 1 * *",
                ExecutionMode = JobExecutionMode.Queue,
                Enabled = true,
                Parameters = new Dictionary<string, string> { [Target] = storedTarget },
                CreatedByUserId = CallerId
            });
        var patch = new ScheduleTriggerPatch { Enabled = new() { Value = false } };

        // Act
        var patched = await _subject.PatchScheduleTriggerAsync(
            new Guid("00000000-0000-0000-0000-0000000000f1"), patch, CallerId, Role.Requester).ConfigureAwait(false);

        // Assert
        Assert.That(patched, Is.True);
    }

    [TestCase("${event.contentHierarchyItemId}")]
    [TestCase("")]
    public void PatchEventTriggerAsync_Throws_WhenRequesterEnablesUntargetedTrigger(string storedTarget)
    {
        // Arrange
        _eventTriggers
            .Setup(repository => repository.GetByIdAsync(
                new Guid("00000000-0000-0000-0000-0000000000e1"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EventTrigger
            {
                Id = new Guid("00000000-0000-0000-0000-0000000000e1"),
                JobType = "Flagged",
                Scope = JobScope.User,
                Name = "Personal",
                EventFilter = new EventFilter(),
                Enabled = false,
                Parameters = new Dictionary<string, string> { [Target] = storedTarget },
                CreatedByUserId = CallerId
            });
        var patch = new EventTriggerPatch { Enabled = new() { Value = true } };

        // Act
        var exception = Assert.ThrowsAsync<MagpieException>(async () =>
            await _subject.PatchEventTriggerAsync(
                new Guid("00000000-0000-0000-0000-0000000000e1"), patch, CallerId, Role.Requester)
                .ConfigureAwait(false));

        // Assert
        Assert.That(exception!.Message, Is.EqualTo(TargetRefusal));
        _eventTriggers.Verify(
            repository => repository.PatchAsync(
                It.IsAny<EventTriggerPatch>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [TestCase("${event.contentHierarchyItemId}")]
    [TestCase("")]
    public async Task PatchEventTriggerAsync_PatchesTrigger_WhenRequesterDisablesUntargetedTrigger(
        string storedTarget)
    {
        // Arrange
        _eventTriggers
            .Setup(repository => repository.GetByIdAsync(
                new Guid("00000000-0000-0000-0000-0000000000e1"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EventTrigger
            {
                Id = new Guid("00000000-0000-0000-0000-0000000000e1"),
                JobType = "Flagged",
                Scope = JobScope.User,
                Name = "Personal",
                EventFilter = new EventFilter(),
                Enabled = true,
                Parameters = new Dictionary<string, string> { [Target] = storedTarget },
                CreatedByUserId = CallerId
            });
        var patch = new EventTriggerPatch { Enabled = new() { Value = false } };

        // Act
        var patched = await _subject.PatchEventTriggerAsync(
            new Guid("00000000-0000-0000-0000-0000000000e1"), patch, CallerId, Role.Requester).ConfigureAwait(false);

        // Assert
        Assert.That(patched, Is.True);
    }

    [Test]
    public async Task DeleteScheduleTriggerAsync_DeletesTrigger_WhenRequesterOwnsUserTrigger()
    {
        // Arrange
        _scheduleTriggers
            .Setup(repository => repository.GetByIdAsync(
                new Guid("00000000-0000-0000-0000-0000000000f1"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScheduleTrigger
            {
                Id = new Guid("00000000-0000-0000-0000-0000000000f1"),
                JobType = "Flagged",
                Scope = JobScope.User,
                Name = "Personal",
                Schedule = "0 0 1 * *",
                ExecutionMode = JobExecutionMode.Queue,
                Parameters = new Dictionary<string, string> { [Target] = "${event.contentHierarchyItemId}" },
                CreatedByUserId = CallerId
            });
        _scheduleTriggers
            .Setup(repository => repository.DeleteAsync(
                new Guid("00000000-0000-0000-0000-0000000000f1"), CallerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var deleted = await _subject.DeleteScheduleTriggerAsync(
            new Guid("00000000-0000-0000-0000-0000000000f1"),
            CallerId,
            Role.Requester).ConfigureAwait(false);

        // Assert
        Assert.That(deleted, Is.True);
    }
}
