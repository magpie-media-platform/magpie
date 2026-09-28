using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Jobs;
using Magpie.Abstractions.Repositories;
using Magpie.Core.Events;
using Magpie.Core.Jobs;
using Magpie.Core.Jobs.Downloads;
using Magpie.Core.Jobs.Fulfillment;
using Magpie.Core.Jobs.Metadata;
using Magpie.Models;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Filtering;
using Magpie.Models.Jobs;
using Magpie.Models.Paging;
using Magpie.Sdk.Events;
using Magpie.Sdk.Notifications;
using Magpie.Sdk.Services;
using Magpie.Test.Core.Jobs.Runtime;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using Moq;

using NUnit.Framework;

using EventCatalog = Magpie.Sdk.Events.MagpieEvents;

namespace Magpie.Test.Core.Events;

[TestFixture]
public sealed class EventDispatchBackgroundServiceTests
{
    [Test]
    public void EventTriggers_PartitionTheAutoGrabPerContentItem_WhenCatalogInspected()
    {
        // Act
        var trigger = DefaultJobTriggerCatalog.EventTriggers
            .Single(candidate => candidate.JobType == "ReleaseAutoGrabItem");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ReleaseAutoGrabItemJob.Spec.ConcurrencyScope, Is.EqualTo(ConcurrencyScope.PerTypeAndTarget));
            Assert.That(ReleaseAutoGrabItemJob.Spec.LockPartitionFields, Is.EqualTo(["contentItemId"]));
            Assert.That(trigger.Parameters, Does.ContainKey("contentItemId"));
        }
    }

    [Test]
    public void EventTriggers_BindNoJob_WhenTheEventIsReleaseScopeMatched()
    {
        // Act
        var triggers = DefaultJobTriggerCatalog.EventTriggers
            .Where(candidate => candidate.EventFilter.EventName?.Value == "release.scopeMatched");

        // Assert
        Assert.That(
            triggers,
            Is.Empty,
            "The additional scopes of a pack rely on the auto-grab scan schedule.");
    }
}
