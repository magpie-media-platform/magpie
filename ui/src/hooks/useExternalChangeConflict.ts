import { useCallback, useEffect, useRef, useState } from 'react';

import { useEventStream } from './useEventStream';

type ExternalChangeConflictState = 'stale' | 'deleted' | null;

export function useExternalChangeConflict({
    dirty,
    entityId,
    eventNames,
    deletedEventNames,
    contextKeys,
    enabled = true,
}: {
    dirty: boolean;
    entityId: string | null | undefined;
    eventNames: readonly string[];
    deletedEventNames: readonly string[];
    contextKeys: readonly string[];
    enabled?: boolean;
}) {
    const [state, setState] = useState<ExternalChangeConflictState>(null);
    const ownSaveInFlightRef = useRef(false);
    const eventObservedDuringSaveRef = useRef(false);

    useEffect(() => {
        setState(null);
        ownSaveInFlightRef.current = false;
        eventObservedDuringSaveRef.current = false;
    }, [entityId, enabled]);

    useEffect(() => {
        if (!dirty) {
            setState((current) => (current === 'stale' ? null : current));
        }
    }, [dirty]);

    useEventStream(
        (event) => {
            const matchesEntity = contextKeys.some((key) => event.context[key] === entityId);
            if (!matchesEntity) {
                return;
            }

            const deleted = deletedEventNames.includes(event.name);
            if (!deleted && !dirty) {
                return;
            }

            if (ownSaveInFlightRef.current) {
                eventObservedDuringSaveRef.current = true;
            }

            setState(deleted ? 'deleted' : 'stale');
        },
        {
            eventName: [...eventNames],
            enabled: enabled && !!entityId,
        },
    );

    const beginOwnSave = useCallback(() => {
        eventObservedDuringSaveRef.current = false;
        ownSaveInFlightRef.current = true;
    }, []);

    const endOwnSave = useCallback((saved: boolean) => {
        const saveWasInFlight = ownSaveInFlightRef.current;
        ownSaveInFlightRef.current = false;
        if (saved && saveWasInFlight && !eventObservedDuringSaveRef.current) {
            setState(null);
        }
        eventObservedDuringSaveRef.current = false;
    }, []);

    const clearConflict = useCallback(() => {
        ownSaveInFlightRef.current = false;
        eventObservedDuringSaveRef.current = false;
        setState(null);
    }, []);

    return { state, beginOwnSave, endOwnSave, clearConflict };
}
