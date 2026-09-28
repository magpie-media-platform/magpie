import { forwardRef, type ComponentPropsWithoutRef, type ReactNode } from 'react';
import { SortAscendingIcon, SortDescendingIcon } from '@phosphor-icons/react';
import type { SortingState } from '@tanstack/react-table';

import { useListSorting } from '@/hooks/ui/useListSorting';

import { SelectModeToggle } from '../BulkActionsToolbar';
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
        const { activeOption, isDescending, selectField, toggleDirection } = useListSorting(
            sorting,
            sortOptions,
            onSortingChange,
        );

        const hasSort = !allowUnsorted || sorting.length > 0;
        const classes = ['mobile-list-toolbar', className].filter(Boolean).join(' ');
        const DirectionIcon = isDescending ? SortDescendingIcon : SortAscendingIcon;

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

                {filterActions && (
                    <div className="mobile-list-toolbar__controls">
                        <div className="mobile-list-toolbar__filter-actions">{filterActions}</div>
                        {activeOption && (
                            <div className="mobile-list-toolbar__sorting" role="group" aria-label="Sort list">
                                <Select
                                    value={hasSort ? activeOption.id : ''}
                                    onValueChange={selectField}
                                    disabled={disabled}
                                >
                                    <SelectTrigger size="sm" aria-label="Sort by field" data-sort="field">
                                        {hasSort ? activeOption.label : 'Sort by'}
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
                                    disabled={disabled || !hasSort}
                                    aria-label={`Sort ${isDescending ? 'ascending' : 'descending'}`}
                                    aria-description={`Current direction: ${isDescending ? 'descending' : 'ascending'}`}
                                    title={isDescending ? 'Descending' : 'Ascending'}
                                    data-sort="direction"
                                    data-direction={isDescending ? 'desc' : 'asc'}
                                >
                                    <DirectionIcon weight="bold" aria-hidden="true" />
                                </Button>
                            </div>
                        )}
                    </div>
                )}

                {!filterActions && activeOption && (
                    <div className="mobile-list-toolbar__sorting" role="group" aria-label="Sort list">
                        <Select value={hasSort ? activeOption.id : ''} onValueChange={selectField} disabled={disabled}>
                            <SelectTrigger size="sm" aria-label="Sort by field" data-sort="field">
                                {hasSort ? activeOption.label : 'Sort by'}
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
                            disabled={disabled || !hasSort}
                            aria-label={`Sort ${isDescending ? 'ascending' : 'descending'}`}
                            aria-description={`Current direction: ${isDescending ? 'descending' : 'ascending'}`}
                            title={isDescending ? 'Descending' : 'Ascending'}
                            data-sort="direction"
                            data-direction={isDescending ? 'desc' : 'asc'}
                        >
                            <DirectionIcon weight="bold" aria-hidden="true" />
                        </Button>
                    </div>
                )}
            </div>
        );
    },
);

MobileListToolbar.displayName = 'MobileListToolbar';

export default MobileListToolbar;
