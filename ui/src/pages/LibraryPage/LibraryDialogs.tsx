import { FolderOpenIcon, RankingIcon } from '@phosphor-icons/react';

import {
    BulkTagAssignment,
    type BulkTagItem,
    type ContentWithTags,
    type BulkTagAssignmentResult,
} from '@/components/ui/BulkTagAssignment';
import { BulkValuePicker, type BulkValuePickerOption } from '@/components/ui/BulkValuePicker';
import { DeleteConfirmDialog } from '@/components/ui/DeleteConfirmDialog/DeleteConfirmDialog';
import { SaveFilterDialog } from '@/components/ui/SaveFilterDialog';
import { ContentLineageTitle } from '@/components/content/ContentLineageTitle';

import type { SavedFilterReference } from './libraryPageConstants';

interface LibraryDialogsProps {
    bulkTagsOpen: boolean;
    onCloseBulkTags: () => void;
    bulkTags: BulkTagItem[];
    selectedItems: ContentWithTags[];
    selectedCount: number;
    onApplyTags: (result: BulkTagAssignmentResult) => void;
    isApplyingTags: boolean;

    bulkRootFolderOpen: boolean;
    onCloseBulkRootFolder: () => void;
    rootFolderOptions: BulkValuePickerOption[];
    onApplyRootFolder: (rootFolderId: string | null) => void;
    isApplyingRootFolder: boolean;

    bulkQualityTierListOpen: boolean;
    onCloseBulkQualityTierList: () => void;
    qualityTierListOptions: BulkValuePickerOption[];
    onApplyQualityTierList: (qualityTierListId: string | null) => void;
    isApplyingQualityTierList: boolean;

    saveFilterDialogOpen: boolean;
    onCloseSaveFilter: () => void;
    onSaveFilter: (name: string) => void;
    canOfferUpdate: boolean;
    onUpdateFilter: () => void;
    referencedSavedFilter: SavedFilterReference | null;
    isSavingFilter: boolean;
    isUpdatingFilter: boolean;
    filterQuery: string;
    saveFilterError: string | null;
    saveFilterQueryError: string | null;
    existingFilterNames: string[];

    bulkDeleteConfirmOpen: boolean;
    onConfirmBulkDelete: () => void;
    onCancelBulkDelete: () => void;
    bulkDeleteFiles: boolean;
    onBulkDeleteFilesChange: (checked: boolean) => void;
    isDeleting: boolean;
}

export function LibraryDialogs({
    bulkTagsOpen,
    onCloseBulkTags,
    bulkTags,
    selectedItems,
    selectedCount,
    onApplyTags,
    isApplyingTags,
    bulkRootFolderOpen,
    onCloseBulkRootFolder,
    rootFolderOptions,
    onApplyRootFolder,
    isApplyingRootFolder,
    bulkQualityTierListOpen,
    onCloseBulkQualityTierList,
    qualityTierListOptions,
    onApplyQualityTierList,
    isApplyingQualityTierList,
    saveFilterDialogOpen,
    onCloseSaveFilter,
    onSaveFilter,
    canOfferUpdate,
    onUpdateFilter,
    referencedSavedFilter,
    isSavingFilter,
    isUpdatingFilter,
    filterQuery,
    saveFilterError,
    saveFilterQueryError,
    existingFilterNames,
    bulkDeleteConfirmOpen,
    onConfirmBulkDelete,
    onCancelBulkDelete,
    bulkDeleteFiles,
    onBulkDeleteFilesChange,
    isDeleting,
}: LibraryDialogsProps) {
    const selectedItemsLabel = `${selectedCount} item${selectedCount === 1 ? '' : 's'}`;

    return (
        <>
            <BulkTagAssignment
                open={bulkTagsOpen}
                onOpenChange={(open) => !open && onCloseBulkTags()}
                availableTags={bulkTags}
                selectedItems={selectedItems}
                selectedCount={selectedCount}
                onApply={onApplyTags}
                isApplying={isApplyingTags}
            />

            <BulkValuePicker
                open={bulkRootFolderOpen}
                onOpenChange={(open) => !open && onCloseBulkRootFolder()}
                title="Root folder"
                icon={<FolderOpenIcon weight="duotone" aria-hidden="true" />}
                description="Root folder for the selected items, without moving files. Inherit removes the per-item override."
                selectLabel="Root folder"
                inheritLabel="Inherit / content-type default"
                options={rootFolderOptions}
                emptyHint="No root folders are configured."
                selectedCount={selectedCount}
                onApply={onApplyRootFolder}
                isApplying={isApplyingRootFolder}
                data-testid="bulk-root-folder-picker"
            />

            <BulkValuePicker
                open={bulkQualityTierListOpen}
                onOpenChange={(open) => !open && onCloseBulkQualityTierList()}
                title="Quality"
                icon={<RankingIcon weight="duotone" aria-hidden="true" />}
                description="Quality for the selected items. Inherit uses each item's inherited or default quality."
                selectLabel="Quality"
                inheritLabel="Inherit / default"
                options={qualityTierListOptions}
                emptyHint="No tier lists are configured."
                selectedCount={selectedCount}
                onApply={onApplyQualityTierList}
                isApplying={isApplyingQualityTierList}
                data-testid="bulk-quality-profile-picker"
            />

            <SaveFilterDialog
                open={saveFilterDialogOpen}
                onOpenChange={(open) => !open && onCloseSaveFilter()}
                onSave={onSaveFilter}
                onUpdate={canOfferUpdate ? onUpdateFilter : undefined}
                updateTargetName={canOfferUpdate ? referencedSavedFilter?.name : undefined}
                isSaving={isSavingFilter}
                isUpdating={isUpdatingFilter}
                filterQuery={filterQuery}
                nameError={saveFilterError}
                queryError={saveFilterQueryError}
                existingNames={existingFilterNames}
            />

            <DeleteConfirmDialog
                open={bulkDeleteConfirmOpen}
                onConfirm={onConfirmBulkDelete}
                onCancel={onCancelBulkDelete}
                itemName={selectedItemsLabel}
                itemType="Content"
                description={
                    <>
                        Delete{' '}
                        {selectedCount === 1 && selectedItems.length === 1 ? (
                            <ContentLineageTitle
                                qualifiedName={{
                                    id: selectedItems[0].id,
                                    parentId: selectedItems[0].parentId,
                                    title: selectedItems[0].title,
                                }}
                            />
                        ) : (
                            selectedItemsLabel
                        )}
                        ? This cannot be undone.{' '}
                        {bulkDeleteFiles
                            ? 'Managed files will be deleted from disk.'
                            : 'Managed files will remain on disk.'}
                    </>
                }
                isDeleting={isDeleting}
                extraToggle={{
                    label: 'Also delete files from disk',
                    checked: bulkDeleteFiles,
                    onChange: onBulkDeleteFilesChange,
                }}
            />
        </>
    );
}

LibraryDialogs.displayName = 'LibraryDialogs';

export default LibraryDialogs;
