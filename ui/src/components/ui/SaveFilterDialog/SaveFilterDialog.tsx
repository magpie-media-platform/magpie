import { useState, useId, useCallback, useEffect, type SubmitEvent } from 'react';
import { FloppyDiskIcon } from '@phosphor-icons/react';

import { Dialog, DialogContent, DialogHeader, DialogTitle, DialogDescription, DialogFooter } from '../Dialog';
import { Alert } from '../Alert';
import { Input } from '../Input';
import { Button } from '../Button';
import { Label } from '../Label';

import './SaveFilterDialog.css';

export interface SaveFilterDialogProps {
    open: boolean;

    onOpenChange: (open: boolean) => void;

    onSave: (name: string) => void | Promise<void>;

    onUpdate?: () => void | Promise<void>;

    updateTargetName?: string;

    isSaving?: boolean;

    isUpdating?: boolean;

    filterQuery?: string;

    nameError?: string | null;

    queryError?: string | null;

    existingNames?: string[];
}

const EMPTY_EXISTING_NAMES: string[] = [];

export function SaveFilterDialog({
    open,
    onOpenChange,
    onSave,
    onUpdate,
    updateTargetName,
    isSaving = false,
    isUpdating = false,
    filterQuery,
    nameError,
    queryError,
    existingNames = EMPTY_EXISTING_NAMES,
}: SaveFilterDialogProps) {
    const inputId = useId();
    const [name, setName] = useState('');
    const [localError, setLocalError] = useState<string | null>(null);

    const canUpdate = !!onUpdate && !!updateTargetName;
    const busy = isSaving || isUpdating;

    useEffect(() => {
        if (open) {
            setName('');
            setLocalError(null);
        }
    }, [open]);

    useEffect(() => {
        setLocalError(nameError ?? null);
    }, [nameError]);

    const handleSubmit = useCallback(
        async (e: SubmitEvent) => {
            e.preventDefault();

            const trimmedName = name.trim();
            if (!trimmedName) {
                setLocalError('Please enter a name for the filter.');
                return;
            }

            const isDuplicate = existingNames.some(
                (existing) => existing.trim().toLowerCase() === trimmedName.toLowerCase(),
            );
            if (isDuplicate) {
                setLocalError('A filter with this name already exists. Choose a different name.');
                return;
            }

            setLocalError(null);
            await onSave(trimmedName);
        },
        [name, onSave, existingNames],
    );

    const handleCancel = useCallback(() => {
        onOpenChange(false);
    }, [onOpenChange]);

    const handleUpdate = useCallback(async () => {
        if (onUpdate) {
            await onUpdate();
        }
    }, [onUpdate]);

    return (
        <Dialog open={open} onOpenChange={onOpenChange}>
            <DialogContent className="save-filter-dialog" data-component="save-filter-dialog">
                <DialogHeader>
                    <DialogTitle>
                        <FloppyDiskIcon weight="duotone" className="save-filter-dialog__icon" aria-hidden="true" />
                        {canUpdate ? 'Save Filter Changes' : 'Save Filter'}
                    </DialogTitle>
                    <DialogDescription>
                        {canUpdate
                            ? `Update "${updateTargetName}" with the current query, or save it as a new filter.`
                            : 'Give your filter a name so you can use it again later.'}
                    </DialogDescription>
                </DialogHeader>

                <form onSubmit={handleSubmit} className="save-filter-dialog__form">
                    <div className="save-filter-dialog__field">
                        <Label htmlFor={inputId}>{canUpdate ? 'New Filter Name' : 'Filter Name'}</Label>
                        <Input
                            id={inputId}
                            type="text"
                            value={name}
                            onChange={(e) => setName(e.target.value)}
                            placeholder="Favorites from 2020"
                            disabled={busy}
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

                    {queryError && (
                        <Alert variant="error" title="The filter query is not valid">
                            {queryError}
                        </Alert>
                    )}

                    <DialogFooter className="save-filter-dialog__footer">
                        <Button type="button" variant="ghost" onClick={handleCancel} disabled={busy}>
                            Cancel
                        </Button>
                        {canUpdate && (
                            <Button
                                type="button"
                                variant="secondary"
                                onClick={handleUpdate}
                                loading={isUpdating}
                                disabled={isSaving}
                                data-action="update-saved-filter"
                            >
                                Update "{updateTargetName}"
                            </Button>
                        )}
                        <Button
                            type="submit"
                            variant="primary"
                            disabled={!name.trim() || isUpdating}
                            loading={isSaving}
                            data-action="save-new-filter"
                        >
                            {canUpdate ? 'Save as New' : 'Save Filter'}
                        </Button>
                    </DialogFooter>
                </form>
            </DialogContent>
        </Dialog>
    );
}

SaveFilterDialog.displayName = 'SaveFilterDialog';

export default SaveFilterDialog;
