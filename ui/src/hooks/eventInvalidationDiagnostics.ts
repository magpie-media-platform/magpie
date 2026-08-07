type EventInvalidationDiagnosticType = 'decision' | 'event' | 'subscription' | 'target';

interface EventInvalidationDiagnostic {
    active?: boolean;
    eventName?: string;
    mounted: boolean;
    owner: string;
    queryHash?: string;
    reason?: string;
    refetched?: boolean;
    subscriptionCount: number;
    timestamp: number;
    type: EventInvalidationDiagnosticType;
}

const MAX_DIAGNOSTICS = 500;
const diagnostics: EventInvalidationDiagnostic[] = [];
const listeners = new Set<(diagnostic: EventInvalidationDiagnostic) => void>();
let activeSubscriptionCount = 0;

export function emitEventInvalidationDiagnostic(diagnostic: Omit<EventInvalidationDiagnostic, 'timestamp'>): void {
    if (!import.meta.env.DEV && listeners.size === 0) {
        return;
    }

    const entry = { ...diagnostic, timestamp: Date.now() };
    diagnostics.push(entry);
    if (diagnostics.length > MAX_DIAGNOSTICS) {
        diagnostics.splice(0, diagnostics.length - MAX_DIAGNOSTICS);
    }
    for (const listener of listeners) {
        listener(entry);
    }
}

export function getEventInvalidationDiagnostics(): readonly EventInvalidationDiagnostic[] {
    return [...diagnostics];
}

export function clearEventInvalidationDiagnostics(): void {
    diagnostics.length = 0;
}

export function getDiagnosticSubscriptionCount(): number {
    return activeSubscriptionCount;
}

export function setDiagnosticSubscriptionCount(count: number): void {
    activeSubscriptionCount = count;
}

export function subscribeToEventInvalidationDiagnostics(
    listener: (diagnostic: EventInvalidationDiagnostic) => void,
): () => void {
    listeners.add(listener);
    return () => listeners.delete(listener);
}
