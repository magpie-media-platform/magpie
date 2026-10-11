import { forwardRef, type ComponentPropsWithoutRef } from 'react';
import { SortAscendingIcon, SortDescendingIcon } from '@phosphor-icons/react';
import type { SortingState } from '@tanstack/react-table';

import { Select, SelectContent, SelectItem, SelectTrigger } from '@/components/ui/Select';
import { useListSorting, type ListSortOption } from '@/hooks/ui/useListSorting';

import './ListSortControl.css';

export interface ListSortControlProps extends Omit<ComponentPropsWithoutRef<'div'>, 'onChange'> {
    sorting: SortingState;
    sortOptions: readonly ListSortOption[];
    onSortingChange: (sorting: SortingState) => void;
    allowUnsorted?: boolean;
    disabled?: boolean;
}

export const ListSortControl = forwardRef<HTMLDivElement, ListSortControlProps>(
    ({ sorting, sortOptions, onSortingChange, allowUnsorted = false, disabled = false, className, ...props }, ref) => {
        const { activeOption, isDescending, selectField, setDirection } = useListSorting(
            sorting,
            sortOptions,
            onSortingChange,
        );

        if (!activeOption) {
            return null;
        }

        const hasSort = !allowUnsorted || sorting.length > 0;
        const classes = ['list-sort-control', className].filter(Boolean).join(' ');

        return (
            <div
                ref={ref}
                className={classes}
                role="group"
                aria-label="Sort list"
                data-component="list-sort-control"
                {...props}
            >
                <Select value={hasSort ? activeOption.id : ''} onValueChange={selectField} disabled={disabled}>
                    <SelectTrigger size="sm" aria-label="Sort by field" data-sort="field">
                        <span className="list-sort-control__label">{hasSort ? activeOption.label : 'Sort by'}</span>
                    </SelectTrigger>
                    <SelectContent>
                        {sortOptions.map((option) => (
                            <SelectItem key={option.id} value={option.id}>
                                {option.label}
                            </SelectItem>
                        ))}
                    </SelectContent>
                </Select>

                <div className="list-sort-control__direction">
                    <button
                        type="button"
                        className="list-sort-control__direction-option"
                        data-state={hasSort && !isDescending ? 'on' : undefined}
                        aria-pressed={hasSort && !isDescending}
                        aria-label="Sort ascending"
                        disabled={disabled || !hasSort}
                        onClick={() => setDirection(false)}
                        data-sort="ascending"
                    >
                        <SortAscendingIcon weight="bold" aria-hidden="true" />
                    </button>
                    <button
                        type="button"
                        className="list-sort-control__direction-option"
                        data-state={hasSort && isDescending ? 'on' : undefined}
                        aria-pressed={hasSort && isDescending}
                        aria-label="Sort descending"
                        disabled={disabled || !hasSort}
                        onClick={() => setDirection(true)}
                        data-sort="descending"
                    >
                        <SortDescendingIcon weight="bold" aria-hidden="true" />
                    </button>
                </div>
            </div>
        );
    },
);

ListSortControl.displayName = 'ListSortControl';

export default ListSortControl;
