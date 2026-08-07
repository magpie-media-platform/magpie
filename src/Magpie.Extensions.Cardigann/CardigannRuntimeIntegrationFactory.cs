using System;

using Magpie.Extensions.Cardigann.Engine;
using Magpie.Extensions.Cardigann.Engine.Session;
using Magpie.Models.Data;
using Magpie.Sdk.Integrations;

using Microsoft.Extensions.Logging;

namespace Magpie.Extensions.Cardigann;

internal sealed class CardigannRuntimeIntegrationFactory(
    ICardigannLoader loader,
    IResultParser parser,
    ICardigannSessionManager sessions,
    ILogger<CardigannIndexerIntegration> logger) : IRuntimeIndexerIntegrationFactory
{
    public const string DefinitionTypeName = "cardigann";

    public string DefinitionType => DefinitionTypeName;

    public IIndexerIntegration Create(Indexer indexer)
    {
        if (string.IsNullOrWhiteSpace(indexer.SchemaData))
        {
            throw new InvalidOperationException(
                $"Indexer {indexer.Name} is marked {DefinitionTypeName} but carries no definition " +
                "(SchemaData is empty).");
        }

        var definition = loader.LoadString(indexer.SchemaData!, indexer.Name);
        return new CardigannIndexerIntegration(definition, parser, sessions, logger);
    }
}
