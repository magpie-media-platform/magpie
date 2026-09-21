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
import { useToggle } from '@/hooks/useToggle';
import { Role, type User } from '@/auth';
import { type ApiFulfillmentStatus } from '@/types';
import { parseMagpieQl } from '@/lib/magpieQl';
import { canEditOwned } from '@/lib/ownership';
import {
    readContentFilter,
    readContentFilterSelection,
    setContentFilter,
    setContentFilterSelection,
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
    canManageContent: boolean;
    hasRole: (minRole: Role) => boolean;
}

export function useLibraryFilters({
    params,
    updateParams,
    contentTypes,
    routeContentTypeSlug,
    selectedLevel,
    user,
    canManageContent,
    hasRole,
}: UseLibraryFiltersArgs) {
    const { toast } = useToast();

    const appliedSavedFilterId = params.savedFilter;

    const { filters: savedContentFilters, isLoading: savedFiltersLoading } = useContentFilters();
    const savedFilterOptions = useMemo(
        () => savedContentFilters.filter((f) => f.query).map((f) => ({ id: f.id, name: f.name, query: f.query })),
        [savedContentFilters],
    );

    const resolvedAppliedFilter = useMemo(() => {
        if (!appliedSavedFilterId) {
            return null;
        }
        return savedFilterOptions.find((f) => f.id === appliedSavedFilterId) ?? null;
    }, [appliedSavedFilterId, savedFilterOptions]);

    const appliedSavedFilterExists = useMemo(() => {
        if (!appliedSavedFilterId) {
            return false;
        }
        return savedContentFilters.some((f) => f.id === appliedSavedFilterId);
    }, [appliedSavedFilterId, savedContentFilters]);

    const [divergedFromId, setDivergedFromId] = useState<string | null>(null);
    const resolvedDivergedFilter = useMemo(() => {
        if (!divergedFromId) {
            return null;
        }
        return savedFilterOptions.find((f) => f.id === divergedFromId) ?? null;
    }, [divergedFromId, savedFilterOptions]);

    const referencedSavedFilter = resolvedAppliedFilter ?? resolvedDivergedFilter;

    const filterQuery = useMemo(() => {
        if (appliedSavedFilterId) {
            return resolvedAppliedFilter ? resolvedAppliedFilter.query : '';
        }
        return params.filter;
    }, [appliedSavedFilterId, resolvedAppliedFilter, params.filter]);

    const [filterBarExpanded, setFilterBarExpanded] = useState<boolean | undefined>(() =>
        params.filter.length > 0 || appliedSavedFilterId.length > 0 ? true : undefined,
    );
    const debouncedFilterQuery = useDebouncedValue(filterQuery, 300);

    const fulfillment: ApiFulfillmentStatus | '' = normalizeFulfillmentApiName(params.fulfillment) ?? '';

    const sortField = params.sort && VALID_SORT_FIELDS.includes(params.sort) ? params.sort : 'publishDate';
    const sortDir = params.sortDir && VALID_SORT_DIRECTIONS.includes(params.sortDir) ? params.sortDir : 'desc';

    const sorting: SortingState = useMemo(() => [{ id: sortField, desc: sortDir === 'desc' }], [sortField, sortDir]);

    const [saveFilterDialogOpen, , openSaveFilterDialog, closeSaveFilterDialog] = useToggle();
    const [saveFilterError, setSaveFilterError] = useState<string | null>(null);

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
        return canEditOwned(fullFilter, user?.id, canManageContent, hasRole, Role.Requester);
    }, [referencedSavedFilter, savedContentFilters, user?.id, canManageContent, hasRole]);

    const canOfferUpdate = isAppliedFilterDirty && canUpdateAppliedFilter;

    const createFilterMutation = useCreateContentFilter();
    const updateFilterMutation = useUpdateContentFilter();

    const handleFilterQueryChange = useCallback(
        (query: string) => {
            if (query.length > 0) {
                setFilterBarExpanded(true);
            }

            const reference = referencedSavedFilter;

            if (query.trim().length === 0) {
                setDivergedFromId(null);
                updateParams({ filter: '', savedFilter: '', page: '1' });
                return;
            }

            if (reference && query.trim() === reference.query.trim()) {
                setDivergedFromId(null);
                updateParams({ filter: '', savedFilter: reference.id, page: '1' });
                return;
            }

            setDivergedFromId(reference ? reference.id : null);
            updateParams({ filter: query, savedFilter: '', page: '1' });
        },
        [updateParams, referencedSavedFilter],
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
            handleFilterQueryChange(mutate(filterQuery));
        },
        [handleFilterQueryChange, filterQuery],
    );

    const handleFulfillmentChange = useCallback(
        (value: string) => {
            const next = normalizeFulfillmentApiName(value);
            updateParams({ fulfillment: next ?? '', page: '1' });
        },
        [updateParams],
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
            fulfillment,
            title: readContentFilter(filterQuery, 'title'),
            publishDateFrom: readContentFilter(filterQuery, 'publishDateFrom'),
            publishDateTo: readContentFilter(filterQuery, 'publishDateTo'),
        };
    }, [contentTypes, filterQuery, fulfillment, routeContentTypeSlug, selectedLevel]);

    const headerFilterHandlers = useMemo(
        () => ({
            onContentTypeChange: (value: string) =>
                setFilterField((query) => setContentFilterSelection(query, parseContentFilterSelectionValue(value))),
            onMonitoringChange: (value: string) => setFilterField((q) => setContentFilter(q, 'monitoring', value)),
            onFulfillmentChange: handleFulfillmentChange,
            onTitleChange: (value: string) => setFilterField((q) => setContentFilter(q, 'title', value)),
            onPublishDateChange: ({ from, to }: { from: string; to: string }) =>
                setFilterField((q) =>
                    setContentFilter(setContentFilter(q, 'publishDateFrom', from), 'publishDateTo', to),
                ),
        }),
        [setFilterField, handleFulfillmentChange],
    );

    const missingSavedFilterNoticeRef = useRef(false);
    useEffect(() => {
        if (!appliedSavedFilterId || savedFiltersLoading) {
            return;
        }
        if (appliedSavedFilterExists) {
            missingSavedFilterNoticeRef.current = false;
            if (params.filter) {
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
    }, [appliedSavedFilterId, savedFiltersLoading, appliedSavedFilterExists, params.filter, updateParams, toast]);

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

    const hasActiveSearchOrFilter = hasActiveFilter || fulfillment.length > 0;

    const handleClearSearchAndFilters = useCallback(() => {
        setDivergedFromId(null);
        updateParams({ filter: '', fulfillment: '', savedFilter: '', page: '1' });
        setFilterBarExpanded(false);
    }, [updateParams]);

    const handleOpenSaveFilter = useCallback(() => {
        setSaveFilterError(null);
        openSaveFilterDialog();
    }, [openSaveFilterDialog]);

    const handleSaveFilter = useCallback(
        async (name: string) => {
            setSaveFilterError(null);

            const parseResult = await parseMagpieQl(filterQuery, 'content');

            if (parseResult.error) {
                setSaveFilterError(parseResult.error.message);
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

                if (created?.id) {
                    setDivergedFromId(null);
                    updateParams({ savedFilter: created.id, filter: '' }, { replace: true });
                }

                closeSaveFilterDialog();
                toast({
                    title: 'Filter saved',
                    description: `Magpie saved "${name}" to filters.`,
                    variant: 'success',
                });
            } catch {
                setSaveFilterError('Failed to save filter. Please try again.');
            }
        },
        [closeSaveFilterDialog, filterQuery, createFilterMutation, updateParams, toast],
    );

    const handleUpdateFilter = useCallback(async () => {
        if (!referencedSavedFilter) {
            return;
        }
        setSaveFilterError(null);

        const parseResult = await parseMagpieQl(filterQuery, 'content');
        if (parseResult.error) {
            setSaveFilterError(parseResult.error.message);
            return;
        }

        try {
            await updateFilterMutation.mutateAsync({
                id: referencedSavedFilter.id,
                criteria: { value: parseResult.criteria as ContentFilterCriteria },
            });

            setDivergedFromId(null);
            updateParams({ savedFilter: referencedSavedFilter.id, filter: '' }, { replace: true });

            closeSaveFilterDialog();
            toast({
                title: 'Filter updated',
                description: `Magpie updated "${referencedSavedFilter.name}".`,
                variant: 'success',
            });
        } catch {
            setSaveFilterError('Failed to update filter. Please try again.');
        }
    }, [referencedSavedFilter, filterQuery, updateFilterMutation, updateParams, closeSaveFilterDialog, toast]);

    return {
        filterQuery,
        debouncedFilterQuery,
        fulfillment,
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
        isSavingFilter: createFilterMutation.isPending,
        isUpdatingFilter: updateFilterMutation.isPending,
    };
}
