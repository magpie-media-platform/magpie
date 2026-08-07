using Magpie.Models;
using Magpie.Models.Enums;

using NUnit.Framework;

namespace Magpie.Test.Core.Integrations;

[TestFixture]
public class SettingSchemaDefinitionTests
{
    [Test]
    public void Construction_WithRequiredFields_Succeeds()
    {
        // Arrange
        // Act
        var definition = new SettingSchemaDefinition
        {
            Key = "host",
            Type = DataType.String
        };

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(definition.Key, Is.EqualTo("host"));
            Assert.That(definition.Type, Is.EqualTo(DataType.String));
        }
    }

    [Test]
    public void Defaults_AreFalseOrNull_WhenOptionalFieldsOmitted()
    {
        // Arrange
        // Act
        var definition = new SettingSchemaDefinition
        {
            Key = "apiKey",
            Type = DataType.String
        };

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(definition.Required, Is.False);
            Assert.That(definition.Secret, Is.False);
            Assert.That(definition.Description, Is.Null);
            Assert.That(definition.DisplayName, Is.Null);
            Assert.That(definition.DefaultValue, Is.Null);
            Assert.That(definition.Options, Is.Null);
            Assert.That(definition.Placeholder, Is.Null);
        }
    }

    [Test]
    public void Construction_WithAllFields_PopulatesAllProperties()
    {
        // Arrange
        // Act
        var definition = new SettingSchemaDefinition
        {
            Key = "protocol",
            Type = DataType.String,
            DisplayName = "Protocol",
            Description = "Which transport to use.",
            Required = true,
            Secret = false,
            DefaultValue = "https",
            Options = ["http", "https"],
            Placeholder = "https"
        };

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(definition.DisplayName, Is.EqualTo("Protocol"));
            Assert.That(definition.Description, Is.EqualTo("Which transport to use."));
            Assert.That(definition.Required, Is.True);
            Assert.That(definition.Secret, Is.False);
            Assert.That(definition.DefaultValue, Is.EqualTo("https"));
            Assert.That(definition.Options, Is.EqualTo(["http", "https"]));
            Assert.That(definition.Placeholder, Is.EqualTo("https"));
        }
    }
}
