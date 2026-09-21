import { useEffect, useMemo, useRef } from 'react';

import { ENTITY_ID_CONTEXT_KEYS } from './entityIdContextKeys';

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
    owner?: string;
}

interface EventSubscription {
    eventNames: Set<string>;
    entityIds: Set<string>;
    context: Readonly<Record<string, string>>;
    handler: (event: EventStreamPayload) => void;
    owner: string;
}

const API_BASE = (() => {
    try {
        const env = (import.meta as ImportMeta & { env?: Record<string, string | undefined> }).env;
        return env?.VITE_API_URL ?? '/api';
    } catch {
        return '/api';
    }
})();

const NO_ENTITY_IDS: readonly string[] = [];

const subscriptions = new Map<number, EventSubscription>();
let nextSubscriptionId = 0;
let source: EventSource | null = null;
let hasConnected = false;
let disconnected = false;

interface ActiveEventSubscription {
    context: Readonly<Record<string, string>>;
    entityIds: readonly string[];
    eventNames: readonly string[];
    id: number;
    owner: string;
}

export interface MagpieEventDiagnostics {
    getActiveEventSubscriptionCount: typeof getActiveEventSubscriptionCount;
    getActiveEventSubscriptions: typeof getActiveEventSubscriptions;
}

export function getActiveEventSubscriptionCount(): number {
    return subscriptions.size;
}

export function getActiveEventSubscriptions(): readonly ActiveEventSubscription[] {
    return [...subscriptions.entries()].map(([id, subscription]) => ({
        context: subscription.context,
        entityIds: [...subscription.entityIds],
        eventNames: [...subscription.eventNames],
        id,
        owner: subscription.owner,
    }));
}

if (import.meta.env.DEV && typeof window !== 'undefined') {
    (window as typeof window & { __magpieEventDiagnostics?: MagpieEventDiagnostics }).__magpieEventDiagnostics = {
        getActiveEventSubscriptionCount,
        getActiveEventSubscriptions,
    };
}

function dispatchMessage(message: MessageEvent): void {
    let payload: EventStreamPayload;
    try {
        payload = JSON.parse(message.data) as EventStreamPayload;
    } catch {
        if (import.meta.env.DEV) {
            console.warn('useEventStream: failed to parse SSE payload', message.data);
        }
        return;
    }

    dispatchPayload(payload);
}

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

function openEventStream(): void {
    if (source !== null || typeof EventSource === 'undefined') {
        return;
    }

    const url = new URL(`${API_BASE}/events/stream`, window.location.origin);
    source = new EventSource(url.toString(), { withCredentials: true });
    source.onmessage = dispatchMessage;
    source.onopen = () => {
        if (hasConnected && disconnected) {
            dispatchPayload({ name: 'stream.reconnected', context: {} });
        }
        hasConnected = true;
        disconnected = false;
    };
    source.onerror = () => {
        disconnected = true;
        if (import.meta.env.DEV) {
            console.warn('useEventStream: SSE connection error; EventSource will attempt to reconnect');
        }
    };
}

function closeEventStream(): void {
    source?.close();
    source = null;
    hasConnected = false;
    disconnected = false;
}

export function useGlobalEventStreamConnection(active: boolean): void {
    useEffect(() => {
        if (!active) {
            closeEventStream();
            return;
        }

        openEventStream();
        return closeEventStream;
    }, [active]);
}

export function useEventStream(
    onEvent: (event: EventStreamPayload) => void,
    options: UseEventStreamOptions = {},
): void {
    const {
        eventName,
        entityId,
        entityIds = NO_ENTITY_IDS,
        context = {},
        enabled = true,
        owner = 'event-stream',
    } = options;
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
            owner,
        });

        return () => {
            subscriptions.delete(subscriptionId);
        };
    }, [contextKey, enabled, entityIdsKey, eventNamesKey, owner]);
}
