import { useCallback, useMemo, useRef } from 'react';
import { useSearchParams } from 'react-router-dom';
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

    setPage: (page: number) => void;
}

function encodeSort(sorting: SortingState): string | null {
    const [first] = sorting;
    if (!first) {
        return null;
    }
    return `${first.id}:${first.desc ? 'desc' : 'asc'}`;
}

function decodeSort(raw: string | null, sortKeys: readonly string[]): SortingState | null {
    if (!raw) {
        return null;
    }
    const [id, dir] = raw.split(':');
    if (!sortKeys.includes(id)) {
        return null;
    }
    return [{ id, desc: dir === 'desc' }];
}

function sameSort(a: SortingState, b: SortingState): boolean {
    return encodeSort(a) === encodeSort(b);
}

export function useUrlTableState<TFilters extends Record<string, string>>(
    config: UrlTableStateConfig<TFilters>,
): UrlTableState<TFilters> {
    const { pageParam = 'page', sortParam = 'sort' } = config;

    const [searchParams, setSearchParams] = useSearchParams();

    const configRef = useRef(config);
    configRef.current = config;

    const sorting = useMemo<SortingState>(
        () => decodeSort(searchParams.get(sortParam), configRef.current.sortKeys) ?? configRef.current.defaultSort,
        [searchParams, sortParam],
    );

    const [filters, setFilter] = useUrlFilterState(config.defaultFilters);

    const page = useMemo<number>(() => {
        const raw = Number(searchParams.get(pageParam));
        return Number.isInteger(raw) && raw >= 1 ? raw : 1;
    }, [searchParams, pageParam]);

    const setSorting = useCallback(
        (next: SortingState) => {
            const { defaultSort } = configRef.current;
            setSearchParams(
                (prev) => {
                    const params = new URLSearchParams(prev);
                    const encoded = encodeSort(next);
                    if (!encoded || sameSort(next, defaultSort)) {
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
        (next: number) => {
            setSearchParams(
                (prev) => {
                    const params = new URLSearchParams(prev);
                    if (next <= 1) {
                        params.delete(pageParam);
                    } else {
                        params.set(pageParam, String(next));
                    }
                    return params;
                },
                { replace: true },
            );
        },
        [setSearchParams, pageParam],
    );

    return { sorting, filters, page, setSorting, setFilters, setPage };
}
