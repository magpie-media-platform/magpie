import { useCallback, useMemo } from 'react';

import { useBulkSelection } from '@/hooks/useBulkSelection';
import { type ContentListItem } from '@/types';

interface UseLibrarySelectionArgs {
    pageRowIds: string[];
    totalItems: number;
    contentItems: ContentListItem[];
    resetKey: string;
}

export function useLibrarySelection({ pageRowIds, totalItems, contentItems, resetKey }: UseLibrarySelectionArgs) {
    const selection = useBulkSelection({ pageRowIds, totalCount: totalItems, resetKey });

    const selectedCount = selection.selectedCount;
    const hasSelection = selection.hasSelection;

    const selectedRecords = useMemo(
        () => contentItems.filter((item) => selection.isRowChecked(item.id)),
        [contentItems, selection],
    );

    const selectedItems = useMemo(
        () =>
            selectedRecords.map((item) => ({
                id: item.id,
                parentId: item.parentId,
                title: item.title,
                tags: item.tags,
            })),
        [selectedRecords],
    );

    const handleClearSelection = useCallback(() => {
        selection.clear();
    }, [selection]);

    return {
        selection,
        selectedItems,
        selectedCount,
        hasSelection,
        handleClearSelection,
    };
}
