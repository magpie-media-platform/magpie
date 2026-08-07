import { WarningIcon } from '@phosphor-icons/react';

import { Dialog, DialogContent, DialogHeader, DialogTitle, DialogDescription, DialogFooter } from '../Dialog';
import { Button } from '../Button';

export interface UnsavedChangesDialogProps {
    open: boolean;

    onDiscard: () => void;

    onCancel: () => void;
}

export function UnsavedChangesDialog({ open, onDiscard, onCancel }: UnsavedChangesDialogProps) {
    return (
        <Dialog
            open={open}
            onOpenChange={(isOpen) => {
                if (!isOpen) onCancel();
            }}
        >
            <DialogContent data-component="unsaved-changes-dialog">
                <DialogHeader>
                    <DialogTitle>
                        <WarningIcon weight="duotone" aria-hidden="true" />
                        Unsaved Changes
                    </DialogTitle>
                    <DialogDescription>Close without saving your changes?</DialogDescription>
                </DialogHeader>
                <DialogFooter>
                    <Button variant="ghost" onClick={onCancel} data-action="cancel">
                        Keep Editing
                    </Button>
                    <Button variant="destructive" onClick={onDiscard} data-action="discard">
                        Discard Changes
                    </Button>
                </DialogFooter>
            </DialogContent>
        </Dialog>
    );
}

UnsavedChangesDialog.displayName = 'UnsavedChangesDialog';

export default UnsavedChangesDialog;
