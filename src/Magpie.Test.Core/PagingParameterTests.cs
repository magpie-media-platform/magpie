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
        // Act
        var subject = new PagingParameter { PageNumber = value, PageSize = value };

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(subject.PageNumber, Is.EqualTo(1));
            Assert.That(subject.PageSize, Is.EqualTo(1));
        }
    }

    [TestCase(1001)]
    [TestCase(100000000)]
    [TestCase(int.MaxValue)]
    public void Initialization_ClampsPageSize_WhenAboveLimit(int pageSize)
    {
        // Act
        var subject = new PagingParameter { PageNumber = 1, PageSize = pageSize };

        // Assert
        Assert.That(subject.PageSize, Is.EqualTo(1000));
    }
}
