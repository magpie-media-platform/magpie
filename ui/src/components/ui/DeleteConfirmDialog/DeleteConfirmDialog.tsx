import { Trash } from '@phosphor-icons/react';

import { Dialog, DialogContent, DialogHeader, DialogTitle, DialogDescription, DialogFooter } from '../Dialog';
import { Button } from '../Button';

export interface DeleteConfirmDialogProps {
    open: boolean;

    onConfirm: () => void;

    onCancel: () => void;

    itemName: string;

    itemType: string;

    isDeleting?: boolean;
}

export function DeleteConfirmDialog({
    open,
    onConfirm,
    onCancel,
    itemName,
    itemType,
    isDeleting = false,
}: DeleteConfirmDialogProps) {
    return (
        <Dialog
            open={open}
            onOpenChange={(isOpen) => {
                if (!isOpen) onCancel();
            }}
        >
            <DialogContent data-component="delete-confirm-dialog">
                <DialogHeader>
                    <DialogTitle>Delete {itemType}</DialogTitle>
                    <DialogDescription>
                        Are you sure you want to delete &quot;{itemName}&quot;? This action cannot be undone.
                    </DialogDescription>
                </DialogHeader>
                <DialogFooter>
                    <Button variant="ghost" onClick={onCancel} disabled={isDeleting} data-action="cancel">
                        Cancel
                    </Button>
                    <Button variant="destructive" onClick={onConfirm} loading={isDeleting} data-action="confirm-delete">
                        <Trash weight="bold" aria-hidden="true" />
                        {isDeleting ? 'Deleting...' : 'Delete'}
                    </Button>
                </DialogFooter>
            </DialogContent>
        </Dialog>
    );
}

DeleteConfirmDialog.displayName = 'DeleteConfirmDialog';

export default DeleteConfirmDialog;
