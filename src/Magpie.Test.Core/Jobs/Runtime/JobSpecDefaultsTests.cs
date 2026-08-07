using Magpie.Models;
using Magpie.Models.Enums;

using NUnit.Framework;
namespace Magpie.Test.Core.Jobs.Runtime;

[TestFixture]
public class JobSpecDefaultsTests
{
    [Test]
    public void ConcurrencyDefaults_ArePerTypeWithEmptyPartitionFields_WhenSpecIsConstructed()
    {
        // Arrange
        // Act
        var spec = new JobSpec
        {
            JobType = "magpie.test.defaults",
            DisplayName = "Defaults job",
            Category = "Administration",
            Scope = JobScope.Administrative,
        };

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(spec.ConcurrencyScope, Is.EqualTo(ConcurrencyScope.PerType));
            Assert.That(spec.LockPartitionFields, Is.Empty);
        }
    }
}
