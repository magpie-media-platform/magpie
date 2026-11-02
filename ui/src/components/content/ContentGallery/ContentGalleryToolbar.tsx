import './ContentGallery.css';
import { forwardRef, type ComponentPropsWithoutRef } from 'react';
import type { SortingState } from '@tanstack/react-table';

import { SelectModeToggle } from '@/components/ui/BulkActionsToolbar';
import { ListSortControl } from '@/components/ui/ListSortControl';
import type { MobileListSelectionControl, MobileListSortOption } from '@/components/ui/MobileListToolbar';

export interface ContentGalleryToolbarProps extends Omit<ComponentPropsWithoutRef<'div'>, 'onChange'> {
    sorting: SortingState;
    sortOptions: readonly MobileListSortOption[];
    onSortingChange: (sorting: SortingState) => void;
    selection?: MobileListSelectionControl;
    selectedCount?: number;
    showSelectionCount?: boolean;
    disabled?: boolean;
    allowUnsorted?: boolean;
}

export const ContentGalleryToolbar = forwardRef<HTMLDivElement, ContentGalleryToolbarProps>(
    (
        {
            sorting,
            sortOptions,
            onSortingChange,
            selection,
            selectedCount = 0,
            showSelectionCount = true,
            disabled = false,
            allowUnsorted = true,
            className,
            ...props
        },
        ref,
    ) => {
        const classes = ['content-gallery-toolbar', className].filter(Boolean).join(' ');

        return (
            <div ref={ref} className={classes} data-component="content-gallery-toolbar" {...props}>
                <div className="content-gallery-toolbar__group">
                    {selection && (
                        <SelectModeToggle
                            active={selection.active}
                            onToggle={selection.onToggle}
                            disabled={selection.disabled}
                            mobileOnly={false}
                        />
                    )}
                    {selection?.active && showSelectionCount && (
                        <span className="content-gallery-toolbar__count" data-testid="content-gallery-selection-count">
                            {selectedCount} selected
                        </span>
                    )}
                </div>

                <div className="content-gallery-toolbar__group">
                    <span className="content-gallery-toolbar__label">Sort</span>
                    <ListSortControl
                        aria-label="Sort gallery"
                        sorting={sorting}
                        sortOptions={sortOptions}
                        onSortingChange={onSortingChange}
                        allowUnsorted={allowUnsorted}
                        disabled={disabled}
                    />
                </div>
            </div>
        );
    },
);

ContentGalleryToolbar.displayName = 'ContentGalleryToolbar';

export default ContentGalleryToolbar;
