using System;
using System.Collections.Generic;
using System.Threading;

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
    private const int MaximumCachedDefinitions = 100;

    private readonly Dictionary<string, CardigannDefinition> _definitions = new(StringComparer.Ordinal);
    private readonly Queue<string> _definitionOrder = new();
    private readonly Lock _definitionsLock = new();

    public string DefinitionType => Constants.ProviderTypeName;

    public IIndexerIntegration Create(Indexer indexer)
    {
        if (string.IsNullOrWhiteSpace(indexer.SchemaData))
        {
            throw new InvalidOperationException(
                $"Indexer {indexer.Name} is marked {Constants.ProviderTypeName} but carries no definition " +
                "(SchemaData is empty).");
        }

        lock (_definitionsLock)
        {
            if (!_definitions.TryGetValue(indexer.SchemaData, out var definition))
            {
                definition = loader.LoadString(indexer.SchemaData, indexer.Name);
                _definitions.Add(indexer.SchemaData, definition);
                _definitionOrder.Enqueue(indexer.SchemaData);
                if (_definitionOrder.Count > MaximumCachedDefinitions)
                {
                    _definitions.Remove(_definitionOrder.Dequeue());
                }
            }

            return new CardigannIndexerIntegration(indexer.Id, definition, parser, sessions, logger);
        }
    }
}
