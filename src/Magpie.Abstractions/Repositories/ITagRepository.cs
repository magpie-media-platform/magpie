using Magpie.Models.Arguments;
using Magpie.Models.Data;

namespace Magpie.Abstractions.Repositories;

/// <summary>
/// The read-only repository contract for tags.
/// </summary>
public interface ITagRepository : IKeyedRepository<string, Tag>, IQueryRepository<Tag, TagQuery>;
