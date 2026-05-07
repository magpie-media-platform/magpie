import { useState, useCallback, useEffect, type FormEvent } from 'react';
import { FloppyDisk } from '@phosphor-icons/react';

import { Dialog, DialogContent, DialogHeader, DialogTitle, DialogDescription, DialogFooter } from '../Dialog';
import { Input } from '../Input';
import { Button } from '../Button';
import { Label } from '../Label';

import './SaveFilterDialog.css';

export interface SaveFilterDialogProps {
    open: boolean;

    onOpenChange: (open: boolean) => void;

    onSave: (name: string) => void | Promise<void>;

    isSaving?: boolean;

    filterQuery?: string;

    error?: string | null;
}

export function SaveFilterDialog({
    open,
    onOpenChange,
    onSave,
    isSaving = false,
    filterQuery,
    error,
}: SaveFilterDialogProps) {
    const [name, setName] = useState('');
    const [localError, setLocalError] = useState<string | null>(null);

    useEffect(() => {
        if (open) {
            setName('');
            setLocalError(null);
        }
    }, [open]);

    useEffect(() => {
        setLocalError(error ?? null);
    }, [error]);

    const handleSubmit = useCallback(
        async (e: FormEvent) => {
            e.preventDefault();

            const trimmedName = name.trim();
            if (!trimmedName) {
                setLocalError('Please enter a name for the filter.');
                return;
            }

            setLocalError(null);
            await onSave(trimmedName);
        },
        [name, onSave],
    );

    const handleCancel = useCallback(() => {
        onOpenChange(false);
    }, [onOpenChange]);

    return (
        <Dialog open={open} onOpenChange={onOpenChange}>
            <DialogContent className="save-filter-dialog" data-component="save-filter-dialog">
                <DialogHeader>
                    <DialogTitle>
                        <FloppyDisk weight="duotone" className="save-filter-dialog__icon" aria-hidden="true" />
                        Save Filter
                    </DialogTitle>
                    <DialogDescription>Give your filter a name so you can use it again later.</DialogDescription>
                </DialogHeader>

                <form onSubmit={handleSubmit} className="save-filter-dialog__form">
                    <div className="save-filter-dialog__field">
                        <Label htmlFor="filter-name">Filter Name</Label>
                        <Input
                            id="filter-name"
                            type="text"
                            value={name}
                            onChange={(e) => setName(e.target.value)}
                            placeholder="e.g., Action Movies 2020+"
                            disabled={isSaving}
                            error={!!localError}
                            errorMessage={localError ?? undefined}
                            autoFocus
                        />
                    </div>

                    {filterQuery && (
                        <div className="save-filter-dialog__query-preview">
                            <span className="save-filter-dialog__query-label">Query:</span>
                            <code className="save-filter-dialog__query-text">{filterQuery}</code>
                        </div>
                    )}

                    <DialogFooter>
                        <Button type="button" variant="ghost" onClick={handleCancel} disabled={isSaving}>
                            Cancel
                        </Button>
                        <Button type="submit" variant="primary" disabled={!name.trim()} loading={isSaving}>
                            Save Filter
                        </Button>
                    </DialogFooter>
                </form>
            </DialogContent>
        </Dialog>
    );
}

SaveFilterDialog.displayName = 'SaveFilterDialog';

export default SaveFilterDialog;
