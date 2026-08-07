import { useCallback, useMemo } from 'react';
import { type RowSelectionState } from '@tanstack/react-table';

import { useBulkSelection } from '@/hooks/useBulkSelection';
import { type ContentListItem } from '@/types';

interface UseLibrarySelectionArgs {
    pageRowIds: string[];
    totalItems: number;
    contentItems: ContentListItem[];
}

export function useLibrarySelection({ pageRowIds, totalItems, contentItems }: UseLibrarySelectionArgs) {
    const selection = useBulkSelection({ pageRowIds, totalCount: totalItems });

    const selectedCount = selection.selectedCount;
    const hasSelection = selection.hasSelection;

    const displaySelectedCount = selectedCount;

    const handleRowSelectionChange = useCallback(
        (updater: RowSelectionState | ((prev: RowSelectionState) => RowSelectionState)) => {
            const current = selection.rowSelection;
            const next = typeof updater === 'function' ? updater(current) : updater;

            const changed = pageRowIds.filter((id) => selection.isRowChecked(id) !== Boolean(next[id]));
            if (changed.length === 0) {
                return;
            }

            if (changed.length > 1) {
                selection.toggleHeader(Boolean(next[changed[0]]));
                return;
            }

            const id = changed[0];
            selection.toggleRow(id, Boolean(next[id]));
        },
        [pageRowIds, selection],
    );

    const selectedRecords = useMemo(
        () => contentItems.filter((item) => selection.isRowChecked(item.id)),
        [contentItems, selection],
    );

    const selectedItems = useMemo(
        () => selectedRecords.map((item) => ({ id: item.id, parentId: item.parentId, title: item.title })),
        [selectedRecords],
    );

    const handleClearSelection = useCallback(() => {
        selection.clear();
    }, [selection]);

    return {
        selection,
        selectedPageCount: selectedRecords.length,
        selectedItems,
        selectedCount,
        hasSelection,
        displaySelectedCount,
        handleClearSelection,
        handleRowSelectionChange,
    };
}
