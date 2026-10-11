import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { type SortingState } from '@tanstack/react-table';

import { type ContentHeaderFilters } from '@/components/ui/ContentList';
import { useToast } from '@/components/ui/Toast';
import {
    useContentFilters,
    useCreateContentFilter,
    useUpdateContentFilter,
    type ContentFilterCriteria,
} from '@/hooks/useFilters';
import { type LibraryLevelDefinition, type LibraryTypeDefinition } from '@/hooks/useLibraryTypes';
import { useDebouncedValue } from '@/hooks/useDebouncedValue';
import { useLiveUrlFilter } from '@/hooks/useLiveUrlFilter';
import { useToggle } from '@/hooks/useToggle';
import { useInstanceName } from '@/hooks/useInstanceName';
import type { User } from '@/auth';
import { parseMagpieQl } from '@/lib/magpieQl';
import { canEditOwned } from '@/lib/ownership';
import {
    readContentFilter,
    readContentFilterSelection,
    setContentFilter,
    setContentFilterSelection,
    hasComplexContentFilter,
} from '@/lib/contentColumnFilters';
import {
    buildContentFilterOptions,
    contentFilterSelectionValue,
    parseContentFilterSelectionValue,
} from '@/lib/contentFilterOptions';

import {
    VALID_SORT_DIRECTIONS,
    VALID_SORT_FIELDS,
    normalizeFulfillmentApiName,
    type LibraryFilterParams,
    type UpdateLibraryFilterParams,
} from './libraryPageConstants';

interface UseLibraryFiltersArgs {
    params: LibraryFilterParams;
    updateParams: UpdateLibraryFilterParams;
    contentTypes: LibraryTypeDefinition[];
    routeContentTypeSlug: string | undefined;
    selectedLevel: LibraryLevelDefinition | null;
    user: User | null;
    isAdmin: boolean;
    canWriteFilters: boolean;
}

export function useLibraryFilters({
    params,
    updateParams,
    contentTypes,
    routeContentTypeSlug,
    selectedLevel,
    user,
    isAdmin,
    canWriteFilters,
}: UseLibraryFiltersArgs) {
    const instanceName = useInstanceName();
    const { toast } = useToast();

    const appliedSavedFilterId = params.savedFilter;

    const {
        filters: savedContentFilters,
        isLoading: savedFiltersLoading,
        listError: savedFiltersListError,
        refetch: retrySavedFilters,
    } = useContentFilters();
    const [pendingSavedFilter, setPendingSavedFilter] = useState<{
        id: string;
        name: string;
        query: string;
        criteria: string;
    } | null>(null);
    const savedFilterOptions = useMemo(() => {
        const formatted = savedContentFilters
            .filter((f) => f.query || f.criteriaCount === 0)
            .map((f) => ({ id: f.id, name: f.name, query: f.query }));
        return pendingSavedFilter
            ? [...formatted.filter((filter) => filter.id !== pendingSavedFilter.id), pendingSavedFilter]
            : formatted;
    }, [savedContentFilters, pendingSavedFilter]);

    useEffect(() => {
        if (
            pendingSavedFilter &&
            savedContentFilters.some(
                (filter) =>
                    filter.id === pendingSavedFilter.id &&
                    filter.query &&
                    JSON.stringify(filter.criteria) === pendingSavedFilter.criteria,
            )
        ) {
            setPendingSavedFilter(null);
        }
    }, [pendingSavedFilter, savedContentFilters]);

    const resolvedAppliedFilter = useMemo(() => {
        if (!appliedSavedFilterId) {
            return null;
        }
        return savedFilterOptions.find((f) => f.id === appliedSavedFilterId) ?? null;
    }, [appliedSavedFilterId, savedFilterOptions]);

    const filterRestoreError =
        appliedSavedFilterId && !resolvedAppliedFilter
            ? (savedContentFilters.find((filter) => filter.id === appliedSavedFilterId)?.queryError ??
              savedFiltersListError ??
              null)
            : null;

    const appliedSavedFilterExists = useMemo(() => {
        if (!appliedSavedFilterId) {
            return false;
        }
        return (
            pendingSavedFilter?.id === appliedSavedFilterId ||
            savedContentFilters.some((f) => f.id === appliedSavedFilterId)
        );
    }, [appliedSavedFilterId, savedContentFilters, pendingSavedFilter]);

    const [divergedFromId, setDivergedFromId] = useState<string | null>(null);
    const resolvedDivergedFilter = useMemo(() => {
        if (!divergedFromId) {
            return null;
        }
        return savedFilterOptions.find((f) => f.id === divergedFromId) ?? null;
    }, [divergedFromId, savedFilterOptions]);

    const referencedSavedFilter = resolvedAppliedFilter ?? resolvedDivergedFilter;

    const urlFilterQuery = useMemo(() => {
        const query = appliedSavedFilterId ? (resolvedAppliedFilter?.query ?? '') : params.filter;
        const legacyStatus = normalizeFulfillmentApiName(params.fulfillment);
        return legacyStatus
            ? [query.trim() ? `(${query})` : '', `fulfillment = ${legacyStatus}`].filter(Boolean).join(' and ')
            : query;
    }, [appliedSavedFilterId, resolvedAppliedFilter, params.filter, params.fulfillment]);

    const commitFilterQuery = useCallback(
        (query: string, replace: boolean, state: { liveFilterCommit: string }) => {
            const reference = referencedSavedFilter;
            if (query.trim().length === 0) {
                updateParams({ filter: '', fulfillment: '', savedFilter: '', page: '1' }, { replace, state });
            } else if (reference && query.trim() === reference.query.trim()) {
                updateParams({ filter: '', fulfillment: '', savedFilter: reference.id, page: '1' }, { replace, state });
            } else {
                setDivergedFromId(reference?.id ?? null);
                updateParams({ filter: query, fulfillment: '', savedFilter: '', page: '1' }, { replace, state });
            }
        },
        [referencedSavedFilter, updateParams],
    );
    const liveFilter = useLiveUrlFilter({
        value: urlFilterQuery,
        onCommit: commitFilterQuery,
        externalKey: `${params.savedFilter}\u0000${params.fulfillment}`,
    });
    const filterQuery = liveFilter.value;

    const [filterBarExpanded, setFilterBarExpanded] = useState<boolean | undefined>(() =>
        params.filter.length > 0 || params.fulfillment.length > 0 || appliedSavedFilterId.length > 0 ? true : undefined,
    );
    const debouncedFilterQuery = useDebouncedValue(filterQuery, 300);

    const sortField = params.sort && VALID_SORT_FIELDS.includes(params.sort) ? params.sort : 'publishDate';
    const sortDir = params.sortDir && VALID_SORT_DIRECTIONS.includes(params.sortDir) ? params.sortDir : 'desc';

    const sorting: SortingState = useMemo(() => [{ id: sortField, desc: sortDir === 'desc' }], [sortField, sortDir]);

    const [saveFilterDialogOpen, , openSaveFilterDialog, closeSaveFilterDialog] = useToggle();
    const [saveFilterError, setSaveFilterError] = useState<string | null>(null);
    const [saveFilterQueryError, setSaveFilterQueryError] = useState<string | null>(null);

    const isAppliedFilterDirty =
        referencedSavedFilter !== null && filterQuery.trim() !== referencedSavedFilter.query.trim();

    const canUpdateAppliedFilter = useMemo(() => {
        if (!referencedSavedFilter) {
            return false;
        }
        const fullFilter = savedContentFilters.find((f) => f.id === referencedSavedFilter.id);
        if (!fullFilter) {
            return false;
        }
        return canEditOwned(fullFilter, user?.id, isAdmin, canWriteFilters);
    }, [referencedSavedFilter, savedContentFilters, user?.id, isAdmin, canWriteFilters]);

    const canOfferUpdate = isAppliedFilterDirty && canUpdateAppliedFilter;

    const createFilterMutation = useCreateContentFilter();
    const updateFilterMutation = useUpdateContentFilter();

    const applyFilterQuery = useCallback(
        (query: string, immediate: boolean) => {
            const reference = referencedSavedFilter;

            setDivergedFromId(
                query.trim().length > 0 && reference && query.trim() !== reference.query.trim() ? reference.id : null,
            );
            liveFilter.change(query, immediate);
        },
        [liveFilter, referencedSavedFilter],
    );

    const handleFilterQueryChange = useCallback(
        (query: string, immediate = false) => {
            if (query.length > 0) {
                setFilterBarExpanded(true);
            }

            applyFilterQuery(query, immediate);
        },
        [applyFilterQuery],
    );

    const handleSortingChange = useCallback(
        (updater: SortingState | ((prev: SortingState) => SortingState)) => {
            const next = typeof updater === 'function' ? updater(sorting) : updater;
            const first = next[0];
            if (!first || !VALID_SORT_FIELDS.includes(first.id)) {
                updateParams({ sort: '', sortDir: '', page: '1' });
            } else {
                updateParams({ sort: first.id, sortDir: first.desc ? 'desc' : 'asc', page: '1' });
            }
        },
        [sorting, updateParams],
    );

    const setFilterField = useCallback(
        (mutate: (query: string) => string) => {
            handleFilterQueryChange(mutate(filterQuery), true);
        },
        [handleFilterQueryChange, filterQuery],
    );

    const headerFilters: ContentHeaderFilters = useMemo(() => {
        const contentSelection = readContentFilterSelection(filterQuery);
        const lockedSelection =
            routeContentTypeSlug && selectedLevel
                ? { contentType: routeContentTypeSlug, level: selectedLevel.slug }
                : null;
        const effectiveSelection =
            lockedSelection ??
            contentSelection ??
            (routeContentTypeSlug ? { contentType: routeContentTypeSlug } : null);
        return {
            contentTypeOptions: buildContentFilterOptions(contentTypes, routeContentTypeSlug),
            contentType: effectiveSelection ? contentFilterSelectionValue(effectiveSelection) : '',
            contentTypeHideAll: Boolean(routeContentTypeSlug),
            contentTypeLocked: Boolean(lockedSelection),
            monitoring: readContentFilter(filterQuery, 'monitoring'),
            fulfillment: readContentFilter(filterQuery, 'fulfillment'),
            title: readContentFilter(filterQuery, 'title'),
            publishDateFrom: readContentFilter(filterQuery, 'publishDateFrom'),
            publishDateTo: readContentFilter(filterQuery, 'publishDateTo'),
            advancedFields: (
                [
                    'contentType',
                    'level',
                    'monitoring',
                    'fulfillment',
                    'title',
                    'publishDateFrom',
                    'publishDateTo',
                ] as const
            ).filter((field) => hasComplexContentFilter(filterQuery, field)),
        };
    }, [contentTypes, filterQuery, routeContentTypeSlug, selectedLevel]);

    const headerFilterHandlers = useMemo(
        () => ({
            onContentTypeChange: (value: string) =>
                setFilterField((query) => setContentFilterSelection(query, parseContentFilterSelectionValue(value))),
            onMonitoringChange: (value: string) => setFilterField((q) => setContentFilter(q, 'monitoring', value)),
            onFulfillmentChange: (value: string) => setFilterField((q) => setContentFilter(q, 'fulfillment', value)),
            onTitleChange: (value: string) => setFilterField((q) => setContentFilter(q, 'title', value)),
            onPublishDateChange: ({ from, to }: { from: string; to: string }) =>
                setFilterField((q) =>
                    setContentFilter(setContentFilter(q, 'publishDateFrom', from), 'publishDateTo', to),
                ),
        }),
        [setFilterField],
    );

    const handleTitlePrefixChange = useCallback(
        (prefix: string) => applyFilterQuery(setContentFilter(filterQuery, 'titlePrefix', prefix), true),
        [applyFilterQuery, filterQuery],
    );

    const missingSavedFilterNoticeRef = useRef(false);
    useEffect(() => {
        if (!appliedSavedFilterId || savedFiltersLoading || savedFiltersListError) {
            return;
        }
        if (appliedSavedFilterExists) {
            missingSavedFilterNoticeRef.current = false;
            if (params.filter && resolvedAppliedFilter) {
                updateParams({ filter: '' }, { replace: true });
            }
            return;
        }
        if (!missingSavedFilterNoticeRef.current) {
            missingSavedFilterNoticeRef.current = true;
            toast({
                title: 'Saved filter not found',
                description: 'That saved filter no longer exists. Showing the full library instead.',
                variant: 'warning',
            });
        }
        updateParams({ savedFilter: '', filter: '' }, { replace: true });
    }, [
        appliedSavedFilterId,
        savedFiltersLoading,
        savedFiltersListError,
        appliedSavedFilterExists,
        resolvedAppliedFilter,
        params.filter,
        updateParams,
        toast,
    ]);

    useEffect(() => {
        if (divergedFromId === null) {
            return;
        }
        const appliedToOtherFilter = appliedSavedFilterId.length > 0 && appliedSavedFilterId !== divergedFromId;
        const fullyCleared = appliedSavedFilterId.length === 0 && params.filter.trim().length === 0;
        if (appliedToOtherFilter || fullyCleared) {
            setDivergedFromId(null);
        }
    }, [divergedFromId, appliedSavedFilterId, params.filter]);

    const hasActiveFilter = filterQuery.trim().length > 0;

    const isOnUnmodifiedSavedFilter = referencedSavedFilter !== null && !isAppliedFilterDirty;
    const showSaveControl = hasActiveFilter && !isOnUnmodifiedSavedFilter;
    const saveControlLabel = canOfferUpdate ? 'Save changes' : 'Save filter';

    const hasActiveSearchOrFilter = hasActiveFilter;

    const handleClearSearchAndFilters = useCallback(() => {
        setDivergedFromId(null);
        liveFilter.change('', true, true);
        setFilterBarExpanded(false);
    }, [liveFilter]);

    const handleOpenSaveFilter = useCallback(() => {
        setSaveFilterError(null);
        setSaveFilterQueryError(null);
        openSaveFilterDialog();
    }, [openSaveFilterDialog]);

    const handleSaveFilter = useCallback(
        async (name: string) => {
            setSaveFilterError(null);
            setSaveFilterQueryError(null);
            const revision = liveFilter.getRevision();

            const parseResult = await parseMagpieQl(filterQuery, 'content');

            if (parseResult.error) {
                setSaveFilterQueryError(parseResult.error.message);
                return;
            }

            try {
                const created = await createFilterMutation.mutateAsync({
                    name,
                    criteria: parseResult.criteria as {
                        contentHierarchyItem?: { and?: unknown[] };
                        mediaFiles?: { and?: unknown[] };
                    },
                });

                if (created?.id && liveFilter.getRevision() === revision) {
                    setPendingSavedFilter({
                        id: created.id,
                        name,
                        query: filterQuery,
                        criteria: JSON.stringify(parseResult.criteria),
                    });
                    liveFilter.cancelPending();
                    setDivergedFromId(null);
                    updateParams(
                        { savedFilter: created.id, filter: '', fulfillment: '' },
                        { replace: true, state: liveFilter.createCommitState() },
                    );
                }

                closeSaveFilterDialog();
                toast({
                    title: 'Filter saved',
                    description: `${instanceName} saved "${name}" to filters.`,
                    variant: 'success',
                });
            } catch {
                setSaveFilterError('Failed to save filter. Please try again.');
            }
        },
        [closeSaveFilterDialog, filterQuery, createFilterMutation, updateParams, toast, liveFilter, instanceName],
    );

    const handleUpdateFilter = useCallback(async () => {
        if (!referencedSavedFilter) {
            return;
        }
        setSaveFilterError(null);
        setSaveFilterQueryError(null);
        const revision = liveFilter.getRevision();

        const parseResult = await parseMagpieQl(filterQuery, 'content');
        if (parseResult.error) {
            setSaveFilterQueryError(parseResult.error.message);
            return;
        }

        try {
            await updateFilterMutation.mutateAsync({
                id: referencedSavedFilter.id,
                criteria: { value: parseResult.criteria as ContentFilterCriteria },
            });

            const isCurrent = liveFilter.getRevision() === revision;
            setPendingSavedFilter((current) =>
                !isCurrent && current && current.id !== referencedSavedFilter.id
                    ? current
                    : {
                          id: referencedSavedFilter.id,
                          name: referencedSavedFilter.name,
                          query: filterQuery,
                          criteria: JSON.stringify(parseResult.criteria),
                      },
            );
            if (isCurrent) {
                liveFilter.cancelPending();
                setDivergedFromId(null);
                updateParams(
                    { savedFilter: referencedSavedFilter.id, filter: '', fulfillment: '' },
                    { replace: true, state: liveFilter.createCommitState() },
                );
            }

            closeSaveFilterDialog();
            toast({
                title: 'Filter updated',
                description: `${instanceName} updated "${referencedSavedFilter.name}".`,
                variant: 'success',
            });
        } catch {
            setSaveFilterError('Failed to update filter. Please try again.');
        }
    }, [
        referencedSavedFilter,
        filterQuery,
        updateFilterMutation,
        updateParams,
        closeSaveFilterDialog,
        toast,
        liveFilter,
        instanceName,
    ]);

    return {
        isRestoringFilter: Boolean(
            appliedSavedFilterId &&
            (!resolvedAppliedFilter || (debouncedFilterQuery !== filterQuery && filterQuery === urlFilterQuery)),
        ),
        filterRestoreError,
        retrySavedFilters,
        filterQuery,
        debouncedFilterQuery,
        titlePrefix: readContentFilter(filterQuery, 'titlePrefix'),
        handleTitlePrefixChange,
        sortField,
        sortDir,
        sorting,
        handleSortingChange,
        handleFilterQueryChange,
        headerFilters,
        headerFilterHandlers,
        filterBarExpanded,
        setFilterBarExpanded,
        referencedSavedFilter,
        isAppliedFilterDirty,
        hasActiveSearchOrFilter,
        handleClearSearchAndFilters,
        showSaveControl,
        saveControlLabel,
        canOfferUpdate,
        savedContentFilters,
        saveFilterDialogOpen,
        closeSaveFilterDialog,
        handleOpenSaveFilter,
        handleSaveFilter,
        handleUpdateFilter,
        saveFilterError,
        saveFilterQueryError,
        isSavingFilter: createFilterMutation.isPending,
        isUpdatingFilter: updateFilterMutation.isPending,
    };
}
