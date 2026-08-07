import { useState } from 'react';

import { Button } from '@/components/ui/Button';
import {
    Dialog,
    DialogBody,
    DialogContent,
    DialogDescription,
    DialogFooter,
    DialogHeader,
    DialogTitle,
} from '@/components/ui/Dialog';
import { InlineEditRow } from '@/components/ui/InlineEditRow';
import { PathInput } from '@/components/ui/PathInput';
import { useToast } from '@/components/ui/Toast';
import { useApplyContentPath } from '@/hooks/useContent';
import './PathControl.css';

interface PathControlProps {
    contentHierarchyItemId: string;
    rawPath?: string | null;
    canEdit?: boolean;
}

function PathEditor({
    contentHierarchyItemId,
    rawPath,
    onClose,
}: {
    contentHierarchyItemId: string;
    rawPath?: string | null;
    onClose: () => void;
}) {
    const { toast } = useToast();
    const applyMutation = useApplyContentPath();
    const [value, setValue] = useState(rawPath ?? '');
    const [error, setError] = useState<string | null>(null);

    const apply = async (path: string | null) => {
        if (path !== null && !path.trim()) {
            setError('Enter a path or use the inherited-path action.');
            return;
        }

        setError(null);
        try {
            await applyMutation.mutateAsync({
                contentHierarchyItemId,
                path: path === null ? null : path.trim(),
            });
            toast({ title: 'Path updated', description: 'The new path is active.', variant: 'success' });
            onClose();
        } catch (cause) {
            setError(cause instanceof Error ? cause.message : 'Failed to update the path.');
        }
    };

    return (
        <>
            <span className="path-control__editing">Editing path...</span>
            <Dialog open onOpenChange={(open) => (!open ? onClose() : undefined)}>
                <DialogContent className="path-control__dialog" data-testid="path-control__dialog">
                    <DialogHeader>
                        <DialogTitle>Edit path</DialogTitle>
                        <DialogDescription>Enter a relative or absolute folder path.</DialogDescription>
                    </DialogHeader>
                    <DialogBody className="path-control__body">
                        <PathInput
                            aria-label="Content path"
                            mode="folder"
                            value={value}
                            onChange={(next) => {
                                setValue(next);
                                setError(null);
                            }}
                            error={!!error}
                            errorMessage={error ?? undefined}
                            disabled={applyMutation.isPending}
                        />
                    </DialogBody>
                    <DialogFooter>
                        <Button variant="ghost" onClick={onClose} disabled={applyMutation.isPending}>
                            Cancel
                        </Button>
                        <Button variant="secondary" onClick={() => void apply(null)} disabled={applyMutation.isPending}>
                            Use inherited path
                        </Button>
                        <Button variant="primary" onClick={() => void apply(value)} loading={applyMutation.isPending}>
                            Save path
                        </Button>
                    </DialogFooter>
                </DialogContent>
            </Dialog>
        </>
    );
}

export function PathControl({ contentHierarchyItemId, rawPath, canEdit = true }: PathControlProps) {
    return (
        <InlineEditRow
            label="Path"
            data-testid="path-control"
            canEdit={canEdit}
            editAriaLabel="Edit content path"
            triggerTestId="path-control__trigger"
            readValue={
                <span className="path-control__value">
                    <strong>{rawPath ? 'Owned path' : 'Inherited path'}</strong>
                    <span>{rawPath ?? 'No explicit location'}</span>
                </span>
            }
        >
            {({ close }) => (
                <PathEditor contentHierarchyItemId={contentHierarchyItemId} rawPath={rawPath} onClose={close} />
            )}
        </InlineEditRow>
    );
}

PathControl.displayName = 'PathControl';
