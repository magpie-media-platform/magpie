import { useState } from 'react';

import { useAuth } from '@/auth';
import { FileBrowser } from '@/components/configuration/FileBrowser';
import { InlineEditRow } from '@/components/ui/InlineEditRow';
import { Select, SelectContent, SelectItem, SelectTrigger } from '@/components/ui/Select';
import { useToast } from '@/components/ui/Toast';
import { useApplyContentPath, useEffectiveRootFolder } from '@/hooks/useContent';
import { useLookupError } from '@/hooks/useLookupError';

interface PathControlProps {
    contentHierarchyItemId: string;
    rawPath?: string | null;
    canEdit?: boolean;
}

const CURRENT_VALUE = '__current__';
const INHERIT_VALUE = '__inherit__';
const SELECT_VALUE = '__select__';

function PathEditor({
    contentHierarchyItemId,
    rawPath,
    initialPath,
    onClose,
}: {
    contentHierarchyItemId: string;
    rawPath?: string | null;
    initialPath?: string | null;
    onClose: () => void;
}) {
    const { hasPermission } = useAuth();
    const { toast } = useToast();
    const applyMutation = useApplyContentPath();
    const [browserOpen, setBrowserOpen] = useState(false);

    const apply = async (path: string | null) => {
        if (applyMutation.isPending) {
            return;
        }
        if (path === rawPath) {
            onClose();
            return;
        }

        try {
            await applyMutation.mutateAsync({ contentHierarchyItemId, path });
            toast({
                title: 'Path updated',
                description: path === null ? 'The path now uses Inherit.' : 'The selected folder is active.',
                variant: 'success',
            });
            onClose();
        } catch (error) {
            const message = error instanceof Error ? error.message : 'The update failed.';
            toast({ title: 'Update failed', description: message, variant: 'error' });
        }
    };

    return (
        <>
            <Select
                defaultOpen
                value={rawPath ? CURRENT_VALUE : INHERIT_VALUE}
                onValueChange={(value) => {
                    if (value === INHERIT_VALUE) {
                        void apply(null);
                    } else if (value === SELECT_VALUE) {
                        setBrowserOpen(true);
                    } else {
                        onClose();
                    }
                }}
                onDismiss={() => {
                    if (!browserOpen) onClose();
                }}
                disabled={applyMutation.isPending}
            >
                <SelectTrigger size="sm" aria-label="Path" data-testid="path-control__select">
                    {rawPath ?? 'Inherit'}
                </SelectTrigger>
                <SelectContent>
                    <SelectItem value={INHERIT_VALUE}>Inherit</SelectItem>
                    {rawPath && (
                        <SelectItem value={CURRENT_VALUE} title={rawPath}>
                            {rawPath}
                        </SelectItem>
                    )}
                    <SelectItem value={SELECT_VALUE} disabled={!hasPermission('files.browse')}>
                        Select folder
                    </SelectItem>
                </SelectContent>
            </Select>
            <FileBrowser
                open={browserOpen}
                onOpenChange={(open) => {
                    setBrowserOpen(open);
                    if (!open) {
                        onClose();
                    }
                }}
                onSelect={(path) => void apply(path)}
                mode="folder"
                initialPath={rawPath ?? initialPath ?? undefined}
            />
        </>
    );
}

export function PathControl({ contentHierarchyItemId, rawPath, canEdit = true }: PathControlProps) {
    const effectiveQuery = useEffectiveRootFolder(contentHierarchyItemId);
    const { data: effectiveRootFolder, isLoading, isFetching, refetch } = effectiveQuery;
    const queryError = useLookupError(effectiveQuery);
    const lookupError = rawPath ? null : queryError;
    const value =
        rawPath ??
        (lookupError
            ? 'Path could not be loaded'
            : isLoading
              ? 'Resolving path...'
              : (effectiveRootFolder?.rootFolderPath ?? 'No path'));

    return (
        <InlineEditRow
            label="Path"
            data-testid="path-control"
            canEdit={canEdit}
            editAriaLabel="Edit content path"
            triggerTestId="path-control__trigger"
            error={lookupError}
            onRetry={() => void refetch()}
            retrying={isFetching}
            retryAriaLabel="Retry loading path"
            readValue={
                <span data-testid="path-control__value" title={value}>
                    {value}
                </span>
            }
        >
            {({ close }) => (
                <PathEditor
                    contentHierarchyItemId={contentHierarchyItemId}
                    rawPath={rawPath}
                    initialPath={effectiveRootFolder?.rootFolderPath}
                    onClose={close}
                />
            )}
        </InlineEditRow>
    );
}

PathControl.displayName = 'PathControl';
