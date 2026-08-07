using Magpie.Models.Arguments;
using Magpie.Models.Data;

namespace Magpie.Abstractions.Repositories;

/// <summary>
/// The repository contract for settings groups.
/// </summary>
public interface ISettingsRepository : IPatchRepository<SettingsGroupLookupKey, SettingsGroup, SettingsGroupPatch>
{
}
