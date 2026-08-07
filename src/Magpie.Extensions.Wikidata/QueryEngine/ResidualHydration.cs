using System;

namespace Magpie.Extensions.Wikidata.QueryEngine;

[Flags]
internal enum ResidualHydration
{
    None = 0,
    Entity = 1,
    Support = 2
}
