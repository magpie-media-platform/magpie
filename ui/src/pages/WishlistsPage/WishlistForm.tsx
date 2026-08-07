import { useState, useCallback, useMemo, useEffect, useRef, type ReactNode } from 'react';
import {
    FloppyDiskIcon,
    XIcon,
    InfoIcon,
    LockSimpleIcon,
    SparkleIcon,
    WarningCircleIcon,
    MagnifyingGlassIcon,
} from '@phosphor-icons/react';

import { useWishlistPreview } from '@/hooks/useWishlistPreview';
import { ProviderSearchWarnings } from '@/components/content/ProviderSearchWarnings';
import { useQualityTierLists } from '@/hooks/useQualityTierLists';
import { useUnsavedChangesGuard } from '@/hooks/useUnsavedChangesGuard';
import { deepEqual, useFormDirty } from '@/hooks/useFormDirty';
import { useExternalChangeConflict } from '@/hooks/useExternalChangeConflict';
import { parseMagpieQl, formatMagpieQlCriteria } from '@/lib/magpieQl';
import { getErrorMessage } from '@/lib/errors';
import type { ContentFilterCriteria } from '@/hooks/useFilters';
import type { ProviderMetadataCandidate } from '@/hooks/useProviderMetadataCandidateSearch';

import { getQueryContentType, setQueryContentType } from './wishlistFormUtils';

import { Badge } from '@/components/ui/Badge';
import { Button } from '@/components/ui/Button';
import { EmptyState } from '@/components/ui/EmptyState';
import { FilterBar } from '@/components/filters/FilterBar';
import { Input } from '@/components/ui/Input';
import { Label } from '@/components/ui/Label';
import { Skeleton } from '@/components/ui/Skeleton';
import { Switch } from '@/components/ui/Switch';
import { Select, SelectTrigger, SelectContent, SelectItem } from '@/components/ui/Select';
import { Sheet, SheetContent, SheetHeader, SheetTitle, SheetDescription, SheetFooter } from '@/components/ui/Sheet';
import { TagInput } from '@/components/ui/TagInput';
import { Tooltip } from '@/components/ui/Tooltip';
import { UnsavedChangesDialog } from '@/components/ui/UnsavedChangesDialog';
import { ExternalChangeConflictWarning, StaleSaveConfirmationDialog } from '@/components/ui/ExternalChangeConflict';
import type {
    ContentFilterOption,
    QualityTierListOption,
    RootFolderOption,
    TagOption,
    WishlistFormValues,
} from '@/types';

import './WishlistForm.css';

export type { ContentFilterOption, QualityTierListOption, RootFolderOption, TagOption, WishlistFormValues };

interface WishlistFormProps {
    open: boolean;

    onOpenChange: (open: boolean) => void;

    initialValues?: WishlistFormValues | null;

    qualityTierLists?: QualityTierListOption[];

    rootFolders: RootFolderOption[];

    tags: TagOption[];

    onSubmit: (values: WishlistFormValues) => void | Promise<void>;

    isSubmitting?: boolean;

    isOwner?: boolean;

    canEdit?: boolean;
}

const NONE_VALUE = '__none__';

const PREVIEW_LIMIT = 3;

const emptyCriteria: ContentFilterCriteria = {};

const emptyValues: WishlistFormValues = {
    name: '',
    filterId: null,
    contentType: '',
    criteria: emptyCriteria,
    qualityTierListId: null,
    rootFolderId: null,
    monitored: true,
    tagIds: [],
};

function isContentFilterCriteria(value: unknown): value is ContentFilterCriteria {
    return typeof value === 'object' && value !== null;
}

function normalizeWishlist(input: { values: WishlistFormValues; filterQuery: string }): unknown {
    const { values, filterQuery } = input;
    return {
        name: values.name,
        contentType: values.contentType,
        qualityTierListId: values.qualityTierListId,
        rootFolderId: values.rootFolderId,
        monitored: values.monitored,
        tagIds: [...values.tagIds].sort(),
        filterQuery,
    };
}

function normalizeServerWishlist(values: WishlistFormValues): unknown {
    return {
        name: values.name,
        contentType: values.contentType,
        criteria: values.criteria,
        qualityTierListId: values.qualityTierListId,
        rootFolderId: values.rootFolderId,
        monitored: values.monitored,
        tagIds: [...values.tagIds].sort(),
    };
}

function criteriaIsEmpty(criteria: ContentFilterCriteria | null | undefined): boolean {
    if (!criteria) {
        return true;
    }
    const chi = criteria.contentHierarchyItem;
    const media = criteria.mediaFiles;
    const count = (set?: { and?: unknown[]; or?: unknown[]; not?: unknown }) =>
        (set?.and?.length ?? 0) + (set?.or?.length ?? 0) + (set?.not != null ? 1 : 0);
    return count(chi) + count(media) === 0;
}

export function WishlistForm({
    open,
    onOpenChange,
    initialValues,
    qualityTierLists,
    rootFolders,
    tags,
    onSubmit,
    isSubmitting = false,
    isOwner = true,
    canEdit = true,
}: WishlistFormProps): ReactNode {
    const isEditing = !!initialValues?.id;

    const [values, setValues] = useState<WishlistFormValues>(initialValues ?? emptyValues);
    const { tierLists: applicableTierLists } = useQualityTierLists({
        contentType: values.contentType || undefined,
        enabled: qualityTierLists === undefined && !!values.contentType,
    });
    const { tierLists: allTierLists, isLoading: allTierListsLoading } = useQualityTierLists({
        enabled: qualityTierLists === undefined && !!values.qualityTierListId,
    });
    const availableTierLists: QualityTierListOption[] =
        qualityTierLists ??
        applicableTierLists.map((tierList) => ({
            id: tierList.id,
            name: tierList.name,
            mimeTypes: tierList.mimeTypes,
        }));
    const [errors, setErrors] = useState<Record<string, string>>({});

    const [filterQuery, setFilterQuery] = useState('');
    const [filterBarExpanded, setFilterBarExpanded] = useState(true);
    const [previewCandidates, setPreviewCandidates] = useState<ProviderMetadataCandidate[] | null>(null);
    const [previewTotal, setPreviewTotal] = useState(0);
    const [previewError, setPreviewError] = useState<string | null>(null);

    const cleanValuesRef = useRef<WishlistFormValues>(initialValues ?? emptyValues);
    const cleanQueryRef = useRef('');
    const filterQueryEditedRef = useRef(false);
    const valuesRef = useRef(values);
    const filterQueryRef = useRef(filterQuery);
    const openEntityIdRef = useRef<string | null>(null);
    const wasOpenRef = useRef(false);
    const initialValuesRef = useRef(initialValues);
    const pendingSavedValuesRef = useRef<WishlistFormValues | null>(null);
    initialValuesRef.current = initialValues;
    valuesRef.current = values;
    filterQueryRef.current = filterQuery;
    const initialValuesKey = JSON.stringify(initialValues ?? null);

    const [showUnsavedDialog, setShowUnsavedDialog] = useState(false);
    const [staleSave, setStaleSave] = useState<{ values: WishlistFormValues; filterQuery: string } | null>(null);

    const preview = useWishlistPreview();

    useEffect(() => {
        if (!open) {
            wasOpenRef.current = false;
            return;
        }
        let cancelled = false;
        const clean = initialValuesRef.current ?? emptyValues;
        const entityId = clean.id ?? null;
        const sameOpenEntity = wasOpenRef.current && openEntityIdRef.current === entityId;
        const currentlyDirty = !deepEqual(
            normalizeWishlist({ values: valuesRef.current, filterQuery: filterQueryRef.current }),
            normalizeWishlist({ values: cleanValuesRef.current, filterQuery: cleanQueryRef.current }),
        );
        wasOpenRef.current = true;
        openEntityIdRef.current = entityId;
        if (sameOpenEntity && currentlyDirty) {
            return;
        }
        setValues(clean);
        cleanValuesRef.current = clean;
        filterQueryEditedRef.current = false;
        setErrors({});
        setShowUnsavedDialog(false);
        setStaleSave(null);
        pendingSavedValuesRef.current = null;
        setPreviewCandidates(null);
        setPreviewTotal(0);
        setPreviewError(null);

        if (!criteriaIsEmpty(clean.criteria)) {
            setFilterBarExpanded(true);
            formatMagpieQlCriteria(clean.criteria, 'content')
                .then((result) => {
                    if (cancelled) {
                        return;
                    }
                    cleanQueryRef.current = result.query;
                    const formattedContentType = getQueryContentType(result.query);
                    if (formattedContentType) {
                        cleanValuesRef.current = { ...cleanValuesRef.current, contentType: formattedContentType };
                        setValues((current) => ({ ...current, contentType: formattedContentType }));
                    }
                    if (!filterQueryEditedRef.current) {
                        setFilterQuery(result.query);
                    }
                })
                .catch((error: unknown) => {
                    if (cancelled) {
                        return;
                    }
                    setFilterQuery('');
                    cleanQueryRef.current = '';
                    setErrors((prev) => ({
                        ...prev,
                        criteria: getErrorMessage(error, 'Failed to format filter criteria.'),
                    }));
                });
        } else {
            setFilterQuery('');
            cleanQueryRef.current = '';
            setFilterBarExpanded(true);
        }

        return () => {
            cancelled = true;
        };
    }, [open, initialValuesKey]);

    const isDirty = useFormDirty(
        { values, filterQuery },
        { values: cleanValuesRef.current, filterQuery: cleanQueryRef.current },
        normalizeWishlist,
    );

    const {
        state: conflictState,
        beginOwnSave,
        endOwnSave,
        clearConflict,
    } = useExternalChangeConflict({
        dirty: isDirty,
        entityId: initialValues?.id,
        eventNames: ['wishlist.updated', 'wishlist.deleted'],
        deletedEventNames: ['wishlist.deleted'],
        contextKeys: ['wishlist.id'],
        enabled: open && isEditing,
    });

    const readOnly = isEditing && !canEdit;

    useEffect(() => {
        const pendingSavedValues = pendingSavedValuesRef.current;
        const serverValues = initialValuesRef.current;
        if (
            !pendingSavedValues ||
            !serverValues ||
            !deepEqual(normalizeServerWishlist(serverValues), normalizeServerWishlist(pendingSavedValues))
        ) {
            return;
        }

        pendingSavedValuesRef.current = null;
        clearConflict();
    }, [clearConflict, initialValuesKey]);

    useUnsavedChangesGuard(open && isDirty && !readOnly && !isSubmitting);

    const handleOpenChange = useCallback(
        (nextOpen: boolean) => {
            if (!nextOpen && isDirty && !readOnly && !isSubmitting) {
                setShowUnsavedDialog(true);
                return;
            }
            onOpenChange(nextOpen);
        },
        [isDirty, readOnly, isSubmitting, onOpenChange],
    );

    const handleDiscard = useCallback(() => {
        cleanValuesRef.current = values;
        cleanQueryRef.current = filterQuery;
        clearConflict();
        setShowUnsavedDialog(false);
        onOpenChange(false);
    }, [values, filterQuery, clearConflict, onOpenChange]);

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

    const queryContentType = useMemo(() => getQueryContentType(filterQuery), [filterQuery]);
    const effectiveContentType = queryContentType || values.contentType;

    const handleFilterQueryChange = useCallback((value: string) => {
        filterQueryEditedRef.current = true;
        setFilterQuery(value);
        const contentType = getQueryContentType(value);
        setValues((prev) => {
            const nextContentType = contentType || prev.contentType;
            return {
                ...prev,
                contentType: nextContentType,
                qualityTierListId: prev.contentType === nextContentType ? prev.qualityTierListId : null,
            };
        });
        setErrors((prev) => ({ ...prev, criteria: '', contentType: '' }));
        if (value.length > 0) {
            setFilterBarExpanded(true);
        }
    }, []);

    const handleQualityTierListChange = useCallback((qualityTierListId: string) => {
        setValues((prev) => ({
            ...prev,
            qualityTierListId: qualityTierListId === NONE_VALUE ? null : qualityTierListId,
        }));
    }, []);

    const handleRootFolderChange = useCallback((rootFolderId: string) => {
        setValues((prev) => ({
            ...prev,
            rootFolderId: rootFolderId === NONE_VALUE ? null : rootFolderId,
        }));
    }, []);

    const handleMonitoredChange = useCallback((monitored: boolean) => {
        setValues((prev) => ({ ...prev, monitored }));
        setErrors((prev) => ({ ...prev, criteria: '' }));
    }, []);

    const handleTagsChange = useCallback((tagIds: string[]) => {
        setValues((prev) => ({ ...prev, tagIds }));
    }, []);

    const resolveCriteria = useCallback(async (): Promise<ContentFilterCriteria | null> => {
        const query = effectiveContentType ? setQueryContentType(filterQuery, effectiveContentType) : filterQuery;
        if (!query.trim()) {
            return emptyCriteria;
        }
        const result = await parseMagpieQl(query, 'content');
        if (result.error) {
            setErrors((prev) => ({ ...prev, criteria: result.error!.message }));
            return null;
        }
        return isContentFilterCriteria(result.criteria) ? result.criteria : emptyCriteria;
    }, [filterQuery, effectiveContentType]);

    const handlePreview = useCallback(async () => {
        if (readOnly) {
            return;
        }
        setPreviewError(null);
        const criteria = await resolveCriteria();
        if (!criteria) {
            return;
        }
        try {
            const result = await preview.mutateAsync({
                criteria,
                contentType: effectiveContentType || undefined,
                page: 1,
            });
            const total = result.pageItemCount || result.candidates.length;
            setPreviewCandidates(result.candidates.slice(0, PREVIEW_LIMIT));
            setPreviewTotal(total);
        } catch {
            setPreviewCandidates(null);
            setPreviewTotal(0);
            setPreviewError('Preview failed. Please try again.');
        }
    }, [readOnly, resolveCriteria, preview, effectiveContentType]);

    const submitValues = useCallback(
        async (submittedValues: WishlistFormValues, submittedFilterQuery: string) => {
            if (isEditing) {
                beginOwnSave();
            }
            try {
                await onSubmit(submittedValues);
                const changedDuringSave = !deepEqual(
                    normalizeWishlist({ values: valuesRef.current, filterQuery: filterQueryRef.current }),
                    normalizeWishlist({ values: submittedValues, filterQuery: submittedFilterQuery }),
                );
                cleanValuesRef.current = submittedValues;
                cleanQueryRef.current = submittedFilterQuery;
                if (isEditing) {
                    pendingSavedValuesRef.current = changedDuringSave ? submittedValues : null;
                    endOwnSave(true);
                    const serverValues = initialValuesRef.current;
                    if (
                        changedDuringSave &&
                        serverValues &&
                        deepEqual(normalizeServerWishlist(serverValues), normalizeServerWishlist(submittedValues))
                    ) {
                        pendingSavedValuesRef.current = null;
                        clearConflict();
                    }
                }
                setStaleSave(null);
                if (!isEditing || !changedDuringSave) {
                    onOpenChange(false);
                    return;
                }
                setValues((current) => ({ ...current }));
            } catch {
                if (isEditing) {
                    endOwnSave(false);
                }
            }
        },
        [beginOwnSave, clearConflict, endOwnSave, isEditing, onOpenChange, onSubmit],
    );

    const handleSubmit = useCallback(
        async (e: React.SubmitEvent) => {
            e.preventDefault();

            const newErrors: Record<string, string> = {};
            if (!values.name.trim()) {
                newErrors.name = 'Name is required';
            }

            const criteria = await resolveCriteria();
            if (criteria === null) {
                setErrors((prev) => ({ ...prev, ...newErrors }));
                return;
            }

            if (values.monitored && criteriaIsEmpty(criteria)) {
                newErrors.criteria = 'Add filter criteria to monitor this wishlist';
            }

            if (values.monitored && !effectiveContentType) {
                newErrors.contentType = 'Select a content type to monitor this wishlist';
            }

            if (Object.keys(newErrors).length > 0) {
                setErrors(newErrors);
                return;
            }

            setErrors({});
            const submittedValues = { ...values, contentType: effectiveContentType, criteria };
            if (conflictState === 'deleted') {
                return;
            }
            if (conflictState === 'stale') {
                setStaleSave({ values: submittedValues, filterQuery });
                return;
            }
            await submitValues(submittedValues, filterQuery);
        },
        [values, resolveCriteria, effectiveContentType, conflictState, filterQuery, submitValues],
    );

    const selectedQualityTierList = useMemo(
        () => (qualityTierLists ?? allTierLists).find((tierList) => tierList.id === values.qualityTierListId),
        [allTierLists, qualityTierLists, values.qualityTierListId],
    );

    const eligibleRootFolders = useMemo(() => {
        if (!effectiveContentType) {
            return rootFolders;
        }
        return rootFolders.filter((f) => f.contentType.toLowerCase() === effectiveContentType.toLowerCase());
    }, [rootFolders, effectiveContentType]);

    const selectedRootFolder = useMemo(
        () => rootFolders.find((f) => f.id === values.rootFolderId),
        [rootFolders, values.rootFolderId],
    );

    return (
        <Sheet open={open} onOpenChange={handleOpenChange}>
            <SheetContent side="right" className="wishlist-form__sheet" data-component="wishlist-form">
                <SheetHeader>
                    <SheetTitle>
                        {isEditing ? 'Edit Wishlist' : 'Add Wishlist'}
                        {readOnly && (
                            <Badge
                                size="sm"
                                variant="default"
                                badgeStyle="outline"
                                className="wishlist-form__readonly-badge"
                                data-testid="wishlist-form__readonly-badge"
                            >
                                <LockSimpleIcon weight="fill" aria-hidden="true" />
                                Read-only
                            </Badge>
                        )}
                        {isEditing && !isOwner && !readOnly && (
                            <Tooltip content="This wishlist is owned by another user" side="bottom">
                                <span className="wishlist-form__owner-lock" data-testid="wishlist-form__owner-lock">
                                    <LockSimpleIcon weight="fill" aria-hidden="true" />
                                </span>
                            </Tooltip>
                        )}
                    </SheetTitle>
                    <SheetDescription>
                        {isEditing
                            ? readOnly
                                ? 'View wishlist configuration.'
                                : 'Update the wishlist configuration.'
                            : 'A wishlist is a saved filter Magpie uses to automatically monitor for matching content.'}
                    </SheetDescription>
                </SheetHeader>

                <form onSubmit={handleSubmit} className="wishlist-form">
                    <div className="wishlist-form__body">
                        {conflictState && (
                            <ExternalChangeConflictWarning
                                state={conflictState}
                                entityLabel="Wishlist"
                                id="wishlist-external-change-warning"
                            />
                        )}
                        <div className="wishlist-form__field">
                            <Label htmlFor="wishlist-name" error={!!errors.name} required>
                                Name
                            </Label>
                            <Input
                                id="wishlist-name"
                                value={values.name}
                                onChange={handleNameChange}
                                placeholder="My Wishlist"
                                error={!!errors.name}
                                errorMessage={errors.name || undefined}
                                disabled={isSubmitting || readOnly}
                                data-field="name"
                            />
                        </div>

                        <div className="wishlist-form__field">
                            <Label error={!!errors.criteria || !!errors.contentType}>Filter</Label>
                            <FilterBar
                                value={filterQuery}
                                onChange={handleFilterQueryChange}
                                context="content"
                                expanded={filterBarExpanded}
                                onExpandedChange={setFilterBarExpanded}
                                readOnly={readOnly || (!isEditing && isSubmitting)}
                                defaultContentType={values.contentType}
                                data-field="criteria"
                            />
                            {errors.criteria && (
                                <p className="wishlist-form__error" role="alert" data-field="criteria-error">
                                    {errors.criteria}
                                </p>
                            )}
                            {errors.contentType && (
                                <p className="wishlist-form__error" role="alert" data-field="contentType-error">
                                    {errors.contentType}
                                </p>
                            )}
                            {!readOnly && (
                                <div className="wishlist-form__filter-actions">
                                    <Button
                                        type="button"
                                        variant="secondary"
                                        size="sm"
                                        onClick={handlePreview}
                                        loading={preview.isPending}
                                        disabled={isSubmitting}
                                        data-action="preview"
                                    >
                                        <SparkleIcon weight="bold" aria-hidden="true" />
                                        Preview
                                    </Button>
                                </div>
                            )}
                        </div>

                        {(preview.isPending || previewCandidates !== null || previewError) && (
                            <div className="wishlist-form__preview" data-testid="wishlist-form__preview">
                                {!preview.isPending && !previewError && previewCandidates !== null && (
                                    <ProviderSearchWarnings
                                        warnings={preview.data?.warnings ?? []}
                                        hasResults={previewCandidates.length > 0}
                                    />
                                )}
                                {preview.isPending ? (
                                    <div className="wishlist-form__preview-loading">
                                        <Skeleton className="wishlist-form__preview-skeleton" />
                                        <Skeleton className="wishlist-form__preview-skeleton" />
                                    </div>
                                ) : previewError ? (
                                    <EmptyState
                                        icon={<WarningCircleIcon weight="duotone" />}
                                        title="Preview failed"
                                        description={previewError}
                                        size="sm"
                                        data-testid="wishlist-form__preview-error"
                                    />
                                ) : previewCandidates && previewCandidates.length > 0 ? (
                                    <>
                                        <p className="wishlist-form__preview-heading">
                                            {previewTotal > previewCandidates.length
                                                ? `Newest ${previewCandidates.length} of ${previewTotal}`
                                                : `${previewTotal} match${previewTotal === 1 ? '' : 'es'}`}
                                        </p>
                                        <ul
                                            className="wishlist-form__preview-list"
                                            data-testid="wishlist-form__preview-list"
                                        >
                                            {previewCandidates.map((candidate) => (
                                                <li
                                                    key={`${candidate.providerType}:${candidate.externalIdSource}:${candidate.externalId}`}
                                                    className="wishlist-form__preview-item"
                                                >
                                                    {candidate.thumbnailUrl ? (
                                                        <img
                                                            src={candidate.thumbnailUrl}
                                                            alt=""
                                                            className="wishlist-form__preview-thumb"
                                                            loading="lazy"
                                                        />
                                                    ) : (
                                                        <div
                                                            className="wishlist-form__preview-thumb wishlist-form__preview-thumb--placeholder"
                                                            aria-hidden="true"
                                                        />
                                                    )}
                                                    <div className="wishlist-form__preview-body">
                                                        <span className="wishlist-form__preview-title">
                                                            {candidate.title?.trim() || 'Untitled'}
                                                            {candidate.year != null && (
                                                                <span className="wishlist-form__preview-year">
                                                                    {candidate.year}
                                                                </span>
                                                            )}
                                                        </span>
                                                        <Badge size="sm" variant="info" badgeStyle="outline">
                                                            {candidate.providerType}
                                                        </Badge>
                                                    </div>
                                                </li>
                                            ))}
                                        </ul>
                                    </>
                                ) : (
                                    <EmptyState
                                        icon={<MagnifyingGlassIcon weight="duotone" />}
                                        title="No matches"
                                        description={
                                            (preview.data?.warnings?.length ?? 0) > 0
                                                ? 'No usable matches were returned. Review the provider warnings and adjust the filter.'
                                                : 'No metadata provider found content matching these criteria.'
                                        }
                                        size="sm"
                                        data-testid="wishlist-form__preview-empty"
                                    />
                                )}
                            </div>
                        )}

                        <div className="wishlist-form__field">
                            <div className="wishlist-form__label-row">
                                <Label htmlFor="wishlist-quality-tier">Quality</Label>
                                <Tooltip
                                    content={
                                        <p className="wishlist-form__tooltip">
                                            Controls how matching releases are ranked.
                                            <br />
                                            Only releases that meet the quality can be grabbed.
                                            <br />
                                            Inherit uses the current root folder default.
                                        </p>
                                    }
                                    side="top"
                                >
                                    <button
                                        type="button"
                                        className="wishlist-form__info-button"
                                        aria-label="Quality help"
                                    >
                                        <InfoIcon weight="fill" aria-hidden="true" />
                                    </button>
                                </Tooltip>
                            </div>
                            <Select
                                value={values.qualityTierListId ?? NONE_VALUE}
                                onValueChange={handleQualityTierListChange}
                                disabled={isSubmitting || readOnly}
                            >
                                <SelectTrigger
                                    id="wishlist-quality-tier"
                                    placeholder="Inherit"
                                    data-field="qualityTierListId"
                                >
                                    {selectedQualityTierList
                                        ? `${selectedQualityTierList.name} - ${
                                              selectedQualityTierList.mimeTypes?.length
                                                  ? selectedQualityTierList.mimeTypes.join(', ')
                                                  : 'Any MIME type'
                                          }`
                                        : values.qualityTierListId
                                          ? allTierListsLoading
                                              ? 'Loading...'
                                              : 'Unknown tier list'
                                          : 'Inherit'}
                                </SelectTrigger>
                                <SelectContent>
                                    <SelectItem value={NONE_VALUE}>Inherit</SelectItem>
                                    {availableTierLists.map((qtl) => (
                                        <SelectItem key={qtl.id} value={qtl.id}>
                                            {qtl.name} -{' '}
                                            {qtl.mimeTypes?.length ? qtl.mimeTypes.join(', ') : 'Any MIME type'}
                                        </SelectItem>
                                    ))}
                                </SelectContent>
                            </Select>
                        </div>

                        <div className="wishlist-form__field">
                            <div className="wishlist-form__label-row">
                                <Label htmlFor="wishlist-root-folder">Root Folder</Label>
                                <Tooltip
                                    content={
                                        <p className="wishlist-form__tooltip">
                                            Folder where acquired content will be stored.
                                            <br />A selected folder takes priority. None uses the content type&apos;s
                                            default root folder.
                                        </p>
                                    }
                                    side="top"
                                >
                                    <button
                                        type="button"
                                        className="wishlist-form__info-button"
                                        aria-label="Root folder help"
                                    >
                                        <InfoIcon weight="fill" aria-hidden="true" />
                                    </button>
                                </Tooltip>
                            </div>
                            <Select
                                value={values.rootFolderId ?? NONE_VALUE}
                                onValueChange={handleRootFolderChange}
                                disabled={isSubmitting || readOnly}
                            >
                                <SelectTrigger id="wishlist-root-folder" placeholder="None" data-field="rootFolderId">
                                    {selectedRootFolder
                                        ? `${selectedRootFolder.name} (${selectedRootFolder.path})`
                                        : 'None'}
                                </SelectTrigger>
                                <SelectContent>
                                    <SelectItem value={NONE_VALUE}>None</SelectItem>
                                    {eligibleRootFolders.map((folder) => (
                                        <SelectItem key={folder.id} value={folder.id}>
                                            {folder.name} ({folder.path})
                                        </SelectItem>
                                    ))}
                                </SelectContent>
                            </Select>
                        </div>

                        <div className="wishlist-form__field wishlist-form__field--toggle">
                            <div className="wishlist-form__toggle-wrapper">
                                <Switch
                                    id="wishlist-monitored"
                                    checked={values.monitored}
                                    onCheckedChange={handleMonitoredChange}
                                    disabled={isSubmitting || readOnly}
                                    data-field="monitored"
                                />
                                <Label htmlFor="wishlist-monitored">Monitored</Label>
                            </div>
                            <p className="wishlist-form__help">
                                Monitored wishlists will be searched for missing content automatically.
                            </p>
                        </div>

                        <div className="wishlist-form__field" data-field="tags">
                            <Label htmlFor="wishlist-tags">Tags</Label>
                            <TagInput
                                id="wishlist-tags"
                                label="Tags"
                                value={values.tagIds}
                                onChange={handleTagsChange}
                                suggestions={tags.map((tag) => tag.name)}
                                allowCreate
                                placeholder="Add a tag..."
                                disabled={isSubmitting || readOnly}
                            />
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
                            {readOnly ? 'Close' : 'Cancel'}
                        </Button>
                        {!readOnly && (
                            <Button
                                type="submit"
                                variant="primary"
                                loading={isSubmitting}
                                disabled={conflictState === 'deleted'}
                                aria-describedby={conflictState ? 'wishlist-external-change-warning' : undefined}
                                data-action="save"
                            >
                                <FloppyDiskIcon weight="bold" aria-hidden="true" />
                                Save
                            </Button>
                        )}
                    </SheetFooter>
                </form>
            </SheetContent>

            <UnsavedChangesDialog open={showUnsavedDialog} onDiscard={handleDiscard} onCancel={handleKeepEditing} />
            <StaleSaveConfirmationDialog
                open={staleSave !== null}
                title="Save over newer wishlist changes?"
                description="This wishlist changed elsewhere after editing began. Saving will overwrite the newer changes."
                saving={isSubmitting}
                onCancel={() => setStaleSave(null)}
                onConfirm={() => {
                    if (staleSave) {
                        void submitValues(staleSave.values, staleSave.filterQuery);
                    }
                }}
            />
        </Sheet>
    );
}

WishlistForm.displayName = 'WishlistForm';

export default WishlistForm;
