using Magpie.Models.Data;

namespace Magpie.Abstractions.Repositories;

/// <summary>
/// The repository contract for MIME mappings, keyed by normalized file extension.
/// </summary>
public interface IMimeMappingRepository : ICrudRepository<string, MimeMapping>;
