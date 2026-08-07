import { MobileSelectionBar } from '@/components/ui/MobileSelectionBar';
import type { BulkSelection } from '@/hooks/useBulkSelection';

import { LibraryBulkActionButtons } from './LibraryBulkActionButtons';

interface LibraryMobileSelectionBarProps {
    selection: BulkSelection;
    selectedPageCount: number;
    totalItems: number;
    displaySelectedCount: number;
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

export function LibraryMobileSelectionBar({
    selection,
    selectedPageCount,
    totalItems,
    displaySelectedCount,
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
}: LibraryMobileSelectionBarProps) {
    return (
        <MobileSelectionBar
            className="library-page__mobile-selection-bar"
            data-component="library-mobile-selection-bar"
            selection={selection}
            pageSelectedCount={selectedPageCount}
            totalCount={totalItems}
            displaySelectedCount={displaySelectedCount}
            onClear={onClearSelection}
            itemNounPlural="items"
            actionsClassName="library-page__mobile-selection-actions"
        >
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
        </MobileSelectionBar>
    );
}

LibraryMobileSelectionBar.displayName = 'LibraryMobileSelectionBar';

export default LibraryMobileSelectionBar;
