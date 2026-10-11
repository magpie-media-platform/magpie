import { useCallback, useMemo, useState } from 'react';
import { useQueryClient } from '@tanstack/react-query';

import { type BulkTagItem, type BulkTagAssignmentResult } from '@/components/ui/BulkTagAssignment';
import { type BulkValuePickerOption } from '@/components/ui/BulkValuePicker';
import { useToast } from '@/components/ui/Toast';
import {
    useContentTags,
    contentQueryKeys,
    useBulkPatchContent,
    useBulkDeleteContent,
    useBulkDeletePreview,
} from '@/hooks/useContent';
import { type BulkSelection } from '@/hooks/useBulkSelection';
import { useRootFolders } from '@/hooks/useRootFolders';
import { useQualityTierLists } from '@/hooks/useQualityTierLists';
import { useToggle } from '@/hooks/useToggle';
import { getErrorMessage } from '@/lib/errors';

interface UseLibraryBulkActionsArgs {
    selection: BulkSelection;
    bulkFilter: string;
}

type BulkTargetResolution =
    | { kind: 'empty' }
    | { kind: 'unsupported' }
    | { kind: 'target'; target: { ids?: string[]; filter?: string } };

const UNSUPPORTED_TARGET_TOAST = {
    title: 'Cannot apply to a partial select-all',
    description: 'Deselecting rows within a select-all is not supported. Clear the selection and pick rows directly.',
    variant: 'error',
} as const;

function resolveBulkTarget(selection: BulkSelection, bulkFilter: string): BulkTargetResolution {
    const resolved = selection.resolve();

    if (resolved.mode === 'ids') {
        return resolved.ids.length === 0 ? { kind: 'empty' } : { kind: 'target', target: { ids: resolved.ids } };
    }

    if (resolved.excludeIds.length > 0) {
        return { kind: 'unsupported' };
    }

    return { kind: 'target', target: { filter: bulkFilter } };
}

export function useLibraryBulkActions({ selection, bulkFilter }: UseLibraryBulkActionsArgs) {
    const queryClient = useQueryClient();
    const { toast } = useToast();

    const [bulkTagsOpen, , openBulkTags, closeBulkTags] = useToggle();
    const [bulkDeleteConfirmOpen, , openBulkDeleteConfirm, closeBulkDeleteConfirm] = useToggle();
    const [bulkDeleteFiles, setBulkDeleteFiles] = useState(false);
    const [bulkRootFolderOpen, , openBulkRootFolder, closeBulkRootFolder] = useToggle();
    const [bulkQualityTierListOpen, , openBulkQualityTierList, closeBulkQualityTierList] = useToggle();

    const bulkTagsMutation = useBulkPatchContent();
    const bulkSetMonitoringMutation = useBulkPatchContent();
    const bulkDeleteMutation = useBulkDeleteContent();
    const bulkDeleteTarget =
        bulkDeleteConfirmOpen && bulkDeleteFiles ? resolveBulkTarget(selection, bulkFilter) : undefined;
    const bulkDeletePreview = useBulkDeletePreview(
        bulkDeleteTarget?.kind === 'target' ? { ...bulkDeleteTarget.target, deleteFromLibrary: true } : undefined,
    );
    const bulkRootFolderMutation = useBulkPatchContent();
    const bulkQualityTierListMutation = useBulkPatchContent();

    const { folders: rootFolders } = useRootFolders();
    const { tierLists: qualityTierLists } = useQualityTierLists();

    const rootFolderOptions: BulkValuePickerOption[] = useMemo(
        () => rootFolders.map((folder) => ({ id: folder.id, label: `${folder.name} (${folder.path})` })),
        [rootFolders],
    );

    const qualityTierListOptions: BulkValuePickerOption[] = useMemo(
        () =>
            qualityTierLists.map((list) => ({
                id: list.id,
                label: `${list.name} - ${list.mimeTypes.length > 0 ? list.mimeTypes.join(', ') : 'Any MIME type'}`,
            })),
        [qualityTierLists],
    );

    const { data: apiTags } = useContentTags();

    const bulkTags: BulkTagItem[] = useMemo(() => {
        if (!apiTags || !Array.isArray(apiTags)) {
            return [];
        }
        return apiTags.map((tag: { name: string }) => ({
            id: tag.name,
            name: tag.name,
        }));
    }, [apiTags]);

    const handleOpenBulkTags = useCallback(() => {
        void queryClient.invalidateQueries({ queryKey: contentQueryKeys.tags() });
        openBulkTags();
    }, [openBulkTags, queryClient]);

    const handleApplyTags = useCallback(
        async (result: BulkTagAssignmentResult) => {
            const resolution = resolveBulkTarget(selection, bulkFilter);
            if (resolution.kind === 'empty') {
                return;
            }
            if (resolution.kind === 'unsupported') {
                toast(UNSUPPORTED_TARGET_TOAST);
                return;
            }

            const tags: { add?: string[]; remove?: string[] } = {};
            if (result.tagsToAdd.length > 0) {
                tags.add = result.tagsToAdd;
            }
            if (result.tagsToRemove.length > 0) {
                tags.remove = result.tagsToRemove;
            }

            try {
                const updateResult = await bulkTagsMutation.mutateAsync({
                    ...resolution.target,
                    patch: { tags },
                });

                if (updateResult.error) {
                    toast({
                        title: 'Failed to update tags',
                        description: updateResult.error,
                        variant: 'error',
                    });
                    return;
                }

                const addCount = result.tagsToAdd.length;
                const removeCount = result.tagsToRemove.length;
                let description = '';
                if (addCount > 0 && removeCount > 0) {
                    description = `Added ${addCount} tag${addCount !== 1 ? 's' : ''}, removed ${removeCount} tag${removeCount !== 1 ? 's' : ''}`;
                } else if (addCount > 0) {
                    description = `Added ${addCount} tag${addCount !== 1 ? 's' : ''}`;
                } else if (removeCount > 0) {
                    description = `Removed ${removeCount} tag${removeCount !== 1 ? 's' : ''}`;
                }

                toast({
                    title: `${updateResult.count} item${updateResult.count !== 1 ? 's' : ''} updated`,
                    description,
                    variant: 'success',
                });

                closeBulkTags();
                selection.clear();
            } catch (error) {
                toast({
                    title: 'Failed to update tags',
                    description: getErrorMessage(error, 'Please try again.'),
                    variant: 'error',
                });
            }
        },
        [bulkTagsMutation, closeBulkTags, selection, bulkFilter, toast],
    );

    const handleBulkSetMonitoring = useCallback(
        async (monitor: boolean) => {
            const resolution = resolveBulkTarget(selection, bulkFilter);
            if (resolution.kind === 'empty') {
                return;
            }
            if (resolution.kind === 'unsupported') {
                toast(UNSUPPORTED_TARGET_TOAST);
                return;
            }

            const verb = monitor ? 'monitored' : 'unmonitored';

            try {
                const result = await bulkSetMonitoringMutation.mutateAsync({
                    ...resolution.target,
                    patch: { monitoring: { value: monitor ? 'Monitored' : 'Unmonitored' } },
                });

                if (result.error) {
                    toast({
                        title: `Failed to ${monitor ? 'monitor' : 'unmonitor'} items`,
                        description: result.error,
                        variant: 'error',
                    });
                    return;
                }

                toast({
                    title: `${result.count} item${result.count !== 1 ? 's' : ''} ${verb}`,
                    variant: 'success',
                });

                selection.clear();
            } catch (error) {
                toast({
                    title: `Failed to ${monitor ? 'monitor' : 'unmonitor'} items`,
                    description: getErrorMessage(error, 'Please try again.'),
                    variant: 'error',
                });
            }
        },
        [bulkSetMonitoringMutation, selection, bulkFilter, toast],
    );

    const handleOpenBulkDelete = useCallback(() => {
        setBulkDeleteFiles(false);
        openBulkDeleteConfirm();
    }, [openBulkDeleteConfirm]);

    const handleConfirmBulkDelete = useCallback(async () => {
        const resolution = resolveBulkTarget(selection, bulkFilter);
        if (resolution.kind === 'empty') {
            closeBulkDeleteConfirm();
            return;
        }
        if (resolution.kind === 'unsupported') {
            toast(UNSUPPORTED_TARGET_TOAST);
            return;
        }

        try {
            const result = await bulkDeleteMutation.mutateAsync({
                ...resolution.target,
                deleteFromLibrary: bulkDeleteFiles,
            });

            if (result.error) {
                toast({
                    title: 'Failed to delete items',
                    description: result.error,
                    variant: 'error',
                });
                return;
            }

            toast({
                title: `${result.count} item${result.count !== 1 ? 's' : ''} deleted`,
                variant: 'success',
            });

            closeBulkDeleteConfirm();
            selection.clear();
        } catch (error) {
            toast({
                title: 'Failed to delete items',
                description: getErrorMessage(error, 'Please try again.'),
                variant: 'error',
            });
        }
    }, [bulkDeleteMutation, bulkDeleteFiles, closeBulkDeleteConfirm, selection, bulkFilter, toast]);

    const handleApplyRootFolder = useCallback(
        async (rootFolderId: string | null) => {
            const resolution = resolveBulkTarget(selection, bulkFilter);
            if (resolution.kind === 'empty') {
                return;
            }
            if (resolution.kind === 'unsupported') {
                toast(UNSUPPORTED_TARGET_TOAST);
                return;
            }

            const verb = rootFolderId ? 'updated' : 'reset to inherit';

            try {
                const result = await bulkRootFolderMutation.mutateAsync({
                    ...resolution.target,
                    patch: { rootFolderId: { value: rootFolderId } },
                });

                if (result.error) {
                    toast({
                        title: 'Failed to update root folder',
                        description: result.error,
                        variant: 'error',
                    });
                    return;
                }

                toast({
                    title: `Root folder ${verb} for ${result.count} item${result.count !== 1 ? 's' : ''}`,
                    variant: 'success',
                });

                closeBulkRootFolder();
                selection.clear();
            } catch (error) {
                toast({
                    title: 'Failed to update root folder',
                    description: getErrorMessage(error, 'Please try again.'),
                    variant: 'error',
                });
            }
        },
        [bulkRootFolderMutation, closeBulkRootFolder, selection, bulkFilter, toast],
    );

    const handleApplyQualityTierList = useCallback(
        async (qualityTierListId: string | null) => {
            const resolution = resolveBulkTarget(selection, bulkFilter);
            if (resolution.kind === 'empty') {
                return;
            }
            if (resolution.kind === 'unsupported') {
                toast(UNSUPPORTED_TARGET_TOAST);
                return;
            }

            const verb = qualityTierListId ? 'updated' : 'reset to inherit';

            try {
                const result = await bulkQualityTierListMutation.mutateAsync({
                    ...resolution.target,
                    patch: { qualityTierListId: { value: qualityTierListId } },
                });

                if (result.error) {
                    toast({
                        title: 'Failed to update quality',
                        description: result.error,
                        variant: 'error',
                    });
                    return;
                }

                toast({
                    title: `Quality ${verb} for ${result.count} item${result.count !== 1 ? 's' : ''}`,
                    variant: 'success',
                });

                closeBulkQualityTierList();
                selection.clear();
            } catch (error) {
                toast({
                    title: 'Failed to update quality',
                    description: getErrorMessage(error, 'Please try again.'),
                    variant: 'error',
                });
            }
        },
        [bulkQualityTierListMutation, closeBulkQualityTierList, selection, bulkFilter, toast],
    );

    return {
        bulkTags,
        rootFolderOptions,
        qualityTierListOptions,
        handleOpenBulkTags,
        handleApplyTags,
        handleBulkSetMonitoring,
        handleConfirmBulkDelete,
        handleApplyRootFolder,
        handleApplyQualityTierList,
        openBulkRootFolder,
        openBulkQualityTierList,
        openBulkDeleteConfirm: handleOpenBulkDelete,
        bulkTagsOpen,
        closeBulkTags,
        bulkRootFolderOpen,
        closeBulkRootFolder,
        bulkQualityTierListOpen,
        closeBulkQualityTierList,
        bulkDeleteConfirmOpen,
        bulkDeleteFiles,
        setBulkDeleteFiles,
        bulkDeletePreview,
        closeBulkDeleteConfirm,
        isApplyingTags: bulkTagsMutation.isPending,
        isSettingMonitoring: bulkSetMonitoringMutation.isPending,
        isDeleting: bulkDeleteMutation.isPending,
        isApplyingRootFolder: bulkRootFolderMutation.isPending,
        isApplyingQualityTierList: bulkQualityTierListMutation.isPending,
    };
}
