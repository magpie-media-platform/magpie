using Magpie.Extensions.Cardigann;
using Magpie.Extensions.Cardigann.Engine;
using Magpie.Extensions.Cardigann.Engine.Session;
using Magpie.Models.Data;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Extensions.Cardigann;

[TestFixture]
public sealed class CardigannRuntimeIntegrationFactoryTests
{
    [Test]
    public void Create_ReusesCachedDefinition_WhenYamlMatches()
    {
        // Arrange
        const string yaml = "definition";
        var loader = new Mock<ICardigannLoader>();
        loader.Setup(subject => subject.LoadString(yaml, It.IsAny<string>())).Returns(new CardigannDefinition());
        var factory = new CardigannRuntimeIntegrationFactory(
            loader.Object,
            Mock.Of<IResultParser>(),
            Mock.Of<ICardigannSessionManager>(),
            NullLogger<CardigannIndexerIntegration>.Instance);

        // Act
        factory.Create(new Indexer { ProviderType = "cardigann", Name = "First", SchemaData = yaml });
        factory.Create(new Indexer { ProviderType = "cardigann", Name = "Second", SchemaData = yaml });

        // Assert
        loader.Verify(subject => subject.LoadString(yaml, "First"), Times.Once);
    }

    [Test]
    public void Create_EvictsOldestDefinition_WhenCacheIsFull()
    {
        // Arrange
        const string yaml = "oldest-definition";
        var loader = new Mock<ICardigannLoader>();
        loader
            .Setup(subject => subject.LoadString(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(new CardigannDefinition());
        var factory = new CardigannRuntimeIntegrationFactory(
            loader.Object,
            Mock.Of<IResultParser>(),
            Mock.Of<ICardigannSessionManager>(),
            NullLogger<CardigannIndexerIntegration>.Instance);

        // Act
        factory.Create(new Indexer { ProviderType = "cardigann", Name = "Oldest", SchemaData = yaml });
        for (var index = 0; index < 100; index++)
        {
            factory.Create(new Indexer
            {
                ProviderType = "cardigann",
                Name = $"Indexer {index}",
                SchemaData = $"definition-{index}",
            });
        }

        factory.Create(new Indexer { ProviderType = "cardigann", Name = "Reloaded", SchemaData = yaml });

        // Assert
        loader.Verify(subject => subject.LoadString(yaml, It.IsAny<string>()), Times.Exactly(2));
    }
}
