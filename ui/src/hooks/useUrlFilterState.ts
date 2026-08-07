import { useMemo, useCallback, useRef } from 'react';
import { useSearchParams } from 'react-router-dom';

export interface SetFilterOptions {
    replace?: boolean;
}

export function useUrlFilterState<T extends Record<string, string>>(
    defaults: T,
): [T, (updates: Partial<T>, options?: SetFilterOptions) => void] {
    const [searchParams, setSearchParams] = useSearchParams();

    const defaultsRef = useRef(defaults);
    defaultsRef.current = defaults;

    const state = useMemo<T>(() => {
        const current = defaultsRef.current;
        const result = { ...current } as Record<string, string>;
        for (const key of Object.keys(current)) {
            const val = searchParams.get(key);
            if (val !== null) {
                result[key] = val;
            }
        }
        return result as T;
    }, [searchParams]);

    const setFilter = useCallback(
        (updates: Partial<T>, options?: SetFilterOptions) => {
            const current = defaultsRef.current;
            setSearchParams(
                (prev) => {
                    const next = new URLSearchParams(prev);
                    for (const [key, val] of Object.entries(updates) as [string, string | undefined][]) {
                        if (!val || val === (current as Record<string, string>)[key]) {
                            next.delete(key);
                        } else {
                            next.set(key, val);
                        }
                    }
                    return next;
                },
                { replace: options?.replace ?? false },
            );
        },
        [setSearchParams],
    );

    return [state, setFilter];
}
