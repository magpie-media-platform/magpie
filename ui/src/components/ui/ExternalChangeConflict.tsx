import { Alert } from './Alert';
import { Button } from './Button';
import {
    Dialog,
    DialogBody,
    DialogContent,
    DialogDescription,
    DialogFooter,
    DialogHeader,
    DialogTitle,
} from './Dialog';

export function ExternalChangeConflictWarning({
    state,
    entityLabel,
    id,
}: {
    state: 'stale' | 'deleted';
    entityLabel: string;
    id: string;
}) {
    const deleted = state === 'deleted';

    return (
        <Alert variant="warning" title={`${entityLabel} ${deleted ? 'deleted' : 'changed'} elsewhere`} id={id}>
            {deleted
                ? `${entityLabel} no longer exists. Magpie cannot save the unsaved changes.`
                : `${entityLabel} changed elsewhere after editing began. Magpie kept the unsaved changes from the older version.`}
        </Alert>
    );
}

export function StaleSaveConfirmationDialog({
    open,
    title,
    description,
    entityLabel,
    saving,
    onCancel,
    onConfirm,
}: {
    open: boolean;
    title?: string;
    description?: string;
    entityLabel?: string;
    saving: boolean;
    onCancel: () => void;
    onConfirm: () => void;
}) {
    const entity = entityLabel?.toLowerCase() ?? 'item';

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
                <DialogBody />
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
