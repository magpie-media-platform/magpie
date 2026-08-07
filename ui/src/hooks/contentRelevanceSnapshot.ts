import type { QueryClient } from '@tanstack/react-query';

import type { ApiContentHierarchyItem } from '@/types/content';
import type { MetadataInspectorResponse } from '@/types/contentMetadata';

import {
    impactChangedIds,
    impactContainingIds,
    impactRelationshipContainerIds,
    type ContentEventImpact,
} from './contentEventImpact';

export interface ContentRelevanceSnapshot {
    /** Hierarchy items the surface renders as ancestors of its subjects. */
    ancestors: ReadonlySet<string>;
    /** Metadata containers the surface observes. */
    metadataContainers: ReadonlySet<string>;
    /** The entities the surface is open for, hierarchy items and their content items. */
    contentEntityIds: ReadonlySet<string>;
}

interface SnapshotCacheEntry {
    observed: readonly unknown[];
    snapshot: ContentRelevanceSnapshot;
}

interface CacheScan {
    details: Map<string, ApiContentHierarchyItem>;
    inspectors: Map<string, MetadataInspectorResponse[]>;
}

const MAX_CACHED_SNAPSHOTS = 32;
const MAX_ANCESTOR_DEPTH = 32;

const EMPTY_SNAPSHOT: ContentRelevanceSnapshot = {
    ancestors: new Set(),
    metadataContainers: new Set(),
    contentEntityIds: new Set(),
};

const snapshotCaches = new WeakMap<QueryClient, Map<string, SnapshotCacheEntry>>();

function cacheFor(queryClient: QueryClient): Map<string, SnapshotCacheEntry> {
    let cache = snapshotCaches.get(queryClient);
    if (!cache) {
        cache = new Map();
        snapshotCaches.set(queryClient, cache);
    }
    return cache;
}

function scanCache(queryClient: QueryClient): CacheScan {
    const details = new Map<string, ApiContentHierarchyItem>();
    const inspectors = new Map<string, MetadataInspectorResponse[]>();

    for (const query of queryClient.getQueryCache().getAll()) {
        const [family, kind, id] = query.queryKey as readonly unknown[];
        if (family !== 'content' || typeof id !== 'string' || query.state.data === undefined) {
            continue;
        }
        if (kind === 'detail') {
            details.set(id, query.state.data as ApiContentHierarchyItem);
        } else if (kind === 'metadata-inspector') {
            const existing = inspectors.get(id) ?? [];
            existing.push(query.state.data as MetadataInspectorResponse);
            inspectors.set(id, existing);
        }
    }

    return { details, inspectors };
}

function ancestorsOf(subjectId: string, details: ReadonlyMap<string, ApiContentHierarchyItem>): string[] {
    const detail = details.get(subjectId);
    if (!detail) {
        return [];
    }

    const ancestors: string[] = [];
    const seen = new Set<string>([subjectId]);
    let current = detail.parentId;
    while (current && !seen.has(current) && ancestors.length < MAX_ANCESTOR_DEPTH) {
        ancestors.push(current);
        seen.add(current);
        current = details.get(current)?.parentId ?? undefined;
    }

    return ancestors;
}

function build(
    entityIds: readonly string[],
    scan: CacheScan,
): { observed: unknown[]; snapshot: ContentRelevanceSnapshot } {
    const contentEntityIds = new Set<string>(entityIds);
    const ancestors = new Set<string>();
    const metadataContainers = new Set<string>();
    const observed: unknown[] = [];

    for (const id of entityIds) {
        const detail = scan.details.get(id);
        observed.push(detail);
        if (detail) {
            if (detail.contentItem?.id) {
                contentEntityIds.add(detail.contentItem.id);
            }
            if (detail.metadataContainerId) {
                metadataContainers.add(detail.metadataContainerId);
            }
            for (const ancestorId of ancestorsOf(id, scan.details)) {
                ancestors.add(ancestorId);
                observed.push(scan.details.get(ancestorId));
            }
        }

        const inspectorData = scan.inspectors.get(id) ?? [];
        observed.push(inspectorData.length);
        for (const inspector of inspectorData) {
            observed.push(inspector);
            if (inspector.materialized.containerId) {
                metadataContainers.add(inspector.materialized.containerId);
            }
            for (const relationship of inspector.materialized.relationships ?? []) {
                if (relationship.relatedMetadataContainerId) {
                    metadataContainers.add(relationship.relatedMetadataContainerId);
                }
            }
        }
    }

    return { observed, snapshot: { ancestors, contentEntityIds, metadataContainers } };
}

function sameObservations(left: readonly unknown[], right: readonly unknown[]): boolean {
    return left.length === right.length && left.every((value, index) => value === right[index]);
}

/**
 * Builds the one relevance snapshot a content surface shares across its hooks, from data already in cache.
 *
 * The snapshot is rebuilt whenever any observed query datum is replaced, and reused by reference otherwise, so
 * every hook on a surface compares events against one shared and current view. It never issues a request.
 */
export function contentRelevanceSnapshot(
    queryClient: QueryClient,
    entityIds: readonly string[] | undefined,
): ContentRelevanceSnapshot {
    if (!entityIds || entityIds.length === 0) {
        return EMPTY_SNAPSHOT;
    }

    const sorted = [...entityIds].sort();
    const key = sorted.join(',');
    const cache = cacheFor(queryClient);
    const { observed, snapshot } = build(sorted, scanCache(queryClient));
    const existing = cache.get(key);
    if (existing && sameObservations(existing.observed, observed)) {
        return existing.snapshot;
    }

    cache.delete(key);
    cache.set(key, { observed, snapshot });
    while (cache.size > MAX_CACHED_SNAPSHOTS) {
        const oldest = cache.keys().next();
        if (oldest.done) {
            break;
        }
        cache.delete(oldest.value);
    }

    return snapshot;
}

/**
 * Whether an event impact touches the active snapshot, matching each event role against the matching active role.
 *
 * Roles are never crossed: an event's containing hierarchy is only ever compared against the surface's own
 * subjects, so a sibling sharing the surface's parent does not read as relevant. A scope the producer had to
 * truncate cannot be proven irrelevant, so it falls back to one bounded refetch.
 */
export function impactIntersectsSnapshot(impact: ContentEventImpact, snapshot: ContentRelevanceSnapshot): boolean {
    if (impact.subtreeTruncated || impact.batchTruncated) {
        return true;
    }

    const changed = impactChangedIds(impact);
    return (
        changed.some((id) => snapshot.contentEntityIds.has(id) || snapshot.ancestors.has(id)) ||
        impactContainingIds(impact).some((id) => snapshot.contentEntityIds.has(id)) ||
        impactRelationshipContainerIds(impact).some((id) => snapshot.metadataContainers.has(id))
    );
}
