import { useState, useEffect, useCallback, useRef } from 'react';
import { FloppyDiskIcon, XIcon, CodeIcon, SlidersIcon } from '@phosphor-icons/react';

import { useToggle } from '@/hooks/useToggle';
import { useUnsavedChangesGuard } from '@/hooks/useUnsavedChangesGuard';
import { useFormDirty } from '@/hooks/useFormDirty';
import { useExternalChangeConflict } from '@/hooks/useExternalChangeConflict';

import { Button } from '@/components/ui/Button';
import {
    Dialog,
    DialogContent,
    DialogDescription,
    DialogFooter,
    DialogHeader,
    DialogTitle,
} from '@/components/ui/Dialog';
import { Input } from '@/components/ui/Input';
import { Label } from '@/components/ui/Label';
import { Sheet, SheetContent, SheetHeader, SheetTitle, SheetDescription, SheetFooter } from '@/components/ui/Sheet';
import { SimpleFilterBuilder } from '@/components/filters/SimpleFilterBuilder';
import { UnsavedChangesDialog } from '@/components/ui/UnsavedChangesDialog';
import { ExternalChangeConflictWarning, StaleSaveConfirmationDialog } from '@/components/ui/ExternalChangeConflict';
import { FilterQueryEditor } from '@/editor';
import { hasFilterQuerySyntaxErrors } from '@/editor/validation';
import { getErrorMessage } from '@/lib/errors';
import { parseMagpieQl } from '@/lib/magpieQl';
import { isSimpleQuery } from '@/lib/simpleFilterQuery';
import type { ContentFilterCriteria } from '@/hooks/useFilters';

import './FilterForm.css';
import '@/styles/mode-switch.css';

function isContentFilterCriteria(value: unknown): value is ContentFilterCriteria {
    return typeof value === 'object' && value !== null;
}

export interface FilterFormValues {
    name: string;

    query: string;

    criteria: ContentFilterCriteria;
}

export interface FilterFormProps {
    open: boolean;

    onOpenChange: (open: boolean) => void;

    initialValues: {
        id: string;
        name: string;
        ownerId: string | null;
        query?: string;
    } | null;

    onSubmit: (values: FilterFormValues) => void | Promise<void>;

    isSubmitting: boolean;

    isOwner: boolean;

    canEdit: boolean;

    isShared: boolean;

    existingNames?: string[];
}

type FilterMode = 'simple' | 'advanced';

interface FilterCleanState {
    name: string;
    query: string;
}

const EMPTY_EXISTING_NAMES: string[] = [];

export function FilterForm({
    open,
    onOpenChange,
    initialValues,
    onSubmit,
    isSubmitting,
    isOwner,
    canEdit,
    isShared,
    existingNames = EMPTY_EXISTING_NAMES,
}: FilterFormProps) {
    const [name, setName] = useState('');
    const [query, setQuery] = useState('');
    const [mode, setMode] = useState<FilterMode>('simple');
    const [parseError, setParseError] = useState<string | null>(null);
    const [nameError, setNameError] = useState<string | null>(null);
    const [confirmSwitchOpen, , openConfirmSwitch, closeConfirmSwitch] = useToggle();

    const cleanStateRef = useRef<FilterCleanState>({ name: '', query: '' });

    const [showUnsavedDialog, setShowUnsavedDialog] = useState(false);
    const [showStaleSaveConfirmation, setShowStaleSaveConfirmation] = useState(false);
    const loadedEntityIdRef = useRef<string | null | undefined>(undefined);
    const isDirty = useFormDirty({ name, query }, cleanStateRef.current);
    const isDirtyRef = useRef(isDirty);
    isDirtyRef.current = isDirty;

    useEffect(() => {
        if (!open) {
            loadedEntityIdRef.current = undefined;
            return;
        }
        const nextEntityId = initialValues?.id ?? null;
        if (loadedEntityIdRef.current === nextEntityId && isDirtyRef.current) {
            return;
        }
        if (initialValues) {
            const q = initialValues.query ?? '';
            setName(initialValues.name);
            setQuery(q);
            setMode(q && !isSimpleQuery(q, 'content') ? 'advanced' : 'simple');
            cleanStateRef.current = { name: initialValues.name, query: q };
        } else {
            setName('');
            setQuery('');
            setMode('simple');
            cleanStateRef.current = { name: '', query: '' };
        }
        loadedEntityIdRef.current = nextEntityId;
        setParseError(null);
        setNameError(null);
        setShowUnsavedDialog(false);
        setShowStaleSaveConfirmation(false);
    }, [open, initialValues]);

    const isEditing = !!initialValues?.id;
    const conflict = useExternalChangeConflict({
        dirty: isDirty,
        entityId: initialValues?.id,
        eventNames: ['contentFilter.updated', 'contentFilter.deleted'],
        deletedEventNames: ['contentFilter.deleted'],
        contextKeys: ['contentFilter.id'],
        enabled: open && isEditing && canEdit,
    });

    useUnsavedChangesGuard(open && isDirty && canEdit);

    const handleOpenChange = useCallback(
        (nextOpen: boolean) => {
            if (!nextOpen && isDirty && canEdit) {
                setShowUnsavedDialog(true);
                return;
            }
            onOpenChange(nextOpen);
        },
        [isDirty, canEdit, onOpenChange],
    );

    const handleDiscard = useCallback(() => {
        setShowUnsavedDialog(false);
        onOpenChange(false);
    }, [onOpenChange]);

    const handleKeepEditing = useCallback(() => {
        setShowUnsavedDialog(false);
    }, []);

    const handleCancel = useCallback(() => {
        handleOpenChange(false);
    }, [handleOpenChange]);

    const handleModeSwitch = useCallback(
        (newMode: FilterMode) => {
            if (newMode === 'simple' && query && !isSimpleQuery(query, 'content')) {
                openConfirmSwitch();
                return;
            }
            setMode(newMode);
        },
        [query, openConfirmSwitch],
    );

    const handleConfirmSwitch = useCallback(() => {
        setQuery('');
        setMode('simple');
        closeConfirmSwitch();
    }, [closeConfirmSwitch]);

    const save = useCallback(async () => {
        const trimmedName = name.trim();
        if (!trimmedName) {
            return;
        }

        const isDuplicate = existingNames.some(
            (existing) => existing.trim().toLowerCase() === trimmedName.toLowerCase(),
        );
        if (isDuplicate) {
            setNameError('A filter with this name already exists. Choose a different name.');
            return;
        }

        let criteria: ContentFilterCriteria = {};
        if (query.trim()) {
            if (hasFilterQuerySyntaxErrors(query)) {
                setParseError('Fix the query syntax before saving.');
                return;
            }

            const result = await parseMagpieQl(query, 'content').catch((error: unknown) => {
                setParseError(getErrorMessage(error, 'Failed to validate the query.'));
                return null;
            });
            if (!result) {
                return;
            }
            if (result.error) {
                setParseError(result.error.message);
                return;
            }
            if (isContentFilterCriteria(result.criteria)) {
                criteria = result.criteria;
            }
        }

        setParseError(null);
        setNameError(null);
        conflict.beginOwnSave();
        try {
            await onSubmit({ name: trimmedName, query, criteria });
            conflict.endOwnSave(true);
        } catch {
            conflict.endOwnSave(false);
        }
    }, [conflict, existingNames, name, onSubmit, query]);

    const handleSubmit = useCallback(
        (e: React.SubmitEvent) => {
            e.preventDefault();
            if (conflict.state === 'deleted') {
                return;
            }
            if (conflict.state === 'stale') {
                setShowStaleSaveConfirmation(true);
                return;
            }
            void save();
        },
        [conflict.state, save],
    );

    const title = isEditing ? 'Edit Filter' : 'Create Filter';
    const description = isEditing
        ? canEdit
            ? 'Modify the filter criteria to change what content matches.'
            : 'View the filter criteria (read-only).'
        : 'Define criteria to match hierarchy items.';

    const isValid = name.trim().length > 0;
    const disabled = !canEdit || isSubmitting;

    return (
        <Sheet open={open} onOpenChange={handleOpenChange}>
            <SheetContent side="right" className="filter-form__sheet" data-component="filter-form">
                <form onSubmit={handleSubmit} className="filter-form">
                    <SheetHeader>
                        <SheetTitle>{title}</SheetTitle>
                        <SheetDescription>{description}</SheetDescription>
                    </SheetHeader>

                    <div className="filter-form__body">
                        {conflict.state && (
                            <ExternalChangeConflictWarning
                                state={conflict.state}
                                entityLabel="Filter"
                                id="filter-external-change"
                            />
                        )}
                        <div className="filter-form__field">
                            <Label htmlFor="filter-name" required>
                                Name
                            </Label>
                            <Input
                                id="filter-name"
                                value={name}
                                onChange={(e) => {
                                    setName(e.target.value);
                                    if (nameError) {
                                        setNameError(null);
                                    }
                                }}
                                placeholder="Enter filter name"
                                disabled={disabled}
                                required
                                error={!!nameError}
                                errorMessage={nameError ?? undefined}
                                data-field="name"
                            />
                        </div>

                        {isEditing && !canEdit && (
                            <div className="filter-form__readonly-notice">
                                <XIcon weight="bold" aria-hidden="true" />
                                <span>
                                    {isShared
                                        ? 'Only admins can edit shared filters.'
                                        : isOwner
                                          ? 'This filter is locked.'
                                          : 'Only the owner can edit this filter.'}
                                </span>
                            </div>
                        )}

                        <div className="filter-form__criteria">
                            {mode === 'simple' ? (
                                <SimpleFilterBuilder
                                    value={query}
                                    onChange={setQuery}
                                    context="content"
                                    readOnly={disabled}
                                    loading={isSubmitting}
                                />
                            ) : (
                                <FilterQueryEditor
                                    value={query}
                                    onChange={setQuery}
                                    context="content"
                                    readOnly={disabled}
                                    placeholder='genre has "Action" and monitoring != Unmonitored'
                                />
                            )}
                        </div>

                        <div className="mode-switch">
                            {mode === 'simple' ? (
                                <Button
                                    variant="ghost"
                                    size="sm"
                                    onClick={() => handleModeSwitch('advanced')}
                                    disabled={disabled}
                                    data-action="switch-to-advanced"
                                >
                                    <CodeIcon weight="bold" aria-hidden="true" />
                                    Advanced filter
                                </Button>
                            ) : (
                                <Button
                                    variant="ghost"
                                    size="sm"
                                    onClick={() => handleModeSwitch('simple')}
                                    disabled={disabled}
                                    data-action="switch-to-simple"
                                >
                                    <SlidersIcon weight="bold" aria-hidden="true" />
                                    Simple filter
                                </Button>
                            )}
                        </div>

                        {parseError && (
                            <div className="filter-form__error" role="alert">
                                {parseError}
                            </div>
                        )}
                    </div>

                    <SheetFooter>
                        <Button
                            type="button"
                            variant="ghost"
                            disabled={isSubmitting}
                            onClick={handleCancel}
                            data-action="cancel"
                        >
                            <XIcon weight="bold" aria-hidden="true" />
                            {canEdit ? 'Cancel' : 'Close'}
                        </Button>
                        {canEdit && (
                            <Button
                                type="submit"
                                variant="primary"
                                disabled={!isValid || conflict.state === 'deleted'}
                                loading={isSubmitting}
                                data-action="save-filter"
                            >
                                <FloppyDiskIcon weight="bold" aria-hidden="true" />
                                Save Filter
                            </Button>
                        )}
                    </SheetFooter>
                </form>
            </SheetContent>

            <Dialog
                open={confirmSwitchOpen}
                onOpenChange={(o) => {
                    if (!o) closeConfirmSwitch();
                }}
            >
                <DialogContent size="alert" data-component="confirm-mode-switch">
                    <DialogHeader>
                        <DialogTitle>Switch to Simple Mode</DialogTitle>
                        <DialogDescription>
                            This query uses options that simple mode cannot represent. Switching modes will clear it.
                        </DialogDescription>
                    </DialogHeader>
                    <DialogFooter>
                        <Button type="button" variant="ghost" onClick={closeConfirmSwitch} data-action="cancel">
                            Cancel
                        </Button>
                        <Button
                            type="button"
                            variant="primary"
                            onClick={handleConfirmSwitch}
                            data-action="confirm-switch"
                        >
                            Switch Mode
                        </Button>
                    </DialogFooter>
                </DialogContent>
            </Dialog>

            <UnsavedChangesDialog open={showUnsavedDialog} onDiscard={handleDiscard} onCancel={handleKeepEditing} />
            <StaleSaveConfirmationDialog
                open={showStaleSaveConfirmation}
                title="Save changes based on older data?"
                description="This item changed after editing began. Saving will overwrite the newer changes."
                saving={isSubmitting}
                onCancel={() => setShowStaleSaveConfirmation(false)}
                onConfirm={() => {
                    setShowStaleSaveConfirmation(false);
                    void save();
                }}
            />
        </Sheet>
    );
}

FilterForm.displayName = 'FilterForm';

export default FilterForm;
