import { useState, useEffect, useCallback, useRef, useMemo } from 'react';
import { FloppyDisk, X } from '@phosphor-icons/react';

import { useToggle } from '@/hooks/useToggle';
import { useUnsavedChangesGuard } from '@/hooks/useUnsavedChangesGuard';

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
import { Switch } from '@/components/ui/Switch';
import { Sheet, SheetContent, SheetHeader, SheetTitle, SheetDescription, SheetFooter } from '@/components/ui/Sheet';
import { SimpleFilterBuilder, isSimpleQuery } from '@/components/ui/SimpleFilterBuilder';
import { UnsavedChangesDialog } from '@/components/ui/UnsavedChangesDialog';
import { FilterQueryEditor } from '@/editor';
import { parseFilterQuery } from '@/lib/filterQuery';
import type { ContentFilterCriteria } from '@/hooks/useFilters';

function isContentFilterCriteria(value: unknown): value is ContentFilterCriteria {
    return typeof value === 'object' && value !== null;
}

import './FilterForm.css';
import '@/styles/mode-switch.css';

export interface FilterFormValues {
    name: string;

    isShared: boolean;

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

    onSubmit: (values: FilterFormValues) => void;

    isSubmitting: boolean;

    isOwner: boolean;

    canEdit: boolean;

    isShared: boolean;
}

type FilterMode = 'simple' | 'advanced';

interface FilterCleanState {
    name: string;
    shared: boolean;
    query: string;
}

export function FilterForm({
    open,
    onOpenChange,
    initialValues,
    onSubmit,
    isSubmitting,
    isOwner,
    canEdit,
    isShared,
}: FilterFormProps) {
    const [name, setName] = useState('');
    const [shared, setShared] = useState(false);
    const [query, setQuery] = useState('');
    const [mode, setMode] = useState<FilterMode>('simple');
    const [parseError, setParseError] = useState<string | null>(null);
    const [confirmSwitchOpen, , openConfirmSwitch, closeConfirmSwitch] = useToggle();

    const cleanStateRef = useRef<FilterCleanState>({ name: '', shared: false, query: '' });

    const [showUnsavedDialog, setShowUnsavedDialog] = useState(false);

    useEffect(() => {
        if (open) {
            if (initialValues) {
                const q = initialValues.query ?? '';
                setName(initialValues.name);
                setShared(initialValues.ownerId === null);
                setQuery(q);
                setMode(q && !isSimpleQuery(q, 'content') ? 'advanced' : 'simple');
                cleanStateRef.current = {
                    name: initialValues.name,
                    shared: initialValues.ownerId === null,
                    query: q,
                };
            } else {
                setName('');
                setShared(false);
                setQuery('');
                setMode('simple');
                cleanStateRef.current = { name: '', shared: false, query: '' };
            }
            setParseError(null);
            setShowUnsavedDialog(false);
        }
    }, [open, initialValues]);

    const isDirty = useMemo(() => {
        const clean = cleanStateRef.current;
        return name !== clean.name || shared !== clean.shared || query !== clean.query;
    }, [name, shared, query]);

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

    const handleSubmit = useCallback(
        async (e: React.FormEvent) => {
            e.preventDefault();
            if (!name.trim()) {
                return;
            }

            let criteria: ContentFilterCriteria = {};
            if (query.trim()) {
                const result = await parseFilterQuery(query, 'content');
                if (result.error) {
                    setParseError(result.error.message);
                    return;
                }
                if (isContentFilterCriteria(result.criteria)) {
                    criteria = result.criteria;
                }
            }

            setParseError(null);
            onSubmit({ name, isShared: shared, query, criteria });
        },
        [name, shared, query, onSubmit],
    );

    const isEditing = !!initialValues?.id;
    const title = isEditing ? 'Edit Filter' : 'Create Filter';
    const description = isEditing
        ? canEdit
            ? 'Modify the filter criteria to change what content matches.'
            : 'View the filter criteria (read-only).'
        : 'Define criteria to match content items.';

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
                        <div className="filter-form__field">
                            <Label htmlFor="filter-name" required>
                                Name
                            </Label>
                            <Input
                                id="filter-name"
                                value={name}
                                onChange={(e) => setName(e.target.value)}
                                placeholder="Enter filter name"
                                disabled={disabled}
                                required
                                data-field="name"
                            />
                        </div>

                        {!isEditing && (
                            <div className="filter-form__field filter-form__field--row">
                                <div className="filter-form__field-label">
                                    <Label htmlFor="filter-shared">Shared Filter</Label>
                                    <p className="filter-form__field-hint">
                                        Shared filters can be used by all users. Personal filters are only visible to
                                        you.
                                    </p>
                                </div>
                                <Switch
                                    id="filter-shared"
                                    checked={shared}
                                    onCheckedChange={setShared}
                                    disabled={isSubmitting}
                                    data-field="shared"
                                />
                            </div>
                        )}

                        {isEditing && !canEdit && (
                            <div className="filter-form__readonly-notice">
                                <X weight="bold" aria-hidden="true" />
                                <span>
                                    {isShared
                                        ? 'Only managers can edit shared filters.'
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
                                    placeholder='e.g. genre has "Action" and monitoring = Active'
                                />
                            )}
                        </div>

                        <div className="mode-switch">
                            {mode === 'simple' ? (
                                <button
                                    type="button"
                                    className="mode-switch__link"
                                    onClick={() => handleModeSwitch('advanced')}
                                    disabled={disabled}
                                    data-action="switch-to-advanced"
                                >
                                    Switch to advanced editor
                                </button>
                            ) : (
                                <button
                                    type="button"
                                    className="mode-switch__link"
                                    onClick={() => handleModeSwitch('simple')}
                                    disabled={disabled}
                                    data-action="switch-to-simple"
                                >
                                    Switch to simple mode
                                </button>
                            )}
                        </div>

                        {parseError && <div className="filter-form__error" role="alert">{parseError}</div>}
                    </div>

                    <SheetFooter>
                        <Button
                            type="button"
                            variant="ghost"
                            disabled={isSubmitting}
                            onClick={handleCancel}
                            data-action="cancel"
                        >
                            <X weight="bold" aria-hidden="true" />
                            {canEdit ? 'Cancel' : 'Close'}
                        </Button>
                        {canEdit && (
                            <Button
                                type="submit"
                                variant="primary"
                                disabled={!isValid}
                                loading={isSubmitting}
                                data-action="save-filter"
                            >
                                <FloppyDisk weight="bold" aria-hidden="true" />
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
                <DialogContent data-component="confirm-mode-switch">
                    <DialogHeader>
                        <DialogTitle>Switch to Simple Mode</DialogTitle>
                        <DialogDescription>
                            The current query uses features not available in simple mode. Switching will reset the
                            query.
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
        </Sheet>
    );
}

FilterForm.displayName = 'FilterForm';

export default FilterForm;
