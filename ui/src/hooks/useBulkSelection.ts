import { useCallback, useMemo, useState } from 'react';
import type { RowSelectionState } from '@tanstack/react-table';

type BulkSelectionResolution = { mode: 'ids'; ids: string[] } | { mode: 'all'; excludeIds: string[] };

export interface BulkSelection {
    rowSelection: RowSelectionState;
    headerChecked: boolean | 'indeterminate';
    toggleHeader: (checked: boolean) => void;
    isRowChecked: (id: string) => boolean;
    toggleRow: (id: string, checked: boolean) => void;
    allPages: boolean;
    selectAllAcrossPages: () => void;
    showAcrossPagesPrompt: boolean;
    selectedCount: number;
    hasSelection: boolean;
    selectedIds: string[];
    clear: () => void;
    resolve: () => BulkSelectionResolution;
}

interface UseBulkSelectionArgs {
    pageRowIds: string[];
    totalCount: number;
}

function withKey(map: Record<string, true>, id: string, present: boolean): Record<string, true> {
    const next = { ...map };
    if (present) {
        next[id] = true;
    } else {
        delete next[id];
    }
    return next;
}

export function useBulkSelection({ pageRowIds, totalCount }: UseBulkSelectionArgs): BulkSelection {
    const [picks, setPicks] = useState<Record<string, true>>({});
    const [allPages, setAllPages] = useState(false);
    const [excluded, setExcluded] = useState<Record<string, true>>({});

    const isRowChecked = useCallback(
        (id: string) => (allPages ? !excluded[id] : Boolean(picks[id])),
        [allPages, excluded, picks],
    );

    const clear = useCallback(() => {
        setAllPages(false);
        setPicks({});
        setExcluded({});
    }, []);

    const toggleRow = useCallback(
        (id: string, checked: boolean) => {
            if (allPages) {
                setExcluded((prev) => withKey(prev, id, !checked));
            } else {
                setPicks((prev) => withKey(prev, id, checked));
            }
        },
        [allPages],
    );

    const toggleHeader = useCallback(
        (checked: boolean) => {
            if (allPages) {
                if (checked) {
                    setExcluded((prev) => {
                        const next = { ...prev };
                        for (const id of pageRowIds) {
                            delete next[id];
                        }
                        return next;
                    });
                } else {
                    clear();
                }
            } else {
                setPicks((prev) => {
                    const next = { ...prev };
                    for (const id of pageRowIds) {
                        if (checked) {
                            next[id] = true;
                        } else {
                            delete next[id];
                        }
                    }
                    return next;
                });
            }
        },
        [allPages, pageRowIds, clear],
    );

    const selectAllAcrossPages = useCallback(() => {
        setAllPages(true);
        setExcluded({});
    }, []);

    const pageAllChecked = pageRowIds.length > 0 && pageRowIds.every(isRowChecked);
    const pageSomeChecked = pageRowIds.some(isRowChecked);
    const headerChecked: boolean | 'indeterminate' = pageAllChecked ? true : pageSomeChecked ? 'indeterminate' : false;

    const excludedCount = Object.keys(excluded).length;
    const selectedCount = allPages ? Math.max(0, totalCount - excludedCount) : Object.keys(picks).length;
    const hasSelection = selectedCount > 0;
    const showAcrossPagesPrompt = !allPages && pageAllChecked && totalCount > pageRowIds.length;

    const rowSelection = useMemo(() => {
        const map: RowSelectionState = {};
        for (const id of pageRowIds) {
            if (isRowChecked(id)) {
                map[id] = true;
            }
        }
        return map;
    }, [pageRowIds, isRowChecked]);

    const selectedIds = useMemo(() => (allPages ? [] : Object.keys(picks)), [allPages, picks]);

    const resolve = useCallback(
        (): BulkSelectionResolution =>
            allPages ? { mode: 'all', excludeIds: Object.keys(excluded) } : { mode: 'ids', ids: Object.keys(picks) },
        [allPages, excluded, picks],
    );

    return useMemo(
        () => ({
            rowSelection,
            headerChecked,
            toggleHeader,
            isRowChecked,
            toggleRow,
            allPages,
            selectAllAcrossPages,
            showAcrossPagesPrompt,
            selectedCount,
            hasSelection,
            selectedIds,
            clear,
            resolve,
        }),
        [
            rowSelection,
            headerChecked,
            toggleHeader,
            isRowChecked,
            toggleRow,
            allPages,
            selectAllAcrossPages,
            showAcrossPagesPrompt,
            selectedCount,
            hasSelection,
            selectedIds,
            clear,
            resolve,
        ],
    );
}
