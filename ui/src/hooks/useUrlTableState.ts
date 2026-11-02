import { useCallback, useMemo, useRef } from 'react';
import { useSyncedSearchParams } from '@/hooks/useSyncedSearchParams';
import { useUrlFilterState } from '@/hooks/useUrlFilterState';
import type { SortingState } from '@tanstack/react-table';

interface UrlTableStateConfig<TFilters extends Record<string, string>> {
    defaultSort: SortingState;

    defaultFilters: TFilters;

    sortKeys: readonly string[];

    pageParam?: string;

    sortParam?: string;
}

interface UrlTableState<TFilters extends Record<string, string>> {
    sorting: SortingState;
    filters: TFilters;
    page: number;

    setSorting: (sorting: SortingState) => void;

    setFilters: (updates: Partial<TFilters>) => void;

    setPage: (page: number, options?: { replace?: boolean }) => void;
}

function encodeSort(sorting: SortingState): string {
    return sorting.map(({ id, desc }) => `${id}:${desc ? 'desc' : 'asc'}`).join(',') || 'none';
}

function parseEncodedSort(encoded: string): SortingState {
    if (!encoded || encoded === 'none') {
        return [];
    }
    return encoded.split(',').map((entry) => {
        const [id, dir] = entry.split(':');
        return { id, desc: dir === 'desc' };
    });
}

function decodeSort(raw: string | null, sortKeys: readonly string[]): SortingState | null {
    if (!raw) {
        return null;
    }
    if (raw === 'none') {
        return [];
    }
    const sorting: SortingState = [];
    for (const entry of raw.split(',')) {
        const [id, dir] = entry.split(':');
        if (!sortKeys.includes(id)) {
            return null;
        }
        sorting.push({ id, desc: dir === 'desc' });
    }
    return sorting;
}

function sameSort(a: SortingState, b: SortingState): boolean {
    return encodeSort(a) === encodeSort(b);
}

export function useUrlTableState<TFilters extends Record<string, string>>(
    config: UrlTableStateConfig<TFilters>,
): UrlTableState<TFilters> {
    const { pageParam = 'page', sortParam = 'sort' } = config;

    const [searchParams, setSearchParams] = useSyncedSearchParams();

    const configRef = useRef(config);
    configRef.current = config;

    const defaultSortKey = encodeSort(config.defaultSort);
    const defaultSort = useMemo<SortingState>(() => parseEncodedSort(defaultSortKey), [defaultSortKey]);

    const sorting = useMemo<SortingState>(
        () => decodeSort(searchParams.get(sortParam), configRef.current.sortKeys) ?? defaultSort,
        [searchParams, sortParam, defaultSort],
    );

    const [filters, setFilter] = useUrlFilterState(config.defaultFilters);

    const page = useMemo<number>(() => {
        const raw = Number(searchParams.get(pageParam));
        return Number.isInteger(raw) && raw >= 1 ? raw : 1;
    }, [searchParams, pageParam]);

    const setSorting = useCallback(
        (next: SortingState) => {
            const { defaultSort: currentDefaultSort } = configRef.current;
            void setSearchParams(
                (prev) => {
                    const params = new URLSearchParams(prev);
                    const encoded = encodeSort(next);
                    if (sameSort(next, currentDefaultSort)) {
                        params.delete(sortParam);
                    } else {
                        params.set(sortParam, encoded);
                    }
                    params.delete(pageParam);
                    return params;
                },
                { replace: false },
            );
        },
        [setSearchParams, sortParam, pageParam],
    );

    const setFilters = useCallback(
        (updates: Partial<TFilters>) => setFilter(updates, { resetParams: [pageParam] }),
        [setFilter, pageParam],
    );

    const setPage = useCallback(
        (next: number, options?: { replace?: boolean }) => {
            void setSearchParams(
                (prev) => {
                    const params = new URLSearchParams(prev);
                    if (next <= 1) {
                        params.delete(pageParam);
                    } else {
                        params.set(pageParam, String(next));
                    }
                    return params;
                },
                { replace: options?.replace ?? false },
            );
        },
        [setSearchParams, pageParam],
    );

    return { sorting, filters, page, setSorting, setFilters, setPage };
}
