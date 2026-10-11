import { forwardRef, type ComponentPropsWithoutRef } from 'react';
import type { SortingState } from '@tanstack/react-table';

import { SelectModeToggle } from '@/components/ui/BulkActionsToolbar';
import { ListSortControl } from '@/components/ui/ListSortControl';
import type { MobileListSelectionControl, MobileListSortOption } from '@/components/ui/MobileListToolbar';

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
        const classes = ['library-gallery-toolbar', className].filter(Boolean).join(' ');

        return (
            <div ref={ref} className={classes} data-component="library-gallery-toolbar" {...props}>
                <div className="library-gallery-toolbar__group">
                    {selection && (
                        <SelectModeToggle
                            active={selection.active}
                            onToggle={selection.onToggle}
                            disabled={selection.disabled}
                            mobileOnly={false}
                        />
                    )}
                    {selection?.active && (
                        <span className="library-gallery-toolbar__count" data-testid="library-gallery-selection-count">
                            {selectedCount} selected
                        </span>
                    )}
                </div>

                <div className="library-gallery-toolbar__group">
                    <span className="library-gallery-toolbar__label">Sort</span>
                    <ListSortControl
                        aria-label="Sort gallery"
                        sorting={sorting}
                        sortOptions={sortOptions}
                        onSortingChange={onSortingChange}
                        disabled={disabled}
                    />
                </div>
            </div>
        );
    },
);

LibraryGalleryToolbar.displayName = 'LibraryGalleryToolbar';

export default LibraryGalleryToolbar;
