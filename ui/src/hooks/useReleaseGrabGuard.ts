import { useCallback, useRef, useState } from 'react';

function addId(ids: ReadonlySet<string>, id: string): ReadonlySet<string> {
    if (ids.has(id)) {
        return ids;
    }

    return new Set([...ids, id]);
}

function removeId(ids: ReadonlySet<string>, id: string): ReadonlySet<string> {
    if (!ids.has(id)) {
        return ids;
    }

    const next = new Set(ids);
    next.delete(id);
    return next;
}

export function useReleaseGrabGuard() {
    const lockedReleaseIdsRef = useRef(new Set<string>());
    const [pendingReleaseIds, setPendingReleaseIds] = useState<ReadonlySet<string>>(() => new Set());
    const [succeededReleaseIds, setSucceededReleaseIds] = useState<ReadonlySet<string>>(() => new Set());

    const tryBeginGrab = useCallback((releaseId: string): boolean => {
        if (lockedReleaseIdsRef.current.has(releaseId)) {
            return false;
        }

        lockedReleaseIdsRef.current.add(releaseId);
        setPendingReleaseIds((current) => addId(current, releaseId));
        return true;
    }, []);

    const markGrabSucceeded = useCallback((releaseId: string) => {
        setPendingReleaseIds((current) => removeId(current, releaseId));
        setSucceededReleaseIds((current) => addId(current, releaseId));
    }, []);

    const markGrabFailed = useCallback((releaseId: string) => {
        lockedReleaseIdsRef.current.delete(releaseId);
        setPendingReleaseIds((current) => removeId(current, releaseId));
        setSucceededReleaseIds((current) => removeId(current, releaseId));
    }, []);

    const clearGrab = useCallback((releaseId: string) => {
        lockedReleaseIdsRef.current.delete(releaseId);
        setPendingReleaseIds((current) => removeId(current, releaseId));
        setSucceededReleaseIds((current) => removeId(current, releaseId));
    }, []);

    return {
        pendingReleaseIds,
        succeededReleaseIds,
        tryBeginGrab,
        markGrabSucceeded,
        markGrabFailed,
        clearGrab,
    };
}
