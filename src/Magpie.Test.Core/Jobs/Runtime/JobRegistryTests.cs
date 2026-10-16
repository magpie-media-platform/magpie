using System;

using Magpie.Core.Config;
using Magpie.Core.Jobs.Fulfillment;
using Magpie.Core.Jobs.Metadata;
using Magpie.Core.Jobs.Runtime;
using Magpie.Models;
using Magpie.Models.Enums;
using Magpie.Models.Jobs;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using NUnit.Framework;

namespace Magpie.Test.Core.Jobs.Runtime;

[TestFixture]
public sealed class JobRegistryTests
{
    private static readonly Type TestJobType = typeof(ContentMetadataRefreshItemJob);
    private static readonly Type OverrideJobType = typeof(ReleaseSearchItemJob);

    [Test]
    public void Registry_RoundTripsTypesToSpecsAndHandlers_WhenJobsAreRegistered()
    {
        // Arrange
        var logger = NullLoggerFactory.Instance.CreateLogger<JobRegistry>();
        var firstSpec = new JobSpec
        {
            JobType = "magpie.test.first",
            DisplayName = "magpie.test.first",
            Category = "Test",
            Scope = JobScope.System,
            OnDemand = true,
        };
        var secondSpec = new JobSpec
        {
            JobType = "magpie.test.second",
            DisplayName = "magpie.test.second",
            Category = "Test",
            Scope = JobScope.System,
            OnDemand = true,
        };

        // Act
        var registry = new JobRegistry(
            [new(firstSpec, TestJobType), new(secondSpec, TestJobType)],
            new JobRuntimeOptions(),
            logger);
        var foundFirst = registry.TryGetJobSpec("magpie.test.first", out var resolvedFirst);
        var foundSecond = registry.TryGetJobSpec("magpie.test.second", out var resolvedSecond);
        var foundHandler = registry.TryGetHandlerType(firstSpec, out var handlerType);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(foundFirst, Is.True);
            Assert.That(resolvedFirst, Is.SameAs(firstSpec));
            Assert.That(foundSecond, Is.True);
            Assert.That(resolvedSecond, Is.SameAs(secondSpec));
            Assert.That(foundHandler, Is.True);
            Assert.That(handlerType, Is.EqualTo(TestJobType));
            Assert.That(registry.ListJobSpecs(), Is.EqualTo([firstSpec, secondSpec]));
            Assert.That(registry.TryGetJobSpec("magpie.test.unknown", out _), Is.False);
            Assert.That(registry.TryGetJobSpec(string.Empty, out _), Is.False);
        }
    }

    [Test]
    public void Registry_UsesLastRegistration_WhenJobTypeIsRegisteredTwice()
    {
        // Arrange
        var logger = NullLoggerFactory.Instance.CreateLogger<JobRegistry>();
        var firstSpec = new JobSpec
        {
            JobType = "magpie.test.first",
            DisplayName = "first",
            Category = "Test",
            Scope = JobScope.System,
            OnDemand = true,
        };
        var overrideSpec = firstSpec with { DisplayName = "override" };

        // Act
        var registry = new JobRegistry(
            [new(firstSpec, TestJobType), new(overrideSpec, OverrideJobType)],
            new JobRuntimeOptions(),
            logger);
        var foundSpec = registry.TryGetJobSpec("magpie.test.first", out var spec);
        var foundHandler = registry.TryGetHandlerType(overrideSpec, out var handlerType);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(foundSpec, Is.True);
            Assert.That(spec, Is.SameAs(overrideSpec));
            Assert.That(foundHandler, Is.True);
            Assert.That(handlerType, Is.EqualTo(OverrideJobType));
            Assert.That(registry.ListJobSpecs(), Is.EqualTo([overrideSpec]));
        }
    }

    [TestCase(false, false)]
    [TestCase(true, true)]
    public void TryGetJobSpec_ReturnsTheNodeMax_WhenTheNodeOverridesTheConcurrencyLimit(
        bool declaredPerNode,
        bool expectedPerNode)
    {
        // Arrange
        var spec = new JobSpec
        {
            JobType = "magpie.test.limited",
            DisplayName = "limited",
            Category = "Test",
            Scope = JobScope.System,
            SupportsImmediate = false,
            ConcurrencyLimit = new() { Max = 1, PerNode = declaredPerNode },
        };
        var options = new JobRuntimeOptions { ConcurrencyLimits = { ["magpie.test.limited"] = 3 } };

        // Act
        var registry = new JobRegistry([new(spec, TestJobType)], options, NullLogger<JobRegistry>.Instance);
        registry.TryGetJobSpec("magpie.test.limited", out var resolved);

        // Assert
        Assert.That(
            resolved!.ConcurrencyLimit,
            Is.EqualTo(new ConcurrencyLimit { Max = 3, PerNode = expectedPerNode }));
    }

    [Test]
    public void TryGetJobSpec_ReturnsNoLimit_WhenTheNodeOverridesAJobTypeWithoutALimit()
    {
        // Arrange
        var spec = new JobSpec
        {
            JobType = "magpie.test.unlimited",
            DisplayName = "unlimited",
            Category = "Test",
            Scope = JobScope.System,
        };
        var options = new JobRuntimeOptions { ConcurrencyLimits = { ["magpie.test.unlimited"] = 2 } };

        // Act
        var registry = new JobRegistry([new(spec, TestJobType)], options, NullLogger<JobRegistry>.Instance);
        registry.TryGetJobSpec("magpie.test.unlimited", out var resolved);

        // Assert
        Assert.That(resolved!.ConcurrencyLimit, Is.Null);
    }

    [TestCase(0, false, "The concurrency limit of job type magpie.test.limited is less than 1.")]
    [TestCase(1, true, "Job type magpie.test.limited has a concurrency limit and supports immediate runs.")]
    public void Constructor_Throws_WhenTheDeclaredConcurrencyLimitIsNotValid(
        int max,
        bool supportsImmediate,
        string expectedMessage)
    {
        // Arrange
        var spec = new JobSpec
        {
            JobType = "magpie.test.limited",
            DisplayName = "limited",
            Category = "Test",
            Scope = JobScope.System,
            SupportsImmediate = supportsImmediate,
            ConcurrencyLimit = new() { Max = max, PerNode = true },
        };

        // Act
        var failure = Assert.Throws<InvalidOperationException>(
            () => _ = new JobRegistry([new(spec, TestJobType)], new(), NullLogger<JobRegistry>.Instance));

        // Assert
        Assert.That(failure!.Message, Is.EqualTo(expectedMessage));
    }

    [Test]
    public void Constructor_Throws_WhenTheNodeMaxIsLessThanOne()
    {
        // Arrange
        var spec = new JobSpec
        {
            JobType = "magpie.test.limited",
            DisplayName = "limited",
            Category = "Test",
            Scope = JobScope.System,
            SupportsImmediate = false,
            ConcurrencyLimit = new() { Max = 1, PerNode = true },
        };
        var options = new JobRuntimeOptions { ConcurrencyLimits = { ["magpie.test.limited"] = 0 } };

        // Act
        var failure = Assert.Throws<InvalidOperationException>(
            () => _ = new JobRegistry([new(spec, TestJobType)], options, NullLogger<JobRegistry>.Instance));

        // Assert
        Assert.That(
            failure!.Message,
            Is.EqualTo("The concurrency limit of job type magpie.test.limited is less than 1."));
    }
}
