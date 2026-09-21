import type { EventStreamPayload } from './useEventStream';

interface ContentEventImpactScope {
    ancestorIds: readonly string[];
    contentType?: string;
    fulfillment?: string;
    level?: string;
    monitoring?: string;
    parentId?: string;
    qualityTierListId?: string;
    rootFolderId?: string;
    rootId?: string;
    topLevel?: string;
    wishlistId?: string;
}

export interface ContentEventImpact {
    batchIds: readonly string[];
    batchTruncated: boolean;
    contentItemId?: string;
    hierarchyItemId?: string;
    metadataAddedContainerIds: readonly string[];
    metadataContainerId?: string;
    metadataRemovedContainerIds: readonly string[];
    metadataSourceContainerId?: string;
    previous?: ContentEventImpactScope;
    scope: ContentEventImpactScope;
    subtreeIds: readonly string[];
    subtreeTruncated: boolean;
}

const IMPACT_KEYS = {
    ancestorIds: 'content.impact.ancestorIds',
    batchIds: 'content.impact.batchIds',
    batchTruncated: 'content.impact.batchTruncated',
    contentItemId: 'content.itemId',
    contentType: 'content.impact.contentType',
    fulfillment: 'content.impact.fulfillment',
    hierarchyItemId: 'content.hierarchyItemId',
    level: 'content.impact.level',
    metadataAddedContainerIds: 'content.impact.metadata.addedContainerIds',
    metadataContainerId: 'content.impact.metadataContainerId',
    metadataRemovedContainerIds: 'content.impact.metadata.removedContainerIds',
    metadataSourceContainerId: 'content.impact.metadata.sourceContainerId',
    monitoring: 'content.impact.monitoring',
    parentId: 'content.impact.parentId',
    previousAncestorIds: 'content.impact.previous.ancestorIds',
    previousContentType: 'content.impact.previous.contentType',
    previousFulfillment: 'content.impact.previous.fulfillment',
    previousLevel: 'content.impact.previous.level',
    previousMonitoring: 'content.impact.previous.monitoring',
    previousParentId: 'content.impact.previous.parentId',
    previousQualityTierListId: 'content.impact.previous.qualityTierListId',
    previousRootFolderId: 'content.impact.previous.rootFolderId',
    previousRootId: 'content.impact.previous.rootId',
    previousTopLevel: 'content.impact.previous.topLevel',
    qualityTierListId: 'content.impact.qualityTierListId',
    rootFolderId: 'content.impact.rootFolderId',
    rootId: 'content.impact.rootId',
    subtreeIds: 'content.impact.subtreeIds',
    subtreeTruncated: 'content.impact.subtreeTruncated',
    topLevel: 'content.impact.topLevel',
    wishlistId: 'content.impact.wishlistId',
} as const;

const impacts = new WeakMap<EventStreamPayload, ContentEventImpact | null>();

function text(context: Readonly<Record<string, string>>, key: string): string | undefined {
    const value = context[key];
    return value && value.length > 0 ? value : undefined;
}

function ids(context: Readonly<Record<string, string>>, key: string): readonly string[] {
    return context[key]?.split(',').filter((id) => id.length > 0) ?? [];
}

function scopeOf(context: Readonly<Record<string, string>>, previous: boolean): ContentEventImpactScope {
    return previous
        ? {
              ancestorIds: ids(context, IMPACT_KEYS.previousAncestorIds),
              contentType: text(context, IMPACT_KEYS.previousContentType),
              fulfillment: text(context, IMPACT_KEYS.previousFulfillment),
              level: text(context, IMPACT_KEYS.previousLevel),
              monitoring: text(context, IMPACT_KEYS.previousMonitoring),
              parentId: text(context, IMPACT_KEYS.previousParentId),
              qualityTierListId: text(context, IMPACT_KEYS.previousQualityTierListId),
              rootFolderId: text(context, IMPACT_KEYS.previousRootFolderId),
              rootId: text(context, IMPACT_KEYS.previousRootId),
              topLevel: text(context, IMPACT_KEYS.previousTopLevel),
          }
        : {
              ancestorIds: ids(context, IMPACT_KEYS.ancestorIds),
              contentType: text(context, IMPACT_KEYS.contentType),
              fulfillment: text(context, IMPACT_KEYS.fulfillment),
              level: text(context, IMPACT_KEYS.level),
              monitoring: text(context, IMPACT_KEYS.monitoring),
              parentId: text(context, IMPACT_KEYS.parentId),
              qualityTierListId: text(context, IMPACT_KEYS.qualityTierListId),
              rootFolderId: text(context, IMPACT_KEYS.rootFolderId),
              rootId: text(context, IMPACT_KEYS.rootId),
              topLevel: text(context, IMPACT_KEYS.topLevel),
              wishlistId: text(context, IMPACT_KEYS.wishlistId),
          };
}

function hasPreviousScope(context: Readonly<Record<string, string>>): boolean {
    return [
        IMPACT_KEYS.previousAncestorIds,
        IMPACT_KEYS.previousContentType,
        IMPACT_KEYS.previousFulfillment,
        IMPACT_KEYS.previousLevel,
        IMPACT_KEYS.previousMonitoring,
        IMPACT_KEYS.previousParentId,
        IMPACT_KEYS.previousQualityTierListId,
        IMPACT_KEYS.previousRootFolderId,
        IMPACT_KEYS.previousRootId,
        IMPACT_KEYS.previousTopLevel,
    ].some((key) => Boolean(context[key]));
}

/** Reads the self-contained content impact an event carries, or null when it carries none. */
export function readContentEventImpact(event: EventStreamPayload): ContentEventImpact | null {
    const cached = impacts.get(event);
    if (cached !== undefined) {
        return cached;
    }

    const context = event.context;
    const hierarchyItemId = text(context, IMPACT_KEYS.hierarchyItemId);
    const contentItemId = text(context, IMPACT_KEYS.contentItemId);
    const batchIds = ids(context, IMPACT_KEYS.batchIds);
    const subtreeIds = ids(context, IMPACT_KEYS.subtreeIds);
    const impact: ContentEventImpact | null =
        hierarchyItemId || contentItemId || batchIds.length > 0 || subtreeIds.length > 0
            ? {
                  batchIds,
                  batchTruncated: context[IMPACT_KEYS.batchTruncated] === 'true',
                  contentItemId,
                  hierarchyItemId,
                  metadataAddedContainerIds: ids(context, IMPACT_KEYS.metadataAddedContainerIds),
                  metadataContainerId: text(context, IMPACT_KEYS.metadataContainerId),
                  metadataRemovedContainerIds: ids(context, IMPACT_KEYS.metadataRemovedContainerIds),
                  metadataSourceContainerId: text(context, IMPACT_KEYS.metadataSourceContainerId),
                  previous: hasPreviousScope(context) ? scopeOf(context, true) : undefined,
                  scope: scopeOf(context, false),
                  subtreeIds,
                  subtreeTruncated: context[IMPACT_KEYS.subtreeTruncated] === 'true',
              }
            : null;

    impacts.set(event, impact);
    return impact;
}

function present(ids: readonly (string | undefined)[]): readonly string[] {
    return ids.filter((id): id is string => Boolean(id));
}

/** The entities the event changed: the primary one plus every entity a subtree or batch change touched. */
export function impactChangedIds(impact: ContentEventImpact): readonly string[] {
    return present([impact.hierarchyItemId, impact.contentItemId, ...impact.subtreeIds, ...impact.batchIds]);
}

/** The hierarchy the changed entity sits under, before and after the change. */
export function impactContainingIds(impact: ContentEventImpact): readonly string[] {
    return present([
        impact.scope.parentId,
        impact.scope.rootId,
        ...impact.scope.ancestorIds,
        impact.previous?.parentId,
        impact.previous?.rootId,
        ...(impact.previous?.ancestorIds ?? []),
    ]);
}

/** The metadata containers the changed relationships point at, in either direction. */
export function impactRelationshipContainerIds(impact: ContentEventImpact): readonly string[] {
    return present([
        impact.metadataSourceContainerId,
        ...impact.metadataAddedContainerIds,
        ...impact.metadataRemovedContainerIds,
    ]);
}
