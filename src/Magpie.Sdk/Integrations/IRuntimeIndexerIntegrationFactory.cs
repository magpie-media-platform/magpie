using Magpie.Models.Data;

namespace Magpie.Sdk.Integrations;

/// <summary>
/// A factory for indexer integrations from indexer definitions.
/// </summary>
public interface IRuntimeIndexerIntegrationFactory
{
    /// <summary>
    /// The indexer definition type that this factory handles.
    /// </summary>
    string DefinitionType { get; }

    /// <summary>
    /// Creates an integration for an indexer.
    /// </summary>
    /// <param name="indexer">The indexer that the integration connects to</param>
    /// <returns>The new integration.</returns>
    IIndexerIntegration Create(Indexer indexer);
}
