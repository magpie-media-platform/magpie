import { useCallback, useEffect } from 'react';

import { usePersistentState } from './usePersistentState';
import { useSyncedSearchParams } from './useSyncedSearchParams';

export function useContentView(key: string) {
    const [storedView, setStoredView] = usePersistentState<string>(key, 'list');
    const [searchParams, setSearchParams] = useSyncedSearchParams();
    const urlView = searchParams.get('view');
    const view: 'list' | 'gallery' =
        urlView === 'list' || urlView === 'gallery' ? urlView : storedView === 'gallery' ? 'gallery' : 'list';

    useEffect(() => {
        if (urlView === 'list' || urlView === 'gallery') {
            return;
        }

        void setSearchParams(
            (prev) => {
                const next = new URLSearchParams(prev);
                next.set('view', view);
                return next;
            },
            { replace: true },
        );
    }, [urlView, view, setSearchParams]);

    const setView = useCallback(
        async (value: string) => {
            const nextView = value === 'gallery' ? 'gallery' : 'list';
            if (nextView === view) {
                return;
            }

            setStoredView(nextView);
            await setSearchParams((prev) => {
                const next = new URLSearchParams(prev);
                next.set('view', nextView);
                return next;
            });
        },
        [view, setSearchParams, setStoredView],
    );

    return { view, setView };
}
