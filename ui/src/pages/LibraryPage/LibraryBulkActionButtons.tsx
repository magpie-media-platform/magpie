import { EyeIcon, EyeSlashIcon, FolderOpenIcon, RankingIcon, TagIcon, TrashIcon } from '@phosphor-icons/react';

import { Button } from '@/components/ui/Button';

interface LibraryBulkActionButtonsProps {
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

export function LibraryBulkActionButtons({
    canEditContent,
    canManageContent,
    canDeleteContent,
    onOpenBulkTags,
    onBulkSetMonitoring,
    isSettingMonitoring,
    onOpenBulkRootFolder,
    onOpenBulkQualityTierList,
    onOpenBulkDelete,
}: LibraryBulkActionButtonsProps) {
    return (
        <>
            {canEditContent && (
                <Button
                    variant="default"
                    size="sm"
                    onClick={onOpenBulkTags}
                    aria-label="Assign Tags"
                    data-action="bulk-tags"
                >
                    <TagIcon weight="bold" aria-hidden="true" />
                    <span className="bulk-actions-toolbar__btn-label">Assign Tags</span>
                </Button>
            )}
            {canEditContent && (
                <>
                    <Button
                        variant="default"
                        size="sm"
                        onClick={() => onBulkSetMonitoring(true)}
                        loading={isSettingMonitoring}
                        aria-label="Monitor"
                        data-action="bulk-monitor"
                    >
                        <EyeIcon weight="bold" aria-hidden="true" />
                        <span className="bulk-actions-toolbar__btn-label">Monitor</span>
                    </Button>
                    <Button
                        variant="default"
                        size="sm"
                        onClick={() => onBulkSetMonitoring(false)}
                        loading={isSettingMonitoring}
                        aria-label="Unmonitor"
                        data-action="bulk-unmonitor"
                    >
                        <EyeSlashIcon weight="bold" aria-hidden="true" />
                        <span className="bulk-actions-toolbar__btn-label">Unmonitor</span>
                    </Button>
                </>
            )}
            {canManageContent && (
                <>
                    <Button
                        variant="default"
                        size="sm"
                        onClick={onOpenBulkRootFolder}
                        aria-label="Root Folder"
                        data-action="bulk-root-folder"
                    >
                        <FolderOpenIcon weight="bold" aria-hidden="true" />
                        <span className="bulk-actions-toolbar__btn-label">Root Folder</span>
                    </Button>
                    <Button
                        variant="default"
                        size="sm"
                        onClick={onOpenBulkQualityTierList}
                        aria-label="Quality"
                        data-action="bulk-quality-profile"
                    >
                        <RankingIcon weight="bold" aria-hidden="true" />
                        <span className="bulk-actions-toolbar__btn-label">Quality</span>
                    </Button>
                </>
            )}
            {canDeleteContent && (
                <Button
                    variant="destructive"
                    size="sm"
                    onClick={onOpenBulkDelete}
                    aria-label="Delete"
                    data-action="bulk-delete"
                >
                    <TrashIcon weight="bold" aria-hidden="true" />
                    <span className="bulk-actions-toolbar__btn-label">Delete</span>
                </Button>
            )}
        </>
    );
}

LibraryBulkActionButtons.displayName = 'LibraryBulkActionButtons';

export default LibraryBulkActionButtons;
