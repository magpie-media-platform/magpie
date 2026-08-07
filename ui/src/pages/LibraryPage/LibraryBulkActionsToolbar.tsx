import { XIcon } from '@phosphor-icons/react';

import { SelectAllBanner } from '@/components/ui/SelectAllBanner';
import type { BulkSelection } from '@/hooks/useBulkSelection';

import { LibraryBulkActionButtons } from './LibraryBulkActionButtons';

interface LibraryBulkActionsToolbarProps {
    selection: BulkSelection;
    selectedPageCount: number;
    totalItems: number;
    selectedCount: number;
    displaySelectedCount: number;
    hasSelection: boolean;
    onClearSelection: () => void;
    canEditContent: boolean;
    canManageContent: boolean;
    canDeleteContent: boolean;
    onOpenBulkTags: () => void;
    onBulkSetMonitoring: (monitor: boolean) => void;
    isSettingMonitoring: boolean;
    onOpenBulkRootFolder: () => void;
    onOpenBulkQualityTierList: () => void;
    onOpenBulkDelete: () => void;
}

export function LibraryBulkActionsToolbar({
    selection,
    selectedPageCount,
    totalItems,
    selectedCount,
    displaySelectedCount,
    hasSelection,
    onClearSelection,
    canEditContent,
    canManageContent,
    canDeleteContent,
    onOpenBulkTags,
    onBulkSetMonitoring,
    isSettingMonitoring,
    onOpenBulkRootFolder,
    onOpenBulkQualityTierList,
    onOpenBulkDelete,
}: LibraryBulkActionsToolbarProps) {
    return (
        <div
            className="bulk-actions-toolbar-presence library-page__desktop-bulk-toolbar"
            data-component="bulk-actions-toolbar"
            data-visible={hasSelection ? 'true' : 'false'}
            aria-hidden={!hasSelection}
            inert={!hasSelection}
        >
            <div className="bulk-actions-toolbar-presence__inner">
                {hasSelection && (
                    <div className="bulk-actions-toolbar">
                        {(selection.showAcrossPagesPrompt || selection.allPages) && (
                            <SelectAllBanner
                                allPages={selection.allPages}
                                pageSelectedCount={selectedPageCount}
                                totalCount={totalItems}
                                selectedCount={selectedCount}
                                onSelectAllAcrossPages={selection.selectAllAcrossPages}
                                onClear={selection.clear}
                                itemNounPlural="items"
                            />
                        )}
                        <div className="bulk-actions-toolbar__info">
                            <span className="bulk-actions-toolbar__count">{displaySelectedCount} selected</span>
                            <button
                                type="button"
                                className="bulk-actions-toolbar__clear"
                                onClick={onClearSelection}
                                aria-label="Clear selection"
                                data-action="clear-selection"
                            >
                                <XIcon weight="bold" aria-hidden="true" />
                                Clear
                            </button>
                        </div>
                        <div className="bulk-actions-toolbar__buttons">
                            <LibraryBulkActionButtons
                                canEditContent={canEditContent}
                                canManageContent={canManageContent}
                                canDeleteContent={canDeleteContent}
                                onOpenBulkTags={onOpenBulkTags}
                                onBulkSetMonitoring={onBulkSetMonitoring}
                                isSettingMonitoring={isSettingMonitoring}
                                onOpenBulkRootFolder={onOpenBulkRootFolder}
                                onOpenBulkQualityTierList={onOpenBulkQualityTierList}
                                onOpenBulkDelete={onOpenBulkDelete}
                            />
                        </div>
                    </div>
                )}
            </div>
        </div>
    );
}

LibraryBulkActionsToolbar.displayName = 'LibraryBulkActionsToolbar';

export default LibraryBulkActionsToolbar;
