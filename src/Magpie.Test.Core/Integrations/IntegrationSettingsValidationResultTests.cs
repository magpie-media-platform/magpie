using Magpie.Models;

using NUnit.Framework;
namespace Magpie.Test.Core.Integrations;

[TestFixture]
public class IntegrationSettingsValidationResultTests
{
    [Test]
    public void Construction_WithIsValidTrue_DefaultsIssuesToEmptyList()
    {
        // Arrange
        // Act
        var result = new IntegrationSettingsValidationResult { IsValid = true };

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsValid, Is.True);
            Assert.That(result.Issues, Is.Not.Null);
            Assert.That(result.Issues, Is.Empty);
        }
    }

    [Test]
    public void Construction_WithIssues_ExposesProvidedList()
    {
        // Arrange
        // Act
        var result = new IntegrationSettingsValidationResult
        {
            IsValid = false,
            Issues =
            [
                new IntegrationSettingsValidationIssue("host", "Host is required."),
                new IntegrationSettingsValidationIssue(null, "Settings could not be parsed.")
            ]
        };

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Issues, Has.Count.EqualTo(2));
            Assert.That(result.Issues[0].Key, Is.EqualTo("host"));
            Assert.That(result.Issues[0].Message, Is.EqualTo("Host is required."));
            Assert.That(result.Issues[1].Key, Is.Null);
            Assert.That(result.Issues[1].Message, Is.EqualTo("Settings could not be parsed."));
        }
    }

    [Test]
    public void Issue_RecordEquality_MatchesByKeyAndMessage()
    {
        // Arrange
        var a = new IntegrationSettingsValidationIssue("host", "Host is required.");
        var b = new IntegrationSettingsValidationIssue("host", "Host is required.");

        // Act
        var c = new IntegrationSettingsValidationIssue("port", "Host is required.");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(a, Is.EqualTo(b));
            Assert.That(a, Is.Not.EqualTo(c));
        }
    }
}
