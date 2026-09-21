import { Alert } from '../Alert';
import { Button } from '../Button';
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '../Dialog';

export interface ExternalChangeConflictWarningProps {
    state: 'stale' | 'deleted';

    entityLabel: string;

    id: string;
}

export function ExternalChangeConflictWarning({ state, entityLabel, id }: ExternalChangeConflictWarningProps) {
    const deleted = state === 'deleted';

    return (
        <Alert variant="warning" title={`${entityLabel} ${deleted ? 'deleted' : 'changed'} elsewhere`} id={id}>
            {deleted
                ? `${entityLabel} no longer exists, so the unsaved changes cannot be saved.`
                : `${entityLabel} changed elsewhere after editing began. The unsaved changes are from the older version.`}
        </Alert>
    );
}

ExternalChangeConflictWarning.displayName = 'ExternalChangeConflictWarning';

export interface StaleSaveConfirmationDialogProps {
    open: boolean;

    title?: string;

    description?: string;

    entityLabel?: string;

    saving: boolean;

    onCancel: () => void;

    onConfirm: () => void;
}

export function StaleSaveConfirmationDialog({
    open,
    title,
    description,
    entityLabel,
    saving,
    onCancel,
    onConfirm,
}: StaleSaveConfirmationDialogProps) {
    const entity = entityLabel ?? 'item';

    return (
        <Dialog open={open} onOpenChange={(nextOpen) => (!nextOpen ? onCancel() : undefined)}>
            <DialogContent size="alert">
                <DialogHeader>
                    <DialogTitle>{title ?? 'Save over newer changes?'}</DialogTitle>
                    <DialogDescription>
                        {description ??
                            `This ${entity} changed elsewhere after editing began. Saving will overwrite the newer changes.`}
                    </DialogDescription>
                </DialogHeader>
                <DialogFooter>
                    <Button variant="ghost" onClick={onCancel} disabled={saving} autoFocus>
                        Keep editing
                    </Button>
                    <Button variant="primary" onClick={onConfirm} disabled={saving}>
                        {saving ? 'Saving...' : 'Save anyway'}
                    </Button>
                </DialogFooter>
            </DialogContent>
        </Dialog>
    );
}

StaleSaveConfirmationDialog.displayName = 'StaleSaveConfirmationDialog';
