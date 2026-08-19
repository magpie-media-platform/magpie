import { forwardRef, type ComponentPropsWithoutRef } from 'react';
import { CheckSquareIcon, SortAscendingIcon, SortDescendingIcon, XIcon } from '@phosphor-icons/react';
import type { SortingState } from '@tanstack/react-table';

import { Button } from '@/components/ui/Button';
import { Select, SelectContent, SelectItem, SelectTrigger } from '@/components/ui/Select';
import type { MobileListSelectionControl, MobileListSortOption } from '@/components/ui/MobileListToolbar';
import { useListSorting } from '@/hooks/ui/useListSorting';

export interface LibraryGalleryToolbarProps extends Omit<ComponentPropsWithoutRef<'div'>, 'onChange'> {
    sorting: SortingState;
    sortOptions: readonly MobileListSortOption[];
    onSortingChange: (sorting: SortingState) => void;
    selection?: MobileListSelectionControl;
    selectedCount?: number;
    disabled?: boolean;
}

export const LibraryGalleryToolbar = forwardRef<HTMLDivElement, LibraryGalleryToolbarProps>(
    (
        { sorting, sortOptions, onSortingChange, selection, selectedCount = 0, disabled = false, className, ...props },
        ref,
    ) => {
        const { activeOption, isDescending, selectField, setDirection } = useListSorting(
            sorting,
            sortOptions,
            onSortingChange,
        );

        if (!activeOption) {
            return null;
        }

        const classes = ['library-gallery-toolbar', className].filter(Boolean).join(' ');

        return (
            <div ref={ref} className={classes} data-component="library-gallery-toolbar" {...props}>
                <div className="library-gallery-toolbar__group">
                    {selection && (
                        <Button
                            variant="default"
                            size="sm"
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
                    {selection?.active && (
                        <span className="library-gallery-toolbar__count" data-testid="library-gallery-selection-count">
                            {selectedCount} selected
                        </span>
                    )}
                </div>

                <div className="library-gallery-toolbar__group" role="group" aria-label="Sort gallery">
                    <span className="library-gallery-toolbar__label">Sort</span>
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

                    <div className="library-gallery-toolbar__direction">
                        <button
                            type="button"
                            className="library-gallery-toolbar__direction-option"
                            data-state={isDescending ? undefined : 'on'}
                            aria-pressed={!isDescending}
                            aria-label="Sort ascending"
                            disabled={disabled}
                            onClick={() => setDirection(false)}
                            data-sort="ascending"
                        >
                            <SortAscendingIcon weight="bold" aria-hidden="true" />
                        </button>
                        <button
                            type="button"
                            className="library-gallery-toolbar__direction-option"
                            data-state={isDescending ? 'on' : undefined}
                            aria-pressed={isDescending}
                            aria-label="Sort descending"
                            disabled={disabled}
                            onClick={() => setDirection(true)}
                            data-sort="descending"
                        >
                            <SortDescendingIcon weight="bold" aria-hidden="true" />
                        </button>
                    </div>
                </div>
            </div>
        );
    },
);

LibraryGalleryToolbar.displayName = 'LibraryGalleryToolbar';

export default LibraryGalleryToolbar;
