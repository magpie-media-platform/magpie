import { useCallback } from 'react';
import type { SortingState } from '@tanstack/react-table';

export interface ListSortOption {
    id: string;
    label: string;
    initialDirection?: 'asc' | 'desc';
}

export interface ListSorting {
    activeOption: ListSortOption | undefined;
    isDescending: boolean;
    selectField: (id: string) => void;
    setDirection: (desc: boolean) => void;
    toggleDirection: () => void;
}

/** Resolves the active sort field and direction from table sorting state. */
export function useListSorting(
    sorting: SortingState,
    sortOptions: readonly ListSortOption[],
    onSortingChange: (sorting: SortingState) => void,
): ListSorting {
    const activeSort = sorting[0];
    const activeOption = sortOptions.find((option) => option.id === activeSort?.id) ?? sortOptions[0];
    const isDescending =
        activeSort?.id === activeOption?.id ? Boolean(activeSort.desc) : activeOption?.initialDirection === 'desc';

    const selectField = useCallback(
        (id: string) => {
            const option = sortOptions.find((candidate) => candidate.id === id);
            if (option) {
                onSortingChange([{ id: option.id, desc: option.initialDirection === 'desc' }]);
            }
        },
        [onSortingChange, sortOptions],
    );

    const setDirection = useCallback(
        (desc: boolean) => {
            if (activeOption) {
                onSortingChange([{ id: activeOption.id, desc }]);
            }
        },
        [activeOption, onSortingChange],
    );

    const toggleDirection = useCallback(() => {
        setDirection(!isDescending);
    }, [isDescending, setDirection]);

    return { activeOption, isDescending, selectField, setDirection, toggleDirection };
}
