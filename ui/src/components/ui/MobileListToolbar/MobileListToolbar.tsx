import { forwardRef, type ComponentPropsWithoutRef, type ReactNode } from 'react';
import type { SortingState } from '@tanstack/react-table';

import { SelectModeToggle } from '../BulkActionsToolbar';
import { ListSortControl } from '../ListSortControl';

import './MobileListToolbar.css';

export interface MobileListSortOption {
    id: string;
    label: string;
    initialDirection?: 'asc' | 'desc';
}

export interface MobileListSelectionControl {
    active: boolean;
    onToggle: () => void;
    disabled?: boolean;
}

export interface MobileListToolbarProps extends Omit<ComponentPropsWithoutRef<'div'>, 'onChange'> {
    sorting: SortingState;
    sortOptions: readonly MobileListSortOption[];
    onSortingChange: (sorting: SortingState) => void;
    selection?: MobileListSelectionControl;
    filterActions?: ReactNode;
    allowUnsorted?: boolean;
    disabled?: boolean;
}

export const MobileListToolbar = forwardRef<HTMLDivElement, MobileListToolbarProps>(
    (
        {
            sorting,
            sortOptions,
            onSortingChange,
            selection,
            filterActions,
            disabled = false,
            allowUnsorted = false,
            className,
            ...props
        },
        ref,
    ) => {
        const classes = ['mobile-list-toolbar', className].filter(Boolean).join(' ');
        const sortControl = (
            <ListSortControl
                className="mobile-list-toolbar__sorting"
                sorting={sorting}
                sortOptions={sortOptions}
                onSortingChange={onSortingChange}
                allowUnsorted={allowUnsorted}
                disabled={disabled}
            />
        );

        return (
            <div ref={ref} className={classes} data-component="mobile-list-toolbar" {...props}>
                {selection && (
                    <SelectModeToggle
                        active={selection.active}
                        onToggle={selection.onToggle}
                        disabled={selection.disabled}
                        className="mobile-list-toolbar__selection"
                    />
                )}

                {filterActions ? (
                    <div className="mobile-list-toolbar__controls">
                        <div className="mobile-list-toolbar__filter-actions">{filterActions}</div>
                        {sortControl}
                    </div>
                ) : (
                    sortControl
                )}
            </div>
        );
    },
);

MobileListToolbar.displayName = 'MobileListToolbar';

export default MobileListToolbar;
