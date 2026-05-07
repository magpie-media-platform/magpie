import { useState, useCallback, useEffect, useRef, useMemo, type ReactNode } from 'react';
import { FloppyDisk, Plus, Trash, X } from '@phosphor-icons/react';

import { Button } from '@/components/ui/Button';
import { Input } from '@/components/ui/Input';
import { Label } from '@/components/ui/Label';
import { Sheet, SheetContent, SheetHeader, SheetTitle, SheetDescription, SheetFooter } from '@/components/ui/Sheet';
import { Switch } from '@/components/ui/Switch';
import { UnsavedChangesDialog } from '@/components/ui/UnsavedChangesDialog';
import { useUnsavedChangesGuard } from '@/hooks/useUnsavedChangesGuard';

import './IndexerFeedForm.css';

export interface IndexerFeedFormValues {
    id?: string;
    name: string;
    description: string;
    provider: string;
    enabled: boolean;
    settings: Record<string, string>;
}

export interface IndexerFeedFormProps {
    open: boolean;

    onOpenChange: (open: boolean) => void;

    initialValues?: IndexerFeedFormValues | null;

    onSubmit: (values: IndexerFeedFormValues) => void;

    isSubmitting?: boolean;
}

interface SettingsEntry {
    id: string;
    key: string;
    value: string;
}

const emptyValues: IndexerFeedFormValues = {
    name: '',
    description: '',
    provider: '',
    enabled: true,
    settings: {},
};

function settingsToEntries(settings: Record<string, string>): SettingsEntry[] {
    return Object.entries(settings).map(([key, value]) => ({ id: crypto.randomUUID(), key, value }));
}

function entriesToSettings(entries: SettingsEntry[]): Record<string, string> {
    const result: Record<string, string> = {};
    for (const entry of entries) {
        const trimmed = entry.key.trim();
        if (trimmed) {
            result[trimmed] = entry.value;
        }
    }
    return result;
}

interface SettingsRowProps {
    entryKey: string;
    value: string;
    onKeyChange: (newKey: string) => void;
    onValueChange: (newValue: string) => void;
    onRemove: () => void;
    disabled?: boolean;

    keyError?: string;
}

function SettingsRow({ entryKey, value, onKeyChange, onValueChange, onRemove, disabled, keyError }: SettingsRowProps) {
    return (
        <div className="indexer-feed-form__settings-row">
            <Input
                value={entryKey}
                onChange={(e) => onKeyChange(e.target.value)}
                placeholder="Key"
                disabled={disabled}
                className="indexer-feed-form__settings-key"
                error={!!keyError}
                errorMessage={keyError}
            />
            <Input
                value={value}
                onChange={(e) => onValueChange(e.target.value)}
                placeholder="Value"
                disabled={disabled}
                className="indexer-feed-form__settings-value"
            />
            <button
                type="button"
                className="indexer-feed-form__settings-remove"
                onClick={onRemove}
                disabled={disabled}
                aria-label="Remove setting"
            >
                <Trash weight="bold" />
            </button>
        </div>
    );
}

export function IndexerFeedForm({
    open,
    onOpenChange,
    initialValues,
    onSubmit,
    isSubmitting = false,
}: IndexerFeedFormProps): ReactNode {
    const isEditing = !!initialValues?.id;

    const [values, setValues] = useState<IndexerFeedFormValues>(initialValues ?? emptyValues);
    const [settingsEntries, setSettingsEntries] = useState<SettingsEntry[]>([]);
    const [errors, setErrors] = useState<Record<string, string>>({});
    const [settingsErrors, setSettingsErrors] = useState<Record<string, string>>({});
    const [showUnsavedDialog, setShowUnsavedDialog] = useState(false);

    const cleanValuesRef = useRef<IndexerFeedFormValues>(initialValues ?? emptyValues);
    const cleanSettingsEntriesRef = useRef<SettingsEntry[]>([]);

    useEffect(() => {
        if (open) {
            const vals = initialValues ?? emptyValues;
            setValues(vals);
            const entries = settingsToEntries(vals.settings);
            setSettingsEntries(entries);
            cleanValuesRef.current = vals;
            cleanSettingsEntriesRef.current = entries;
            setErrors({});
            setSettingsErrors({});
            setShowUnsavedDialog(false);
        }
    }, [open, initialValues]);

    const isDirty = useMemo(() => {
        const clean = cleanValuesRef.current;
        if (
            values.name !== clean.name ||
            values.description !== clean.description ||
            values.provider !== clean.provider ||
            values.enabled !== clean.enabled
        ) {
            return true;
        }
        const cleanEntries = cleanSettingsEntriesRef.current;
        if (settingsEntries.length !== cleanEntries.length) {
            return true;
        }
        return settingsEntries.some(
            (entry, i) => entry.key !== cleanEntries[i].key || entry.value !== cleanEntries[i].value,
        );
    }, [values, settingsEntries]);

    useUnsavedChangesGuard(open && isDirty);

    const handleOpenChange = useCallback(
        (nextOpen: boolean) => {
            if (!nextOpen && isDirty) {
                setShowUnsavedDialog(true);
                return;
            }
            onOpenChange(nextOpen);
        },
        [isDirty, onOpenChange],
    );

    const handleCancel = useCallback(() => {
        handleOpenChange(false);
    }, [handleOpenChange]);

    const handleDiscard = useCallback(() => {
        setShowUnsavedDialog(false);
        onOpenChange(false);
    }, [onOpenChange]);

    const handleKeepEditing = useCallback(() => {
        setShowUnsavedDialog(false);
    }, []);

    const handleNameChange = useCallback((e: React.ChangeEvent<HTMLInputElement>) => {
        setValues((prev) => ({ ...prev, name: e.target.value }));
        setErrors((prev) => ({ ...prev, name: '' }));
    }, []);

    const handleDescriptionChange = useCallback((e: React.ChangeEvent<HTMLInputElement>) => {
        setValues((prev) => ({ ...prev, description: e.target.value }));
    }, []);

    const handleProviderChange = useCallback((e: React.ChangeEvent<HTMLInputElement>) => {
        setValues((prev) => ({ ...prev, provider: e.target.value }));
        setErrors((prev) => ({ ...prev, provider: '' }));
    }, []);

    const handleEnabledChange = useCallback((enabled: boolean) => {
        setValues((prev) => ({ ...prev, enabled }));
    }, []);

    const handleAddEntry = useCallback(() => {
        setSettingsEntries((prev) => [...prev, { id: crypto.randomUUID(), key: '', value: '' }]);
    }, []);

    const handleEntryKeyChange = useCallback((index: number, newKey: string) => {
        setSettingsEntries((prev) => {
            const updated = prev.map((entry, i) => (i === index ? { ...entry, key: newKey } : entry));
            const entryId = prev[index].id;
            setSettingsErrors((prevErrors) => {
                if (!prevErrors[entryId]) {
                    return prevErrors;
                }
                const { [entryId]: _, ...rest } = prevErrors;
                return rest;
            });
            return updated;
        });
    }, []);

    const handleEntryValueChange = useCallback((index: number, newValue: string) => {
        setSettingsEntries((prev) => prev.map((entry, i) => (i === index ? { ...entry, value: newValue } : entry)));
    }, []);

    const handleRemoveEntry = useCallback((index: number) => {
        setSettingsEntries((prev) => prev.filter((_, i) => i !== index));
    }, []);

    const validate = useCallback((): boolean => {
        const newErrors: Record<string, string> = {};

        if (!values.name.trim()) {
            newErrors.name = 'Name is required';
        }

        if (!values.provider.trim()) {
            newErrors.provider = 'Provider is required';
        }

        const newSettingsErrors: Record<string, string> = {};
        const seenKeys = new Map<string, string>();
        for (const entry of settingsEntries) {
            const trimmed = entry.key.trim();
            if (!trimmed) {
                continue;
            }
            const existing = seenKeys.get(trimmed);
            if (existing) {
                newSettingsErrors[entry.id] = 'Duplicate key';
                newSettingsErrors[existing] = 'Duplicate key';
            } else {
                seenKeys.set(trimmed, entry.id);
            }
        }

        setErrors(newErrors);
        setSettingsErrors(newSettingsErrors);
        return Object.keys(newErrors).length === 0 && Object.keys(newSettingsErrors).length === 0;
    }, [values, settingsEntries]);

    const handleSubmit = useCallback(
        (e: React.FormEvent) => {
            e.preventDefault();
            if (validate()) {
                onSubmit({
                    ...values,
                    settings: entriesToSettings(settingsEntries),
                });
            }
        },
        [values, settingsEntries, validate, onSubmit],
    );

    return (
        <Sheet open={open} onOpenChange={handleOpenChange}>
            <SheetContent side="right" className="indexer-feed-form__sheet" data-component="indexer-feed-form">
                <SheetHeader>
                    <SheetTitle>{isEditing ? 'Edit Feed' : 'Add Feed'}</SheetTitle>
                    <SheetDescription>
                        {isEditing
                            ? 'Update the indexer feed configuration.'
                            : 'Configure a new feed to sync indexer definitions.'}
                    </SheetDescription>
                </SheetHeader>

                <form onSubmit={handleSubmit} className="indexer-feed-form">
                    <div className="indexer-feed-form__body">
                        <div className="indexer-feed-form__field">
                            <Label htmlFor="feed-name" error={!!errors.name} required>
                                Name
                            </Label>
                            <Input
                                id="feed-name"
                                value={values.name}
                                onChange={handleNameChange}
                                placeholder="My Feed"
                                error={!!errors.name}
                                errorMessage={errors.name || undefined}
                                disabled={isSubmitting}
                                data-field="name"
                            />
                        </div>

                        <div className="indexer-feed-form__field">
                            <Label htmlFor="feed-description">Description</Label>
                            <Input
                                id="feed-description"
                                value={values.description}
                                onChange={handleDescriptionChange}
                                placeholder="Optional description"
                                disabled={isSubmitting}
                                data-field="description"
                            />
                        </div>

                        <div className="indexer-feed-form__field">
                            <Label htmlFor="feed-provider" error={!!errors.provider} required>
                                Provider
                            </Label>
                            <Input
                                id="feed-provider"
                                value={values.provider}
                                onChange={handleProviderChange}
                                placeholder="prowlarr"
                                error={!!errors.provider}
                                errorMessage={errors.provider || undefined}
                                disabled={isSubmitting}
                                data-field="provider"
                            />
                            <p className="indexer-feed-form__help">The feed provider type (e.g., prowlarr, jackett).</p>
                        </div>

                        <div className="indexer-feed-form__field indexer-feed-form__field--toggle">
                            <div className="indexer-feed-form__toggle-wrapper">
                                <Switch
                                    id="feed-enabled"
                                    checked={values.enabled}
                                    onCheckedChange={handleEnabledChange}
                                    disabled={isSubmitting}
                                    data-field="enabled"
                                />
                                <Label htmlFor="feed-enabled">Enabled</Label>
                            </div>
                            <p className="indexer-feed-form__help">Disabled feeds will not be synced.</p>
                        </div>

                        <div className="indexer-feed-form__settings">
                            <h3 className="indexer-feed-form__settings-title">Settings</h3>
                            {settingsEntries.map((entry, index) => (
                                <SettingsRow
                                    key={entry.id}
                                    entryKey={entry.key}
                                    value={entry.value}
                                    onKeyChange={(newKey) => handleEntryKeyChange(index, newKey)}
                                    onValueChange={(newValue) => handleEntryValueChange(index, newValue)}
                                    onRemove={() => handleRemoveEntry(index)}
                                    disabled={isSubmitting}
                                    keyError={settingsErrors[entry.id]}
                                />
                            ))}
                            <Button
                                type="button"
                                variant="ghost"
                                size="sm"
                                onClick={handleAddEntry}
                                disabled={isSubmitting}
                                className="indexer-feed-form__settings-add"
                                data-action="add-setting"
                            >
                                <Plus weight="bold" aria-hidden="true" />
                                Add Setting
                            </Button>
                        </div>
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
                            Cancel
                        </Button>
                        <Button type="submit" variant="primary" loading={isSubmitting} data-action="save">
                            <FloppyDisk weight="bold" aria-hidden="true" />
                            Save
                        </Button>
                    </SheetFooter>
                </form>
            </SheetContent>
            <UnsavedChangesDialog open={showUnsavedDialog} onDiscard={handleDiscard} onCancel={handleKeepEditing} />
        </Sheet>
    );
}

IndexerFeedForm.displayName = 'IndexerFeedForm';

export default IndexerFeedForm;
