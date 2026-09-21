using System;

using Magpie.Core.Jobs.Fulfillment;
using Magpie.Core.Jobs.Metadata;
using Magpie.Core.Jobs.Runtime;
using Magpie.Models;
using Magpie.Models.Enums;

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
        var registry = new JobRegistry([new(firstSpec, TestJobType), new(secondSpec, TestJobType)], logger);
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
        var registry = new JobRegistry([new(firstSpec, TestJobType), new(overrideSpec, OverrideJobType)], logger);
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
}
