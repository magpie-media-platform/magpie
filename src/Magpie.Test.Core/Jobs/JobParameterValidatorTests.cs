using System;
using System.Collections.Generic;

using Magpie.Abstractions.Jobs;
using Magpie.Core.Jobs;
using Magpie.Models;
using Magpie.Models.Enums;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Jobs;

[TestFixture]
public class JobParameterValidatorTests
{
    [Test]
    public void Validate_ReturnsEmptyDictionary_WhenNoParametersDefined()
    {
        // Arrange
        var job = new Mock<IJob>();
        job.SetupGet(j => j.Parameters).Returns([]);

        // Act
        var result = JobParameterValidator.TryValidate(job.Object, new Dictionary<string, string>());

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void Validate_ThrowsArgumentException_ForMissingRequiredParameter()
    {
        // Arrange
        var job = new Mock<IJob>();
        job.SetupGet(j => j.Parameters).Returns([
            new JobParameter("libraryId", "The library ID", DataType.String, true, null)
        ]);

        // Act
        void Act() => JobParameterValidator.TryValidate(job.Object, new Dictionary<string, string>());

        // Assert
        using (Assert.EnterMultipleScope())
        {
            var ex = Assert.Throws<ArgumentException>(Act);
            Assert.That(ex, Has.Property(nameof(ex.Message)).Contain("libraryId"));
        }
    }

    [Test]
    public void Validate_AppliesDefaultValue_WhenOptionalParameterMissing()
    {
        // Arrange
        var job = new Mock<IJob>();
        job.SetupGet(j => j.Parameters).Returns([
            new JobParameter("deep", "Deep scan", DataType.Boolean, false, "false")
        ]);

        // Act
        var result = JobParameterValidator.TryValidate(job.Object, new Dictionary<string, string>());

        // Assert
        Assert.That(result, Is.Not.Null.And.Count.EqualTo(1).And.ContainKey("deep").WithValue("false"));
    }

    [Test]
    public void Validate_OmitsOptionalParameterWithNoDefault()
    {
        // Arrange
        var job = new Mock<IJob>();
        job.SetupGet(j => j.Parameters).Returns([
            new JobParameter("filter", "Optional filter", DataType.String, false, null)
        ]);

        // Act
        var result = JobParameterValidator.TryValidate(job.Object, new Dictionary<string, string>());

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void Validate_AcceptsValidStringParameter()
    {
        // Arrange
        var job = new Mock<IJob>();
        job.SetupGet(j => j.Parameters).Returns([
            new JobParameter("name", "The name", DataType.String, true, null)
        ]);
        var parameters = new Dictionary<string, string> { ["name"] = "test-value" };

        // Act
        var result = JobParameterValidator.TryValidate(job.Object, parameters);

        // Assert
        Assert.That(result, Contains.Key("name").WithValue("test-value"));
    }

    [Test]
    public void Validate_AcceptsValidNumberParameter()
    {
        // Arrange
        var job = new Mock<IJob>();
        job.SetupGet(j => j.Parameters).Returns([
            new JobParameter("count", "The count", DataType.Number, true, null)
        ]);
        var parameters = new Dictionary<string, string> { ["count"] = "42" };

        // Act
        var result = JobParameterValidator.TryValidate(job.Object, parameters);

        // Assert
        Assert.That(result, Contains.Key("count").WithValue("42"));
    }

    [Test]
    public void Validate_AcceptsNegativeNumberParameter()
    {
        // Arrange
        var job = new Mock<IJob>();
        job.SetupGet(j => j.Parameters).Returns([
            new JobParameter("offset", "The offset", DataType.Number, true, null)
        ]);
        var parameters = new Dictionary<string, string> { ["offset"] = "-100" };

        // Act
        var result = JobParameterValidator.TryValidate(job.Object, parameters);

        // Assert
        Assert.That(result, Contains.Key("offset").WithValue("-100"));
    }

    [Test]
    public void Validate_AcceptsDecimalNumber()
    {
        // Arrange
        var job = new Mock<IJob>();
        job.SetupGet(j => j.Parameters).Returns([
            new JobParameter("rate", "The rate", DataType.Number, true, null)
        ]);
        var parameters = new Dictionary<string, string> { ["rate"] = "3.14" };

        // Act
        var result = JobParameterValidator.TryValidate(job.Object, parameters);

        // Assert
        Assert.That(result, Contains.Key("rate").WithValue("3.14"));
    }

    [Test]
    public void Validate_ThrowsArgumentException_ForInvalidNumberParameter()
    {
        // Arrange
        var job = new Mock<IJob>();
        job.SetupGet(j => j.Parameters).Returns([
            new JobParameter("count", "The count", DataType.Number, true, null)
        ]);
        var parameters = new Dictionary<string, string> { ["count"] = "not-a-number" };

        // Act
        void Act() => JobParameterValidator.TryValidate(job.Object, parameters);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            var ex = Assert.Throws<ArgumentException>(Act);
            Assert.That(ex, Has.Property(nameof(ex.Message)).Contain("count"));
            Assert.That(ex, Has.Property(nameof(ex.Message)).Contain("number"));
        }
    }

    [Test]
    public void Validate_AcceptsValidBooleanParameter_True()
    {
        // Arrange
        var job = new Mock<IJob>();
        job.SetupGet(j => j.Parameters).Returns([
            new JobParameter("enabled", "Is enabled", DataType.Boolean, true, null)
        ]);
        var parameters = new Dictionary<string, string> { ["enabled"] = "true" };

        // Act
        var result = JobParameterValidator.TryValidate(job.Object, parameters);

        // Assert
        Assert.That(result, Contains.Key("enabled").WithValue("true"));
    }

    [Test]
    public void Validate_AcceptsValidBooleanParameter_False()
    {
        // Arrange
        var job = new Mock<IJob>();
        job.SetupGet(j => j.Parameters).Returns([
            new JobParameter("enabled", "Is enabled", DataType.Boolean, true, null)
        ]);
        var parameters = new Dictionary<string, string> { ["enabled"] = "false" };

        // Act
        var result = JobParameterValidator.TryValidate(job.Object, parameters);

        // Assert
        Assert.That(result, Contains.Key("enabled").WithValue("false"));
    }

    [Test]
    public void Validate_NormalizesBooleanParameter_CaseInsensitive()
    {
        // Arrange
        var job = new Mock<IJob>();
        job.SetupGet(j => j.Parameters).Returns([
            new JobParameter("enabled", "Is enabled", DataType.Boolean, true, null)
        ]);
        var parameters = new Dictionary<string, string> { ["enabled"] = "TRUE" };

        // Act
        var result = JobParameterValidator.TryValidate(job.Object, parameters);

        // Assert
        Assert.That(result, Contains.Key("enabled").WithValue("true"));
    }

    [Test]
    public void Validate_ThrowsArgumentException_ForInvalidBooleanParameter()
    {
        // Arrange
        var job = new Mock<IJob>();
        job.SetupGet(j => j.Parameters).Returns([
            new JobParameter("enabled", "Is enabled", DataType.Boolean, true, null)
        ]);
        var parameters = new Dictionary<string, string> { ["enabled"] = "yes" };

        // Act
        void Act() => JobParameterValidator.TryValidate(job.Object, parameters);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            var ex = Assert.Throws<ArgumentException>(Act);
            Assert.That(ex, Has.Property(nameof(ex.Message)).Contain("enabled"));
            Assert.That(ex, Has.Property(nameof(ex.Message)).Contain("boolean"));
        }
    }

    [Test]
    public void Validate_AcceptsValidDateParameter()
    {
        // Arrange
        var job = new Mock<IJob>();
        job.SetupGet(j => j.Parameters).Returns([
            new JobParameter("startDate", "The start date", DataType.Date, true, null)
        ]);
        var parameters = new Dictionary<string, string> { ["startDate"] = "2024-01-15T10:30:00Z" };

        // Act
        var result = JobParameterValidator.TryValidate(job.Object, parameters);

        // Assert
        Assert.That(result, Contains.Key("startDate").WithValue("2024-01-15T10:30:00Z"));
    }

    [Test]
    public void Validate_ThrowsArgumentException_ForInvalidDateParameter()
    {
        // Arrange
        var job = new Mock<IJob>();
        job.SetupGet(j => j.Parameters).Returns([
            new JobParameter("startDate", "The start date", DataType.Date, true, null)
        ]);
        var parameters = new Dictionary<string, string> { ["startDate"] = "not-a-date" };

        // Act
        void Act() => JobParameterValidator.TryValidate(job.Object, parameters);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            var ex = Assert.Throws<ArgumentException>(Act);
            Assert.That(ex, Has.Property(nameof(ex.Message)).Contain("startDate"));
            Assert.That(ex, Has.Property(nameof(ex.Message)).Contain("date"));
        }
    }

    [Test]
    public void Validate_HandlesMultipleParameters()
    {
        // Arrange
        var job = new Mock<IJob>();
        job.SetupGet(j => j.Parameters).Returns([
            new JobParameter("libraryId", "The library ID", DataType.String, true, null),
            new JobParameter("deep", "Deep scan", DataType.Boolean, false, "false"),
            new JobParameter("limit", "Max items", DataType.Number, false, "100")
        ]);
        var parameters = new Dictionary<string, string>
        {
            ["libraryId"] = "lib-123"
        };

        // Act
        var result = JobParameterValidator.TryValidate(job.Object, parameters);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Count.EqualTo(3));
            Assert.That(result, Contains.Key("libraryId").WithValue("lib-123"));
            Assert.That(result, Contains.Key("deep").WithValue("false"));
            Assert.That(result, Contains.Key("limit").WithValue("100"));
        }
    }

    [Test]
    public void Validate_OverridesDefaultWithProvidedValue()
    {
        // Arrange
        var job = new Mock<IJob>();
        job.SetupGet(j => j.Parameters).Returns([
            new JobParameter("deep", "Deep scan", DataType.Boolean, false, "false")
        ]);
        var parameters = new Dictionary<string, string> { ["deep"] = "true" };

        // Act
        var result = JobParameterValidator.TryValidate(job.Object, parameters);

        // Assert
        Assert.That(result, Contains.Key("deep").WithValue("true"));
    }

    [Test]
    public void Validate_ParameterLookupIsCaseInsensitive()
    {
        // Arrange
        var job = new Mock<IJob>();
        job.SetupGet(j => j.Parameters).Returns([
            new JobParameter("libraryId", "The library ID", DataType.String, true, null)
        ]);
        var parameters = new Dictionary<string, string> { ["LIBRARYID"] = "test-value" };

        // Act
        var result = JobParameterValidator.TryValidate(job.Object, parameters);

        // Assert
        Assert.That(result, Contains.Key("libraryId").WithValue("test-value"));
    }
}
