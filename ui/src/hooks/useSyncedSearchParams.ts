import { useCallback, useRef } from 'react';
import { type Location, useLocation, useNavigate, useSearchParams } from 'react-router-dom';

interface PendingLocationUpdate {
    searchParams: URLSearchParams;
    hash: string;
    state: unknown;
    pushed: boolean;
}

export interface SetSyncedSearchParamsOptions {
    replace?: boolean;
    state?: unknown;
}

type SearchParamsUpdater = URLSearchParams | ((prev: URLSearchParams) => URLSearchParams);

const pendingUpdates = new WeakMap<Location, PendingLocationUpdate>();

export function getSyncedLocationState(location: Location): unknown {
    const pending = pendingUpdates.get(location);
    return pending ? pending.state : location.state;
}

export function useSyncedSearchParams(): [
    URLSearchParams,
    (updater: SearchParamsUpdater, options?: SetSyncedSearchParamsOptions) => Promise<void> | void,
] {
    const location = useLocation();
    const navigate = useNavigate();
    const [searchParams, setRouterSearchParams] = useSearchParams();
    const locationRef = useRef(location);
    locationRef.current = location;

    const setSearchParams = useCallback(
        (updater: SearchParamsUpdater, options?: SetSyncedSearchParamsOptions) => {
            const currentLocation = locationRef.current;
            const existing = pendingUpdates.get(currentLocation);
            const current: PendingLocationUpdate = existing ?? {
                searchParams: new URLSearchParams(currentLocation.search),
                hash: currentLocation.hash,
                state: currentLocation.state,
                pushed: false,
            };
            const nextParams =
                typeof updater === 'function'
                    ? updater(new URLSearchParams(current.searchParams))
                    : new URLSearchParams(updater);
            const nextState = options && options.state !== undefined ? options.state : current.state;
            const requestedReplace = options?.replace ?? false;
            const effectiveReplace = requestedReplace || current.pushed;

            pendingUpdates.set(currentLocation, {
                searchParams: new URLSearchParams(nextParams),
                hash: current.hash,
                state: nextState,
                pushed: current.pushed || !requestedReplace,
            });

            if (!existing) {
                queueMicrotask(() => {
                    pendingUpdates.delete(currentLocation);
                });
            }

            if (current.hash) {
                return navigate(
                    { search: nextParams.toString(), hash: current.hash },
                    { replace: effectiveReplace, state: nextState },
                );
            }

            setRouterSearchParams(nextParams, { replace: effectiveReplace, state: nextState });
        },
        [navigate, setRouterSearchParams],
    );

    return [searchParams, setSearchParams];
}
