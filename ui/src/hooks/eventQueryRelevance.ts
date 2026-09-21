import type { QueryKey } from '@tanstack/react-query';

import { readContentEventImpact } from './contentEventImpact';
import { impactIntersectsSnapshot, type ContentRelevanceSnapshot } from './contentRelevanceSnapshot';
import { ENTITY_ID_CONTEXT_KEYS } from './entityIdContextKeys';
import type { EventStreamPayload } from './useEventStream';

type EventRelevanceDecision = 'irrelevant' | 'relevant';

export interface EventQueryRelevanceScope {
    context?: Readonly<Record<string, string>>;
    entityIds?: readonly string[];
    eventNames: readonly string[];
    failOpenEventNames?: readonly string[];
    prePredicate?: (event: EventStreamPayload) => boolean;
    predicate?: (event: EventStreamPayload) => boolean;
    queryKeys: readonly QueryKey[];
    snapshot?: ContentRelevanceSnapshot;
    unprovableMembership?: boolean;
}

interface EventRelevanceResult {
    decision: EventRelevanceDecision;
    reason: string;
}

const ID_CONTEXT_KEYS: ReadonlySet<string> = new Set(ENTITY_ID_CONTEXT_KEYS);

const PRIMARY_EVENT_IDENTITIES: readonly {
    contextKey: string;
    eventPrefix: string;
    queryFamily: string;
}[] = [
    { contextKey: 'apiKey.id', eventPrefix: 'apiKey.', queryFamily: 'api-keys' },
    { contextKey: 'backup.id', eventPrefix: 'backup.', queryFamily: 'backups' },
    { contextKey: 'blockedFileType.id', eventPrefix: 'blockedFileType.', queryFamily: 'blocked-file-types' },
    { contextKey: 'contentFilter.id', eventPrefix: 'contentFilter.', queryFamily: 'content-filters' },
    { contextKey: 'content.hierarchyItemId', eventPrefix: 'content.hierarchyItem.', queryFamily: 'content' },
    { contextKey: 'diagnostic.findingId', eventPrefix: 'diagnostic.finding.', queryFamily: 'diagnostics' },
    { contextKey: 'downloadClient.id', eventPrefix: 'downloadClient.', queryFamily: 'download-clients' },
    { contextKey: 'download.id', eventPrefix: 'download.', queryFamily: 'downloads' },
    { contextKey: 'eventTrigger.id', eventPrefix: 'eventTrigger.', queryFamily: 'event-triggers' },
    { contextKey: 'execution.id', eventPrefix: 'job.execution.', queryFamily: 'jobs' },
    { contextKey: 'extension.id', eventPrefix: 'extension.', queryFamily: 'extensions' },
    { contextKey: 'indexer.id', eventPrefix: 'indexer.', queryFamily: 'indexers' },
    { contextKey: 'mediaServer.id', eventPrefix: 'mediaServer.', queryFamily: 'media-servers' },
    { contextKey: 'notificationRule.id', eventPrefix: 'notificationRule.', queryFamily: 'notification-rules' },
    { contextKey: 'qualityTierList.id', eventPrefix: 'qualityTierList.', queryFamily: 'quality-tier-lists' },
    { contextKey: 'release.id', eventPrefix: 'release.', queryFamily: 'releases' },
    { contextKey: 'remotePathMapping.id', eventPrefix: 'remotePathMapping.', queryFamily: 'remote-path-mappings' },
    { contextKey: 'rootFolder.id', eventPrefix: 'library.rootFolder.', queryFamily: 'root-folders' },
    { contextKey: 'scheduleTrigger.id', eventPrefix: 'scheduleTrigger.', queryFamily: 'schedule-triggers' },
    { contextKey: 'subject.id', eventPrefix: 'subject.', queryFamily: 'subjects' },
    { contextKey: 'user.id', eventPrefix: 'user.', queryFamily: 'users' },
    { contextKey: 'wishlist.id', eventPrefix: 'wishlist.', queryFamily: 'wishlists' },
];

function contextMatches(
    event: EventStreamPayload,
    expectedContext: Readonly<Record<string, string>> | undefined,
): boolean {
    return !expectedContext || Object.entries(expectedContext).every(([key, value]) => event.context[key] === value);
}

function hasMismatchedPrimaryIdentity(
    event: EventStreamPayload,
    queryKeys: readonly QueryKey[],
    entityIds: ReadonlySet<string>,
): boolean {
    const identity = PRIMARY_EVENT_IDENTITIES.find(
        ({ eventPrefix, queryFamily }) =>
            event.name.startsWith(eventPrefix) &&
            queryKeys.every((queryKey) => queryKey[0] === queryFamily && queryKey[1] === 'detail'),
    );
    if (!identity) {
        return false;
    }

    const eventIds = event.context[identity.contextKey]?.split(',').filter(Boolean);
    return Boolean(eventIds?.length) && !eventIds?.some((id) => entityIds.has(id));
}

function predicateMatches(
    event: EventStreamPayload,
    predicate: ((event: EventStreamPayload) => boolean) | undefined,
): boolean {
    return !predicate || predicate(event);
}

function eventContextMatchesEntityIds(event: EventStreamPayload, entityIds: ReadonlySet<string>): boolean {
    return Object.entries(event.context).some(
        ([key, value]) => ID_CONTEXT_KEYS.has(key) && value.split(',').some((id) => entityIds.has(id)),
    );
}

export function eventFingerprint(event: EventStreamPayload): string {
    const context = Object.entries(event.context).sort(([left], [right]) => left.localeCompare(right));
    return JSON.stringify([event.name, context]);
}

/** Decides, synchronously and without any network access, whether an event affects a query scope. */
export function evaluateEventQueryRelevance(
    event: EventStreamPayload,
    scope: EventQueryRelevanceScope,
): EventRelevanceResult {
    if (event.name === 'stream.reconnected') {
        return { decision: 'relevant', reason: 'stream reconnected' };
    }
    if (!scope.eventNames.includes(event.name)) {
        return { decision: 'irrelevant', reason: 'event name is outside the query family' };
    }
    if (!contextMatches(event, scope.context)) {
        return { decision: 'irrelevant', reason: 'event context does not match the query filters' };
    }
    if (scope.failOpenEventNames?.includes(event.name)) {
        return { decision: 'relevant', reason: 'new entity cannot be proven irrelevant to the query filters' };
    }
    if (scope.prePredicate && !scope.prePredicate(event)) {
        return { decision: 'irrelevant', reason: 'event is rejectable by the available query filters' };
    }

    const entityIds = new Set(scope.entityIds ?? []);
    if (scope.entityIds && entityIds.size === 0) {
        return { decision: 'irrelevant', reason: 'query entity scope has no current members' };
    }

    if (entityIds.size > 0) {
        const impact = readContentEventImpact(event);
        const snapshot = scope.snapshot ?? {
            ancestors: new Set<string>(),
            metadataContainers: new Set<string>(),
            contentEntityIds: entityIds,
        };
        if (
            eventContextMatchesEntityIds(event, entityIds) ||
            (impact !== null && impactIntersectsSnapshot(impact, snapshot))
        ) {
            return predicateMatches(event, scope.predicate)
                ? { decision: 'relevant', reason: 'event impact intersects the active snapshot' }
                : { decision: 'irrelevant', reason: 'event does not match the query predicate' };
        }

        return hasMismatchedPrimaryIdentity(event, scope.queryKeys, entityIds)
            ? { decision: 'irrelevant', reason: 'event identity belongs to a different entity' }
            : { decision: 'irrelevant', reason: 'event impact does not intersect the active snapshot' };
    }

    if (scope.unprovableMembership && readContentEventImpact(event) === null) {
        return { decision: 'relevant', reason: 'event carries no impact the list filters can reject' };
    }

    return predicateMatches(event, scope.predicate)
        ? { decision: 'relevant', reason: 'event matches the query filters' }
        : { decision: 'irrelevant', reason: 'event does not match the query predicate' };
}
