import { forwardRef, type ComponentPropsWithoutRef, type ReactNode } from 'react';
import { CheckSquareIcon, SortAscendingIcon, SortDescendingIcon, XIcon } from '@phosphor-icons/react';
import type { SortingState } from '@tanstack/react-table';

import { useListSorting } from '@/hooks/ui/useListSorting';

import { Button } from '../Button';
import { Select, SelectContent, SelectItem, SelectTrigger } from '../Select';

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
    disabled?: boolean;
}

export const MobileListToolbar = forwardRef<HTMLDivElement, MobileListToolbarProps>(
    (
        { sorting, sortOptions, onSortingChange, selection, filterActions, disabled = false, className, ...props },
        ref,
    ) => {
        const { activeOption, isDescending, selectField, toggleDirection } = useListSorting(
            sorting,
            sortOptions,
            onSortingChange,
        );

        if (!activeOption) {
            return null;
        }

        const classes = ['mobile-list-toolbar', className].filter(Boolean).join(' ');
        const DirectionIcon = isDescending ? SortDescendingIcon : SortAscendingIcon;

        return (
            <div ref={ref} className={classes} data-component="mobile-list-toolbar" {...props}>
                {selection && (
                    <Button
                        variant="default"
                        size="sm"
                        className="mobile-list-toolbar__selection"
                        onClick={selection.onToggle}
                        disabled={selection.disabled}
                        data-action={selection.active ? 'exit-select-mode' : 'enter-select-mode'}
                    >
                        {selection.active ? (
                            <XIcon weight="bold" aria-hidden="true" />
                        ) : (
                            <CheckSquareIcon weight="bold" aria-hidden="true" />
                        )}
                        {selection.active ? 'Stop selecting' : 'Select'}
                    </Button>
                )}

                {filterActions && <div className="mobile-list-toolbar__filter-actions">{filterActions}</div>}

                <div className="mobile-list-toolbar__sorting" role="group" aria-label="Sort list">
                    <Select value={activeOption.id} onValueChange={selectField} disabled={disabled}>
                        <SelectTrigger size="sm" aria-label="Sort by field" data-sort="field">
                            {activeOption.label}
                        </SelectTrigger>
                        <SelectContent>
                            {sortOptions.map((option) => (
                                <SelectItem key={option.id} value={option.id}>
                                    {option.label}
                                </SelectItem>
                            ))}
                        </SelectContent>
                    </Select>

                    <Button
                        variant="ghost"
                        size="sm"
                        className="mobile-list-toolbar__direction"
                        onClick={toggleDirection}
                        disabled={disabled}
                        aria-label={`Sort ${isDescending ? 'ascending' : 'descending'}`}
                        title={isDescending ? 'Descending' : 'Ascending'}
                        data-sort="direction"
                        data-direction={isDescending ? 'desc' : 'asc'}
                    >
                        <DirectionIcon weight="bold" aria-hidden="true" />
                    </Button>
                </div>
            </div>
        );
    },
);

MobileListToolbar.displayName = 'MobileListToolbar';

export default MobileListToolbar;
