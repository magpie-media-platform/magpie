import { useState, useCallback, useEffect, useMemo, useRef, type ReactNode } from 'react';
import { FloppyDiskIcon, XIcon } from '@phosphor-icons/react';

import { useUnsavedChangesGuard } from '@/hooks/useUnsavedChangesGuard';
import { useExternalChangeConflict } from '@/hooks/useExternalChangeConflict';

import { Button } from '@/components/ui/Button';
import { Input } from '@/components/ui/Input';
import { Label } from '@/components/ui/Label';
import { PathInput } from '@/components/ui/PathInput';
import { Select, SelectTrigger, SelectContent, SelectItem, SelectGroup, SelectLabel } from '@/components/ui/Select';
import { Sheet, SheetContent, SheetHeader, SheetTitle, SheetDescription, SheetFooter } from '@/components/ui/Sheet';
import { Switch } from '@/components/ui/Switch';
import { UnsavedChangesDialog } from '@/components/ui/UnsavedChangesDialog';
import { ExternalChangeConflictWarning, StaleSaveConfirmationDialog } from '@/components/ui/ExternalChangeConflict';
import type { LibraryLevelDefinition, LibraryTypeDefinition } from '@/hooks/useLibraryTypes';
import { useQualityTierLists } from '@/hooks/useQualityTierLists';
import type { RootFolderDefault, UpgradeFileHandling } from '@/hooks/useRootFolders';

import './RootFolderForm.css';

export interface RootFolderFormValues {
    id?: string;
    name: string;
    path: string;
    contentType: string;
    isDefault: boolean;
    defaultMonitoring: boolean | null;
    renameTemplate: string;
    upgradeFileHandling: UpgradeFileHandling;
    defaults: Record<string, RootFolderDefault>;
}

export interface QualityTierListOption {
    id: string;
    name: string;
    mimeTypes?: string[];
}

interface RootFolderFormProps {
    open: boolean;

    onOpenChange: (open: boolean) => void;

    initialValues?: RootFolderFormValues | null;

    contentTypes: LibraryTypeDefinition[];

    qualityTierLists?: QualityTierListOption[];

    onSubmit: (values: RootFolderFormValues) => void | Promise<void>;

    isSubmitting?: boolean;
}

const NO_QUALITY_PROFILE_VALUE = '__none__';
const upgradeFileHandlingOptions: { value: UpgradeFileHandling; label: string; description: string }[] = [
    {
        value: 'Replace',
        label: 'Replace',
        description: 'Delete the previous lower-quality file after importing an upgrade.',
    },
    {
        value: 'KeepBoth',
        label: 'Keep both',
        description: 'Keep the previous lower-quality file after importing an upgrade.',
    },
];

const emptyValues: RootFolderFormValues = {
    name: '',
    path: '',
    contentType: '',
    isDefault: false,
    defaultMonitoring: null,
    renameTemplate: '',
    upgradeFileHandling: 'Replace',
    defaults: {},
};

const DEFAULT_RENAME_TEMPLATE_SUGGESTION = '{Title} ({Year})/{Title} ({Year}).{Ext}';

function describeMimePatterns(patterns: string[] | undefined): string {
    return patterns?.length ? patterns.join(', ') : 'Any MIME type';
}

interface LevelDefaultPickerProps {
    contentType: string;
    level: LibraryLevelDefinition;
    value: string;
    required: boolean;
    simple: boolean;
    disabled: boolean;
    qualityTierLists?: QualityTierListOption[];
    error?: string;
    onChange: (levelSlug: string, qualityTierListId: string) => void;
}

function LevelDefaultPicker({
    contentType,
    level,
    value,
    required,
    simple,
    disabled,
    qualityTierLists,
    error,
    onChange,
}: LevelDefaultPickerProps) {
    const { tierLists: applicableTierLists, isLoading } = useQualityTierLists({
        contentType,
        level: level.slug,
        enabled: qualityTierLists === undefined && !!contentType,
    });
    const availableTierLists = qualityTierLists ?? applicableTierLists;
    const applicableSelection = availableTierLists.find((tierList) => tierList.id === value);
    const { tierLists: allTierLists, isLoading: allTierListsLoading } = useQualityTierLists({
        enabled: qualityTierLists === undefined && !!value && !applicableSelection,
    });
    const selectedTierList = applicableSelection ?? allTierLists.find((tierList) => tierList.id === value);
    const fieldId = `folder-default-quality-${level.slug}`;
    const acceptedMimeTypes = level.effectiveAcceptedMimeTypes ?? [];

    return (
        <div className="root-folder-form__level-default" data-level={level.slug}>
            <Label htmlFor={fieldId} error={!!error} required={required}>
                {simple ? 'Default Quality Tiers' : `${level.displayName} (${level.slug})`}
            </Label>
            <p className="root-folder-form__help">Accepts: {describeMimePatterns(acceptedMimeTypes)}</p>
            <Select
                value={value || NO_QUALITY_PROFILE_VALUE}
                onValueChange={(next) => onChange(level.slug, next === NO_QUALITY_PROFILE_VALUE ? '' : next)}
                disabled={disabled}
            >
                <SelectTrigger
                    id={fieldId}
                    placeholder="Select tier list..."
                    error={!!error}
                    aria-describedby={error ? `${fieldId}-error` : undefined}
                    data-field="default-quality-profile"
                    data-level={level.slug}
                >
                    {selectedTierList
                        ? `${selectedTierList.name} - ${describeMimePatterns(selectedTierList.mimeTypes)}`
                        : value
                          ? allTierListsLoading
                              ? 'Loading...'
                              : 'Unknown tier list'
                          : required
                            ? isLoading
                                ? 'Loading...'
                                : 'Select tier list...'
                            : 'Inherit parent level'}
                </SelectTrigger>
                <SelectContent>
                    {!required && <SelectItem value={NO_QUALITY_PROFILE_VALUE}>Inherit parent level</SelectItem>}
                    {availableTierLists.length > 0 && (
                        <SelectGroup>
                            <SelectLabel>Compatible tier lists</SelectLabel>
                            {availableTierLists.map((tierList) => (
                                <SelectItem key={tierList.id} value={tierList.id}>
                                    {tierList.name} - {describeMimePatterns(tierList.mimeTypes)}
                                </SelectItem>
                            ))}
                        </SelectGroup>
                    )}
                </SelectContent>
            </Select>
            {selectedTierList && (
                <p className="root-folder-form__help">
                    Selected list accepts: {describeMimePatterns(selectedTierList.mimeTypes)}
                </p>
            )}
            {error && (
                <p id={`${fieldId}-error`} className="form-error" role="alert">
                    {error}
                </p>
            )}
        </div>
    );
}

export function RootFolderForm({
    open,
    onOpenChange,
    initialValues,
    contentTypes,
    qualityTierLists,
    onSubmit,
    isSubmitting = false,
}: RootFolderFormProps): ReactNode {
    const isEditing = !!initialValues?.id;
    const startingValues = initialValues ? { ...initialValues, defaults: initialValues.defaults ?? {} } : emptyValues;

    const [values, setValues] = useState<RootFolderFormValues>(startingValues);
    const [errors, setErrors] = useState<Record<string, string>>({});
    const [renameEnabled, setRenameEnabled] = useState<boolean>(
        (initialValues?.renameTemplate ?? '').trim().length > 0,
    );

    const cleanValuesRef = useRef<RootFolderFormValues>(startingValues);
    const cleanRenameEnabledRef = useRef<boolean>((initialValues?.renameTemplate ?? '').trim().length > 0);
    const [showUnsavedDialog, setShowUnsavedDialog] = useState(false);
    const [showStaleSaveDialog, setShowStaleSaveDialog] = useState(false);
    const wasOpenRef = useRef(false);

    const isDirty = useMemo(
        () =>
            values.name !== cleanValuesRef.current.name ||
            values.path !== cleanValuesRef.current.path ||
            values.contentType !== cleanValuesRef.current.contentType ||
            values.isDefault !== cleanValuesRef.current.isDefault ||
            values.defaultMonitoring !== cleanValuesRef.current.defaultMonitoring ||
            values.renameTemplate !== cleanValuesRef.current.renameTemplate ||
            values.upgradeFileHandling !== cleanValuesRef.current.upgradeFileHandling ||
            JSON.stringify(values.defaults) !== JSON.stringify(cleanValuesRef.current.defaults) ||
            renameEnabled !== cleanRenameEnabledRef.current,
        [values, renameEnabled],
    );

    const {
        state: conflictState,
        beginOwnSave,
        endOwnSave,
        clearConflict,
    } = useExternalChangeConflict({
        dirty: isDirty,
        entityId: initialValues?.id,
        eventNames: ['library.rootFolder.updated', 'library.rootFolder.deleted'],
        deletedEventNames: ['library.rootFolder.deleted'],
        contextKeys: ['rootFolder.id'],
        enabled: open,
    });

    useEffect(() => {
        const justOpened = open && !wasOpenRef.current;
        wasOpenRef.current = open;
        if (!open) {
            return;
        }
        if (!justOpened && isDirty && cleanValuesRef.current.id === initialValues?.id) {
            return;
        }
        const clean = initialValues ? { ...initialValues, defaults: initialValues.defaults ?? {} } : emptyValues;
        const startEnabled = (clean.renameTemplate ?? '').trim().length > 0;
        setValues(clean);
        cleanValuesRef.current = clean;
        setRenameEnabled(startEnabled);
        cleanRenameEnabledRef.current = startEnabled;
        setErrors({});
        setShowUnsavedDialog(false);
        setShowStaleSaveDialog(false);
        clearConflict();
    }, [clearConflict, initialValues, isDirty, open]);

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

    const handleDiscard = useCallback(() => {
        setShowUnsavedDialog(false);
        clearConflict();
        onOpenChange(false);
    }, [clearConflict, onOpenChange]);

    const handleKeepEditing = useCallback(() => {
        setShowUnsavedDialog(false);
    }, []);

    const handleCancel = useCallback(() => {
        handleOpenChange(false);
    }, [handleOpenChange]);

    const handleNameChange = useCallback((e: React.ChangeEvent<HTMLInputElement>) => {
        setValues((prev) => ({ ...prev, name: e.target.value }));
        setErrors((prev) => ({ ...prev, name: '' }));
    }, []);

    const handlePathChange = useCallback((path: string) => {
        setValues((prev) => ({ ...prev, path }));
        setErrors((prev) => ({ ...prev, path: '' }));
    }, []);

    const handleContentTypeChange = useCallback((contentType: string) => {
        setValues((prev) => ({
            ...prev,
            contentType,
            defaults: prev.contentType === contentType ? prev.defaults : {},
        }));
        setErrors((prev) => ({ ...prev, contentType: '' }));
    }, []);

    const handleDefaultMonitoringChange = useCallback((value: boolean) => {
        setValues((prev) => ({ ...prev, defaultMonitoring: value }));
    }, []);

    const handleInheritMonitoring = useCallback(() => {
        setValues((prev) => ({ ...prev, defaultMonitoring: null }));
    }, []);

    const handleIsDefaultChange = useCallback((isDefault: boolean) => {
        setValues((prev) => ({ ...prev, isDefault }));
    }, []);

    const handleUpgradeFileHandlingChange = useCallback((value: string) => {
        setValues((prev) => ({ ...prev, upgradeFileHandling: value as UpgradeFileHandling }));
    }, []);

    const handleLevelDefaultChange = useCallback((levelSlug: string, qualityTierListId: string) => {
        setValues((prev) => {
            const remaining = { ...prev.defaults };
            delete remaining[levelSlug];
            return {
                ...prev,
                defaults: qualityTierListId ? { ...remaining, [levelSlug]: { qualityTierListId } } : remaining,
            };
        });
        setErrors((prev) => ({ ...prev, [`defaults.${levelSlug}`]: '' }));
    }, []);

    const handleRenameTemplateChange = useCallback((e: React.ChangeEvent<HTMLInputElement>) => {
        setValues((prev) => ({ ...prev, renameTemplate: e.target.value }));
        setErrors((prev) => ({ ...prev, renameTemplate: '' }));
    }, []);

    const handleRenameEnabledChange = useCallback((enabled: boolean) => {
        setRenameEnabled(enabled);
        if (!enabled) {
            setValues((prev) => ({ ...prev, renameTemplate: '' }));
            setErrors((prev) => ({ ...prev, renameTemplate: '' }));
        }
    }, []);

    const { selectedContentType, rootLevels, nestedLevels } = useMemo(() => {
        const selected = contentTypes.find((contentType) => contentType.id === values.contentType);
        return {
            selectedContentType: selected,
            rootLevels: selected?.levels.filter((level) => !level.parentSlug) ?? [],
            nestedLevels: selected?.levels.filter((level) => !!level.parentSlug) ?? [],
        };
    }, [contentTypes, values.contentType]);

    const validate = useCallback((): boolean => {
        const newErrors: Record<string, string> = {};

        if (!values.name.trim()) {
            newErrors.name = 'Name is required';
        }

        if (!values.path.trim()) {
            newErrors.path = 'Path is required';
        }

        if (!values.contentType) {
            newErrors.contentType = 'Content type is required';
        } else if (rootLevels.length === 0) {
            newErrors.contentType = 'This content type has no root level definitions';
        }

        for (const level of rootLevels) {
            if (!values.defaults[level.slug]) {
                newErrors[`defaults.${level.slug}`] = `Select a default tier list for ${level.displayName}`;
            }
        }

        if (renameEnabled && !values.renameTemplate.trim()) {
            newErrors.renameTemplate = 'Rename template is required';
        }

        setErrors(newErrors);
        return Object.keys(newErrors).length === 0;
    }, [renameEnabled, rootLevels, values]);

    const save = useCallback(async () => {
        beginOwnSave();
        try {
            await Promise.resolve(onSubmit(values));
            endOwnSave(true);
            setShowStaleSaveDialog(false);
        } catch {
            endOwnSave(false);
        }
    }, [beginOwnSave, endOwnSave, onSubmit, values]);

    const handleSubmit = useCallback(
        (e: React.SubmitEvent) => {
            e.preventDefault();
            if (!validate()) {
                return;
            }
            if (conflictState === 'stale') {
                setShowStaleSaveDialog(true);
                return;
            }
            void save();
        },
        [conflictState, save, validate],
    );

    const selectedUpgradeFileHandling = upgradeFileHandlingOptions.find((o) => o.value === values.upgradeFileHandling);

    return (
        <Sheet open={open} onOpenChange={handleOpenChange}>
            <SheetContent side="right" className="root-folder-form__sheet" data-component="root-folder-form">
                <SheetHeader>
                    <SheetTitle>{isEditing ? 'Edit Root Folder' : 'Add Root Folder'}</SheetTitle>
                    <SheetDescription>
                        {isEditing
                            ? 'Update the root folder configuration.'
                            : 'Configure a new root folder for content storage.'}
                    </SheetDescription>
                </SheetHeader>

                <form onSubmit={handleSubmit} className="root-folder-form">
                    <div className="root-folder-form__body">
                        {conflictState && (
                            <ExternalChangeConflictWarning
                                state={conflictState}
                                entityLabel="Root folder"
                                id="root-folder-external-change"
                            />
                        )}
                        <div className="root-folder-form__field">
                            <Label htmlFor="folder-name" error={!!errors.name} required>
                                Name
                            </Label>
                            <Input
                                id="folder-name"
                                value={values.name}
                                onChange={handleNameChange}
                                placeholder="My Media Folder"
                                error={!!errors.name}
                                errorMessage={errors.name || undefined}
                                disabled={isSubmitting}
                                data-field="name"
                            />
                        </div>

                        <div className="root-folder-form__field">
                            <Label htmlFor="folder-path" error={!!errors.path} required>
                                Path
                            </Label>
                            <PathInput
                                id="folder-path"
                                value={values.path}
                                onChange={handlePathChange}
                                placeholder="/media/content"
                                mode="folder"
                                error={!!errors.path}
                                errorMessage={errors.path || undefined}
                                disabled={isSubmitting}
                                data-field="path"
                            />
                            <p className="root-folder-form__help">Folder where content will be stored.</p>
                        </div>

                        <div className="root-folder-form__field">
                            <Label htmlFor="folder-content-type" error={!!errors.contentType} required>
                                Content Type
                            </Label>
                            <Select
                                value={values.contentType}
                                onValueChange={handleContentTypeChange}
                                disabled={isSubmitting}
                            >
                                <SelectTrigger
                                    id="folder-content-type"
                                    placeholder="Select a content type..."
                                    error={!!errors.contentType}
                                    aria-describedby={errors.contentType ? 'folder-content-type-error' : undefined}
                                    data-field="content-type"
                                >
                                    {selectedContentType?.name ?? 'Select a content type...'}
                                </SelectTrigger>
                                <SelectContent>
                                    {contentTypes.map((ct) => (
                                        <SelectItem key={ct.id} value={ct.id}>
                                            {ct.name}
                                        </SelectItem>
                                    ))}
                                </SelectContent>
                            </Select>
                            <p className="root-folder-form__help">
                                The type of content that will be stored in this folder.
                            </p>
                            {errors.contentType && (
                                <p id="folder-content-type-error" className="form-error" role="alert">
                                    {errors.contentType}
                                </p>
                            )}
                        </div>

                        <div className="root-folder-form__field">
                            <div className="root-folder-form__toggle-row">
                                <Switch
                                    id="folder-default-monitoring"
                                    checked={values.defaultMonitoring === true}
                                    onCheckedChange={handleDefaultMonitoringChange}
                                    disabled={isSubmitting}
                                    aria-label="Default monitoring"
                                    data-field="default-monitoring"
                                />
                                <Label htmlFor="folder-default-monitoring">
                                    {values.defaultMonitoring === null
                                        ? 'Use system default monitoring'
                                        : values.defaultMonitoring
                                          ? 'Monitor new content'
                                          : 'Exclude new content'}
                                </Label>
                            </div>
                            <p className="root-folder-form__help">
                                {values.defaultMonitoring === null
                                    ? 'New top-level content uses the system default monitoring setting.'
                                    : values.defaultMonitoring
                                      ? 'New top-level content under this folder is actively monitored.'
                                      : 'New top-level content under this folder is excluded from monitoring.'}
                            </p>
                            {values.defaultMonitoring !== null && (
                                <Button
                                    type="button"
                                    variant="ghost"
                                    size="sm"
                                    onClick={handleInheritMonitoring}
                                    disabled={isSubmitting}
                                    data-action="inherit-monitoring"
                                >
                                    Use system default
                                </Button>
                            )}
                        </div>

                        <fieldset className="root-folder-form__field root-folder-form__radio-field">
                            <legend className="label">Default folder</legend>
                            <div
                                className="root-folder-form__radio-group"
                                role="radiogroup"
                                aria-label="Default folder"
                                data-field="is-default"
                            >
                                <label className="root-folder-form__radio-option">
                                    <input
                                        type="radio"
                                        name="root-folder-default"
                                        value="default"
                                        checked={values.isDefault}
                                        onChange={() => handleIsDefaultChange(true)}
                                        disabled={isSubmitting}
                                    />
                                    <span>Default for this content type</span>
                                </label>
                                <label className="root-folder-form__radio-option">
                                    <input
                                        type="radio"
                                        name="root-folder-default"
                                        value="not-default"
                                        checked={!values.isDefault}
                                        onChange={() => handleIsDefaultChange(false)}
                                        disabled={isSubmitting}
                                    />
                                    <span>Not the default</span>
                                </label>
                            </div>
                            <p className="root-folder-form__help">
                                Selecting a default folder replaces the current default for this content type.
                            </p>
                        </fieldset>

                        <div className="root-folder-form__field">
                            <Label htmlFor="folder-upgrade-file-handling">Upgrade File Handling</Label>
                            <Select
                                value={values.upgradeFileHandling}
                                onValueChange={handleUpgradeFileHandlingChange}
                                disabled={isSubmitting}
                            >
                                <SelectTrigger
                                    id="folder-upgrade-file-handling"
                                    placeholder="Select upgrade file handling..."
                                    data-field="upgrade-file-handling"
                                >
                                    {selectedUpgradeFileHandling?.label ?? 'Replace'}
                                </SelectTrigger>
                                <SelectContent>
                                    {upgradeFileHandlingOptions.map((opt) => (
                                        <SelectItem key={opt.value} value={opt.value}>
                                            {opt.label}
                                        </SelectItem>
                                    ))}
                                </SelectContent>
                            </Select>
                            <p className="root-folder-form__help">
                                {selectedUpgradeFileHandling?.description ?? upgradeFileHandlingOptions[0].description}
                            </p>
                        </div>

                        {rootLevels.length === 1 && (
                            <LevelDefaultPicker
                                contentType={values.contentType}
                                level={rootLevels[0]}
                                value={values.defaults[rootLevels[0].slug]?.qualityTierListId ?? ''}
                                required
                                simple
                                disabled={isSubmitting}
                                qualityTierLists={qualityTierLists}
                                error={errors[`defaults.${rootLevels[0].slug}`]}
                                onChange={handleLevelDefaultChange}
                            />
                        )}

                        {rootLevels.length > 1 && (
                            <fieldset className="root-folder-form__field root-folder-form__level-defaults">
                                <legend className="label">Default Quality Tiers</legend>
                                <p className="root-folder-form__help">
                                    Each root level requires a compatible tier list.
                                </p>
                                {rootLevels.map((level) => (
                                    <LevelDefaultPicker
                                        key={level.slug}
                                        contentType={values.contentType}
                                        level={level}
                                        value={values.defaults[level.slug]?.qualityTierListId ?? ''}
                                        required
                                        simple={false}
                                        disabled={isSubmitting}
                                        qualityTierLists={qualityTierLists}
                                        error={errors[`defaults.${level.slug}`]}
                                        onChange={handleLevelDefaultChange}
                                    />
                                ))}
                            </fieldset>
                        )}

                        {nestedLevels.length > 0 && (
                            <details className="root-folder-form__advanced">
                                <summary>Advanced quality defaults</summary>
                                <p className="root-folder-form__help">
                                    Nested levels inherit from their nearest configured parent unless overridden.
                                </p>
                                <div className="root-folder-form__level-defaults">
                                    {nestedLevels.map((level) => (
                                        <LevelDefaultPicker
                                            key={level.slug}
                                            contentType={values.contentType}
                                            level={level}
                                            value={values.defaults[level.slug]?.qualityTierListId ?? ''}
                                            required={false}
                                            simple={false}
                                            disabled={isSubmitting}
                                            qualityTierLists={qualityTierLists}
                                            error={errors[`defaults.${level.slug}`]}
                                            onChange={handleLevelDefaultChange}
                                        />
                                    ))}
                                </div>
                            </details>
                        )}

                        <div className="root-folder-form__field">
                            <div className="root-folder-form__toggle-row">
                                <Switch
                                    id="folder-rename-enabled"
                                    checked={renameEnabled}
                                    onCheckedChange={handleRenameEnabledChange}
                                    disabled={isSubmitting}
                                    data-field="rename-enabled"
                                />
                                <Label htmlFor="folder-rename-enabled">Rename files on import</Label>
                            </div>
                            <p className="root-folder-form__help">
                                When off, imported files keep their original names. When on, Magpie renames files using
                                the template below.
                            </p>
                            {renameEnabled && (
                                <>
                                    <Label htmlFor="folder-rename-template" error={!!errors.renameTemplate} required>
                                        Rename template
                                    </Label>
                                    <Input
                                        id="folder-rename-template"
                                        value={values.renameTemplate}
                                        onChange={handleRenameTemplateChange}
                                        placeholder={DEFAULT_RENAME_TEMPLATE_SUGGESTION}
                                        error={!!errors.renameTemplate}
                                        errorMessage={errors.renameTemplate || undefined}
                                        disabled={isSubmitting}
                                        data-field="rename-template"
                                    />
                                    <p className="root-folder-form__help">
                                        Placeholders use the <code>{'{Token}'}</code> form. An example is{' '}
                                        <code>{'{Title} ({Year}).{Ext}'}</code>.
                                    </p>
                                </>
                            )}
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
                            <XIcon weight="bold" aria-hidden="true" />
                            Cancel
                        </Button>
                        <Button
                            type="submit"
                            variant="primary"
                            loading={isSubmitting}
                            disabled={conflictState === 'deleted'}
                            data-action="save"
                        >
                            <FloppyDiskIcon weight="bold" aria-hidden="true" />
                            Save
                        </Button>
                    </SheetFooter>
                </form>
            </SheetContent>

            <UnsavedChangesDialog open={showUnsavedDialog} onDiscard={handleDiscard} onCancel={handleKeepEditing} />
            <StaleSaveConfirmationDialog
                open={showStaleSaveDialog}
                title="Save changes based on older data?"
                description="This item changed elsewhere after editing began. Saving will overwrite those newer changes with the values in this form."
                saving={isSubmitting}
                onCancel={() => setShowStaleSaveDialog(false)}
                onConfirm={() => void save()}
            />
        </Sheet>
    );
}

RootFolderForm.displayName = 'RootFolderForm';

export default RootFolderForm;
