using System;

namespace Magpie.Core.Repositories;

internal sealed record MetadataRelationshipScope(Guid PivotContainerId, MetadataRelationshipDirection Direction);
