using Magpie.Models.Paging;

using NUnit.Framework;

namespace Magpie.Test.Core;

[TestFixture]
public sealed class PagingParameterTests
{
    [TestCase(0)]
    [TestCase(-1)]
    public void Initialization_NormalizesValues_WhenNonPositive(int value)
    {
        // Arrange

        // Act
        var subject = new PagingParameter { PageNumber = value, PageSize = value };

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(subject.PageNumber, Is.EqualTo(1));
            Assert.That(subject.PageSize, Is.EqualTo(1));
        }
    }

    [Test]
    public void Initialization_PreservesPageSize_WhenAbovePreviousLimit()
    {
        // Arrange

        // Act
        var subject = new PagingParameter { PageNumber = 1, PageSize = 1001 };

        // Assert
        Assert.That(subject.PageSize, Is.EqualTo(1001));
    }
}
