using Magpie.Models.Data;

namespace Magpie.Abstractions.Repositories;

/// <summary>
/// The repository contract for materialized metadata.
/// </summary>
public interface IMaterializedMetadataRepository : IQueryRepository<MaterializedMetadata, MetadataCriteria>;
