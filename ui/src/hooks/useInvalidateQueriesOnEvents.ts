import { useCallback, useEffect, useMemo, useRef } from 'react';
import { useQueryClient, type Query, type QueryKey } from '@tanstack/react-query';

import { contentRelevanceSnapshot } from './contentRelevanceSnapshot';
import { evaluateEventQueryRelevance, eventFingerprint, type EventQueryRelevanceScope } from './eventQueryRelevance';
import { useEventStream, type EventStreamPayload } from './useEventStream';

interface InvalidateQueriesOnEventsOptions {
    context?: Readonly<Record<string, string>>;
    debounceMs?: number;
    entityIds?: readonly string[];
    exact: boolean;
    failOpenEventNames?: readonly string[];
    maxWaitMs?: number;
    owner: string;
    prePredicate?: (event: EventStreamPayload) => boolean;
    predicate?: (event: EventStreamPayload) => boolean;
    unprovableMembership?: boolean;
}

interface RefreshCoordinator {
    recentRefetches: Map<string, number>;
}

const BURST_EVENT_DEBOUNCE_MS: Readonly<Record<string, number>> = {
    'content.fulfillment.changed': 200,
    'content.hierarchyItem.created': 200,
    'content.hierarchyItem.deleted': 200,
    'content.hierarchyItem.updated': 200,
    'diagnostic.finding.raised': 200,
    'diagnostic.finding.updated': 200,
    'download.updated': 200,
    'integration.health.changed': 150,
    'job.execution.created': 150,
    'job.execution.progress.changed': 150,
    'job.execution.queued': 150,
    'job.execution.started': 150,
    'metadata.refreshed': 200,
};

const refreshCoordinators = new WeakMap<object, RefreshCoordinator>();
const reconnectRefetches = new WeakMap<EventStreamPayload, WeakMap<object, Set<string>>>();

function recommendedDebounceMs(eventNames: readonly string[]): number {
    return eventNames.reduce((delay, eventName) => Math.max(delay, BURST_EVENT_DEBOUNCE_MS[eventName] ?? 0), 0);
}

function reconnectTargets(event: EventStreamPayload, queryClient: ReturnType<typeof useQueryClient>): Set<string> {
    let clients = reconnectRefetches.get(event);
    if (!clients) {
        clients = new WeakMap();
        reconnectRefetches.set(event, clients);
    }
    let targets = clients.get(queryClient);
    if (!targets) {
        targets = new Set();
        clients.set(queryClient, targets);
    }
    return targets;
}

function coordinatorFor(queryClient: ReturnType<typeof useQueryClient>): RefreshCoordinator {
    let coordinator = refreshCoordinators.get(queryClient);
    if (!coordinator) {
        coordinator = { recentRefetches: new Map() };
        refreshCoordinators.set(queryClient, coordinator);
    }
    return coordinator;
}

export function useInvalidateQueriesOnEvents(
    eventNames: readonly string[],
    queryKeys: readonly QueryKey[],
    options: InvalidateQueriesOnEventsOptions,
): void {
    const queryClient = useQueryClient();
    const queryKeysRef = useRef(queryKeys);
    const optionsRef = useRef(options);
    const eventNamesRef = useRef(eventNames);
    const debounceMsRef = useRef(options.debounceMs ?? recommendedDebounceMs(eventNames));
    const debounceTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
    const maxWaitTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
    const mountedRef = useRef(false);
    const generationRef = useRef(0);
    const pendingEventsRef = useRef(new Map<string, EventStreamPayload>());
    queryKeysRef.current = queryKeys;
    optionsRef.current = options;
    eventNamesRef.current = eventNames;
    debounceMsRef.current = options.debounceMs ?? recommendedDebounceMs(eventNames);

    const exact = options.exact;
    const owner = options.owner;
    const scopeKey = useMemo(
        () =>
            JSON.stringify({
                context: Object.entries(options.context ?? {}).sort(([left], [right]) => left.localeCompare(right)),
                entityIds: [...(options.entityIds ?? [])].sort(),
                eventNames: [...eventNames].sort(),
                exact,
                failOpenEventNames: [...(options.failOpenEventNames ?? [])].sort(),
                owner,
                queryKeys,
                unprovableMembership: options.unprovableMembership,
            }),
        [
            eventNames,
            exact,
            options.context,
            options.entityIds,
            options.failOpenEventNames,
            options.unprovableMembership,
            owner,
            queryKeys,
        ],
    );

    const clearTimers = useCallback(() => {
        if (debounceTimerRef.current !== null) {
            clearTimeout(debounceTimerRef.current);
            debounceTimerRef.current = null;
        }
        if (maxWaitTimerRef.current !== null) {
            clearTimeout(maxWaitTimerRef.current);
            maxWaitTimerRef.current = null;
        }
    }, []);

    const refreshTargets = useCallback(
        (
            targetQueryKeys: readonly QueryKey[],
            targetExact: boolean,
            coalesceMs: number,
            reconnectEvent?: EventStreamPayload,
        ) => {
            const queries = new Map<string, Query>();
            for (const queryKey of targetQueryKeys) {
                for (const query of queryClient.getQueryCache().findAll({ exact: targetExact, queryKey })) {
                    queries.set(query.queryHash, query);
                }
                void queryClient.invalidateQueries({ exact: targetExact, queryKey, refetchType: 'none' });
            }

            const now = Date.now();
            const coordinator = coordinatorFor(queryClient);
            for (const [queryHash, expiresAt] of coordinator.recentRefetches) {
                if (expiresAt <= now) {
                    coordinator.recentRefetches.delete(queryHash);
                }
            }
            const reconnectedTargets = reconnectEvent ? reconnectTargets(reconnectEvent, queryClient) : undefined;
            const eligibleHashes = new Set<string>();

            for (const query of queries.values()) {
                const active = query.isActive();
                const fetching = query.state.fetchStatus === 'fetching';
                const recentlyRefetched = (coordinator.recentRefetches.get(query.queryHash) ?? 0) > now;
                const reconciled = reconnectedTargets?.has(query.queryHash) ?? false;
                const refetched = active && !fetching && !recentlyRefetched && !reconciled;
                if (refetched) {
                    eligibleHashes.add(query.queryHash);
                    coordinator.recentRefetches.set(query.queryHash, now + Math.max(coalesceMs, 25));
                    reconnectedTargets?.add(query.queryHash);
                }
            }

            if (eligibleHashes.size > 0 && mountedRef.current) {
                void queryClient.refetchQueries(
                    {
                        predicate: (query) => eligibleHashes.has(query.queryHash),
                        type: 'active',
                    },
                    { cancelRefetch: false },
                );
            }
        },
        [queryClient],
    );

    const processPendingEvents = useCallback(() => {
        const events = [...pendingEventsRef.current.values()];
        pendingEventsRef.current.clear();
        clearTimers();
        if (events.length === 0) {
            return;
        }

        const generation = generationRef.current;
        const targetQueryKeys = queryKeysRef.current;
        const targetOptions = optionsRef.current;
        const targetEventNames = eventNamesRef.current;
        const targetExact = targetOptions.exact;
        const scope: EventQueryRelevanceScope = {
            context: targetOptions.context,
            entityIds: targetOptions.entityIds,
            eventNames: targetEventNames,
            failOpenEventNames: targetOptions.failOpenEventNames,
            prePredicate: targetOptions.prePredicate,
            predicate: targetOptions.predicate,
            queryKeys: targetQueryKeys,
            snapshot: contentRelevanceSnapshot(queryClient, targetOptions.entityIds),
            unprovableMembership: targetOptions.unprovableMembership,
        };
        const receivedDecisions = events.map((event) => ({
            event,
            result: evaluateEventQueryRelevance(event, scope),
        }));

        const relevantEvents = receivedDecisions
            .filter(({ result }) => result.decision === 'relevant')
            .map(({ event }) => event);

        if (!mountedRef.current || generationRef.current !== generation || relevantEvents.length === 0) {
            return;
        }

        refreshTargets(
            targetQueryKeys,
            targetExact,
            debounceMsRef.current,
            relevantEvents.find((event) => event.name === 'stream.reconnected'),
        );
    }, [clearTimers, queryClient, refreshTargets]);

    useEffect(() => {
        generationRef.current += 1;
        mountedRef.current = true;
        const pendingEvents = pendingEventsRef.current;
        return () => {
            mountedRef.current = false;
            generationRef.current += 1;
            pendingEvents.clear();
            clearTimers();
        };
    }, [clearTimers, scopeKey]);

    useEventStream(
        (event) => {
            pendingEventsRef.current.set(eventFingerprint(event), event);

            const debounceMs = debounceMsRef.current;
            if (debounceMs <= 0) {
                processPendingEvents();
                return;
            }

            if (debounceTimerRef.current !== null) {
                clearTimeout(debounceTimerRef.current);
            }
            debounceTimerRef.current = setTimeout(processPendingEvents, debounceMs);

            if (maxWaitTimerRef.current === null) {
                const maxWaitMs = Math.max(optionsRef.current.maxWaitMs ?? debounceMs * 4, debounceMs);
                maxWaitTimerRef.current = setTimeout(processPendingEvents, maxWaitMs);
            }
        },
        {
            context: options.context,
            enabled:
                eventNames.length > 0 &&
                (options.entityIds === undefined ||
                    options.entityIds.length > 0 ||
                    Boolean(options.failOpenEventNames?.length)),
            eventName: [...eventNames, 'stream.reconnected'],
            owner,
        },
    );
}
