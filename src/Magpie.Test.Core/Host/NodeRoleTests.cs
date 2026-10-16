using System.Linq;

using Magpie.Core.Jobs.Runtime;

using Microsoft.Extensions.DependencyInjection;

using NUnit.Framework;

namespace Magpie.Test.Core.Host;

[TestFixture]
public sealed class NodeRoleTests
{
    [TestCase(true, 1)]
    [TestCase(false, 0)]
    public void AddMagpieCore_RegistersTheJobRuntimeOnlyOnAJobRunner_WhenTheHostStarts(
        bool jobRunner,
        int expectedRegistrations)
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddMagpieCore(true, jobRunner);

        // Assert
        Assert.That(
            services.Count(descriptor => descriptor.ImplementationType == typeof(JobRuntimeBackgroundService)),
            Is.EqualTo(expectedRegistrations));
    }
}
