using Magpie.Models.Arguments;
using Magpie.Models.Data;

namespace Magpie.Abstractions.Repositories;

/// <summary>
/// Repository for managing settings and settings groups.
/// </summary>
public interface ISettingsRepository : IPatchRepository<SettingsGroupLookupKey, SettingsGroup, SettingsGroupPatch>
{
}
