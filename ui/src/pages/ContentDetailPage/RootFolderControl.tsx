import { useMemo } from 'react';

import { InlineEditRow } from '@/components/ui/InlineEditRow';
import { Select, SelectContent, SelectItem, SelectTrigger } from '@/components/ui/Select';
import { useToast } from '@/components/ui/Toast';
import { useEffectiveRootFolder, useSetContentRootFolder } from '@/hooks/useContent';
import { useLookupError } from '@/hooks/useLookupError';
import { useRootFolders } from '@/hooks/useRootFolders';

interface RootFolderControlProps {
    contentHierarchyItemId: string;
    contentType: string;
    canEdit?: boolean;
}

const INHERIT_VALUE = '__inherit__';

export function RootFolderControl({ contentHierarchyItemId, contentType, canEdit = true }: RootFolderControlProps) {
    const { toast } = useToast();
    const effectiveQuery = useEffectiveRootFolder(contentHierarchyItemId);
    const { data: effective, isLoading, isFetching, refetch } = effectiveQuery;
    const lookupError = useLookupError(effectiveQuery);
    const { folders: rootFolders, isLoading: foldersLoading } = useRootFolders();
    const setMutation = useSetContentRootFolder();

    const eligibleRoots = useMemo(() => {
        if (!rootFolders) {
            return [];
        }
        return rootFolders.filter((root) => root.contentType.toLowerCase() === contentType.toLowerCase());
    }, [rootFolders, contentType]);

    const selectedRootId = effective?.isOverriddenOnItem ? (effective.rootFolderId ?? '') : '';

    const handleChange = async (value: string, close: () => void) => {
        const nextRootId = value === INHERIT_VALUE ? null : value;
        if (setMutation.isPending) {
            return;
        }
        if (nextRootId === (selectedRootId || null)) {
            close();
            return;
        }
        try {
            await setMutation.mutateAsync({
                contentHierarchyItemId,
                rootFolderId: nextRootId,
            });
            toast({
                title: 'Root folder updated',
                description: nextRootId
                    ? 'The item now uses the selected root folder.'
                    : 'The override was cleared. The item falls back to its inherited or default root folder.',
                variant: 'success',
            });
            close();
        } catch (error) {
            const message = error instanceof Error ? error.message : 'The update failed.';
            toast({ title: 'Update failed', description: message, variant: 'error' });
        }
    };

    const label = lookupError
        ? 'Root folder could not be loaded'
        : isLoading
          ? 'Resolving root folder...'
          : effective?.rootFolderName
            ? effective.rootFolderName
            : 'No root folder';

    return (
        <InlineEditRow
            label="Root Folder"
            data-testid="root-folder-control"
            canEdit={canEdit}
            editAriaLabel="Edit root folder"
            triggerTestId="root-folder-control__trigger"
            error={lookupError}
            onRetry={() => void refetch()}
            retrying={isFetching}
            retryAriaLabel="Retry loading root folder"
            readValue={
                <span className="root-folder-control__value" data-testid="root-folder-control__label">
                    {label}
                </span>
            }
        >
            {({ close }) => (
                <Select
                    defaultOpen
                    value={selectedRootId || INHERIT_VALUE}
                    onValueChange={(value) => {
                        void handleChange(value, close);
                    }}
                    onDismiss={close}
                    disabled={foldersLoading || setMutation.isPending}
                >
                    <SelectTrigger
                        size="sm"
                        aria-label="Root folder override"
                        data-testid="root-folder-control__select"
                    >
                        {!selectedRootId
                            ? foldersLoading
                                ? 'Loading...'
                                : 'Inherit'
                            : (eligibleRoots.find((root) => root.id === selectedRootId)?.name ?? 'Inherit')}
                    </SelectTrigger>
                    <SelectContent>
                        <SelectItem value={INHERIT_VALUE}>{foldersLoading ? 'Loading...' : 'Inherit'}</SelectItem>
                        {eligibleRoots.map((root) => (
                            <SelectItem key={root.id} value={root.id}>
                                {root.name} ({root.path})
                            </SelectItem>
                        ))}
                    </SelectContent>
                </Select>
            )}
        </InlineEditRow>
    );
}

RootFolderControl.displayName = 'RootFolderControl';
