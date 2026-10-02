import { useEffect, useMemo, useRef } from 'react';

import { ENTITY_ID_CONTEXT_KEYS } from './entityIdContextKeys';
import { connectLiveUpdates } from '@/lib/liveUpdates';

export interface EventStreamPayload {
    name: string;
    context: Record<string, string>;
}

interface UseEventStreamOptions {
    eventName?: string | string[];
    entityId?: string;
    entityIds?: readonly string[];
    context?: Readonly<Record<string, string>>;
    enabled?: boolean;
}

interface EventSubscription {
    eventNames: Set<string>;
    entityIds: Set<string>;
    context: Readonly<Record<string, string>>;
    handler: (event: EventStreamPayload) => void;
}

const NO_ENTITY_IDS: readonly string[] = [];

const subscriptions = new Map<number, EventSubscription>();
let nextSubscriptionId = 0;

function dispatchPayload(payload: EventStreamPayload): void {
    for (const subscription of subscriptions.values()) {
        if (subscription.eventNames.size > 0 && !subscription.eventNames.has(payload.name)) {
            continue;
        }
        if (
            payload.name !== 'stream.reconnected' &&
            subscription.entityIds.size > 0 &&
            !ENTITY_ID_CONTEXT_KEYS.some((key) =>
                payload.context[key]?.split(',').some((id) => subscription.entityIds.has(id)),
            )
        ) {
            continue;
        }
        if (
            payload.name !== 'stream.reconnected' &&
            Object.entries(subscription.context).some(([key, value]) => payload.context[key] !== value)
        ) {
            continue;
        }
        try {
            subscription.handler(payload);
        } catch (error) {
            if (import.meta.env.DEV) {
                console.error('useEventStream: event listener failed', error);
            }
        }
    }
}

export function useGlobalEventStreamConnection(active: boolean, userId = ''): void {
    useEffect(() => {
        if (!active) return;
        return connectLiveUpdates(userId, dispatchPayload);
    }, [active, userId]);
}

export function useEventStream(
    onEvent: (event: EventStreamPayload) => void,
    options: UseEventStreamOptions = {},
): void {
    const { eventName, entityId, entityIds = NO_ENTITY_IDS, context = {}, enabled = true } = options;
    const eventNames = Array.isArray(eventName) ? eventName : eventName ? [eventName] : [];
    const eventNamesKey = eventNames.join(',');
    const entityIdsKey = useMemo(
        () => [...new Set(entityId ? [entityId, ...entityIds] : entityIds)].sort().join(','),
        [entityId, entityIds],
    );
    const contextKey = JSON.stringify(Object.entries(context).sort(([left], [right]) => left.localeCompare(right)));

    const handlerRef = useRef(onEvent);
    handlerRef.current = onEvent;

    useEffect(() => {
        if (!enabled) {
            return;
        }

        const subscriptionId = nextSubscriptionId++;
        subscriptions.set(subscriptionId, {
            eventNames: new Set(eventNamesKey ? eventNamesKey.split(',') : []),
            entityIds: new Set(entityIdsKey ? entityIdsKey.split(',') : []),
            context: Object.fromEntries(JSON.parse(contextKey) as [string, string][]),
            handler: (event) => handlerRef.current(event),
        });

        return () => {
            subscriptions.delete(subscriptionId);
        };
    }, [contextKey, enabled, entityIdsKey, eventNamesKey]);
}
