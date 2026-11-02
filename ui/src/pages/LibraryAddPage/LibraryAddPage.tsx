import { ContentGallerySkeleton, ContentViewToggle } from '@/components/content/ContentGallery';
import { useContentView } from '@/hooks/useContentView';
import { useUrlFilterState } from '@/hooks/useUrlFilterState';
import { useUrlTableState } from '@/hooks/useUrlTableState';
import { useLiveUrlFilter } from '@/hooks/useLiveUrlFilter';
import { AddContentResults } from './AddContentResults';
import { useCallback, useEffect, useLayoutEffect, useMemo, useRef, useState } from 'react';
import { Link, useLocation, useNavigate } from 'react-router-dom';
import { useOpenLocalContentPreview } from '@/components/content/LocalContentPreview';
import { type SortingState } from '@tanstack/react-table';

import {
    BinocularsIcon,
    FloppyDiskIcon,
    PencilSimpleIcon,
    PulseIcon,
    SparkleIcon,
    StarIcon,
    WarningCircleIcon,
} from '@phosphor-icons/react';

import { Button } from '@/components/ui/Button';
import { EmptyState } from '@/components/ui/EmptyState';
import { FilterBar } from '@/components/filters/FilterBar';
import { Label } from '@/components/ui/Label';
import { type MobileListSortOption } from '@/components/ui/MobileListToolbar';
import { Pager } from '@/components/ui/Pager';
import { PageHeader } from '@/components/ui/PageHeader';
import { PreviewDrawer, type PreviewDrawerTarget } from '@/components/content/PreviewDrawer';
import { ProviderSearchWarnings } from '@/components/content/ProviderSearchWarnings';
import { SaveFilterDialog } from '@/components/ui/SaveFilterDialog';
import { Select, SelectContent, SelectItem, SelectTrigger } from '@/components/ui/Select';
import { Skeleton } from '@/components/ui/Skeleton';
import { Tabs, TabsList, TabsTrigger } from '@/components/ui/Tabs';
import { useToast } from '@/components/ui/Toast';
import { useAddContentByExternalId } from '@/hooks/useAddContentByExternalId';
import {
    useProviderMetadataCandidateSearch,
    type ProviderMetadataCandidate,
} from '@/hooks/useProviderMetadataCandidateSearch';
import { useDocumentTitle } from '@/hooks/useDocumentTitle';
import { getRootLevels, useLibraryTypes } from '@/hooks/useLibraryTypes';
import { useContentFilters, useCreateContentFilter, type ContentFilterCriteria } from '@/hooks/useFilters';
import { useRootFolders, type ApiRootFolder } from '@/hooks/useRootFolders';
import { useToggle } from '@/hooks/useToggle';
import { useCardLayout } from '@/hooks/ui/useCardLayout';
import { useInstanceName } from '@/hooks/useInstanceName';
import { setContentFilterSelection } from '@/lib/contentColumnFilters';
import { candidateSearchErrorMessage, getErrorMessage } from '@/lib/errors';
import { isAbortError } from '@/lib/api';
import { parseMagpieQl } from '@/lib/magpieQl';
import { toPlainTextSummary } from '@/lib/plainTextSummary';
import type { LibraryAddPreloadState, WishlistsPageCreateState } from '@/types/navigation';

import { type AddableCandidate, type RowActionContext } from './buildAddableColumns';
import './LibraryAddPage.css';

const SAVED_FILTER_NONE_VALUE = '__none__';

const DEFAULT_SORTING: SortingState = [];

type AddTab = 'live' | 'saved';

function fromProviderMetadataCandidate(candidate: ProviderMetadataCandidate): AddableCandidate {
    return {
        key: `${candidate.providerType}:${candidate.externalIdSource}:${candidate.externalId}`,
        providerType: candidate.providerType,
        externalIdSource: candidate.externalIdSource,
        externalId: candidate.externalId,
        contentType: candidate.contentType,
        levelSlug: candidate.level ?? undefined,
        title: candidate.title?.trim() || 'Untitled',
        year: candidate.year != null ? String(candidate.year) : undefined,
        yearValue: candidate.year ?? undefined,
        releaseDate: candidate.releaseDate ?? undefined,
        releaseDatePrecision: candidate.releaseDatePrecision ?? undefined,
        overview: toPlainTextSummary(candidate.overview) || undefined,
        thumbnailUrl: candidate.thumbnailUrl,
        inLibrary: candidate.inLibrary ?? false,
        existingContentHierarchyItemId: candidate.existingContentHierarchyItemId ?? undefined,
    };
}

function normalizeTitleForDisplayMatch(title: string): string {
    return title
        .normalize('NFD')
        .replace(/[̀-ͯ'’]/g, '')
        .replace(/[^\p{L}\p{N}]+/gu, ' ')
        .trim()
        .toLowerCase();
}

function mergeEquivalentCandidates(primary: AddableCandidate, secondary: AddableCandidate): AddableCandidate {
    return {
        ...primary,
        releaseDate: primary.releaseDate ?? secondary.releaseDate,
        releaseDatePrecision: primary.releaseDate ? primary.releaseDatePrecision : secondary.releaseDatePrecision,
        overview: primary.overview || secondary.overview,
        thumbnailUrl: primary.thumbnailUrl || secondary.thumbnailUrl,
        inLibrary: primary.inLibrary || secondary.inLibrary,
        existingContentHierarchyItemId:
            primary.existingContentHierarchyItemId ?? secondary.existingContentHierarchyItemId,
    };
}

function coalesceEquivalentCandidates(
    candidates: AddableCandidate[],
    rootLevelKeys: ReadonlySet<string>,
): AddableCandidate[] {
    const positions = new Map<string, number>();
    const coalesced: AddableCandidate[] = [];
    for (const candidate of candidates) {
        const title = normalizeTitleForDisplayMatch(candidate.title);
        const isRootLevel =
            !!candidate.levelSlug && rootLevelKeys.has(`${candidate.contentType}:${candidate.levelSlug}`);
        if (!isRootLevel || title.length === 0 || candidate.yearValue == null) {
            coalesced.push(candidate);
            continue;
        }

        const identity = `${candidate.contentType}:${candidate.levelSlug}:${title}:${candidate.yearValue}`;
        const position = positions.get(identity);
        if (position === undefined) {
            positions.set(identity, coalesced.length);
            coalesced.push(candidate);
            continue;
        }

        coalesced[position] = mergeEquivalentCandidates(coalesced[position], candidate);
    }

    return coalesced;
}

const ADD_CONTENT_SORT_OPTIONS: MobileListSortOption[] = [
    { id: 'title', label: 'Title' },
    { id: 'type', label: 'Type' },
    { id: 'published', label: 'Published', initialDirection: 'desc' },
];

function noResultsDescription(canGoNext: boolean, page: number, hasWarnings: boolean): string {
    if (hasWarnings) {
        return 'Results from some providers are missing; the warnings below say why.';
    }
    if (canGoNext) {
        return 'Continue to the next page to see more results.';
    }
    if (page > 1) {
        return 'Return to a previous page to see earlier results.';
    }
    return 'No providers returned content matching this filter. Try broader criteria, or save it as a wishlist to keep checking for new matches.';
}

export function LibraryAddPage() {
    const instanceName = useInstanceName();
    useDocumentTitle('Add content');
    const navigate = useNavigate();
    const openLocalPreview = useOpenLocalContentPreview();
    const location = useLocation();
    const { toast } = useToast();

    const { folders, isLoading: isLoadingFolders } = useRootFolders();
    const { contentTypes } = useLibraryTypes();
    const [urlState, setUrlState] = useUrlFilterState({
        tab: 'live',
        filter: '',
        savedFilter: '',
        query: '',
        continuation: '',
        page: '',
        sort: '',
    });
    const setUrlStateRef = useRef(setUrlState);
    setUrlStateRef.current = setUrlState;
    const tab: AddTab = urlState.tab === 'saved' ? 'saved' : 'live';
    const { page, sorting } = useUrlTableState({
        defaultSort: DEFAULT_SORTING,
        defaultFilters: {},
        sortKeys: ADD_CONTENT_SORT_OPTIONS.map((option) => option.id),
    });
    const { view, setView } = useContentView('library-add-view');
    const [searchVersion, setSearchVersion] = useState(0);
    const [addingKeys, setAddingKeys] = useState<ReadonlySet<string>>(() => new Set());
    const [bulkAdding, setBulkAdding] = useState(false);
    const [addedKeys, setAddedKeys] = useState<Set<string>>(() => new Set());
    const [addedItemIds, setAddedItemIds] = useState<Map<string, string>>(() => new Map());
    const [previewCandidate, setPreviewCandidate] = useState<AddableCandidate | null>(null);
    const previewCandidateKey = useRef<string | null>(null);
    const [previewOpen, setPreviewOpen] = useState(false);

    const commitFilter = useCallback(
        (filter: string, replace: boolean, state: { liveFilterCommit: string }) => {
            setUrlState({ filter }, { replace, state });
        },
        [setUrlState],
    );
    const liveFilter = useLiveUrlFilter({ value: urlState.filter, onCommit: commitFilter });
    const filterQuery = liveFilter.value;
    const [saveFilterDialogOpen, , openSaveFilterDialog, closeSaveFilterDialog] = useToggle();
    const [saveFilterError, setSaveFilterError] = useState<string | null>(null);
    const [saveFilterQueryError, setSaveFilterQueryError] = useState<string | null>(null);
    const [candidateSearchError, setCandidateSearchError] = useState<string | null>(null);

    const liveCandidateSearch = useProviderMetadataCandidateSearch();
    const savedCandidateSearch = useProviderMetadataCandidateSearch();

    const { mutateAsync: addContent } = useAddContentByExternalId();

    const { filters: savedContentFilters, isLoading: savedFiltersLoading } = useContentFilters();
    const savedFilterOptions = useMemo(
        () => savedContentFilters.filter((f) => f.query).map((f) => ({ id: f.id, name: f.name, query: f.query })),
        [savedContentFilters],
    );
    const selectedSavedFilterId = urlState.savedFilter;
    const createFilterMutation = useCreateContentFilter();

    const searchRequestRef = useRef('');
    const candidateRequestVersion = useRef(0);
    const handleFilterQueryChange = liveFilter.change;

    const foldersForContentType = useCallback(
        (contentType: string): ApiRootFolder[] => {
            if (!contentType) {
                return [];
            }
            return folders.filter((f) => f.contentType === contentType);
        },
        [folders],
    );

    const pendingInlineFocusKeyRef = useRef<string | null>(null);

    useLayoutEffect(() => {
        const key = pendingInlineFocusKeyRef.current;
        if (!key || !addedKeys.has(key) || addingKeys.has(key)) {
            return;
        }
        pendingInlineFocusKeyRef.current = null;
        const active = document.activeElement;
        if (
            active instanceof HTMLElement &&
            active !== document.body &&
            active.dataset.resultKey !== key &&
            !active.closest('[data-radix-popper-content-wrapper]')
        ) {
            return;
        }
        const target = Array.from(
            document.querySelectorAll<HTMLElement>(
                '[data-action="open-library-item"], [data-action="in-library"]:not(:disabled)',
            ),
        ).find((element) => element.dataset.resultKey === key);
        target?.focus({ preventScroll: true });
    }, [addedKeys, addingKeys]);

    const handleAddSuccess = useCallback(
        (candidateKey: string, title: string, contentHierarchyItemId: string) => {
            setAddedKeys((current) => new Set(current).add(candidateKey));
            setAddedItemIds((current) => new Map(current).set(candidateKey, contentHierarchyItemId));
            toast({
                title: 'Added to library',
                description: `${instanceName} added "${title}" to the library. ${instanceName} queued a metadata refresh.`,
                variant: 'success',
            });
            if (previewCandidateKey.current === candidateKey) setPreviewOpen(false);
        },
        [toast, instanceName],
    );

    const handleAdd = useCallback(
        async (candidate: AddableCandidate, rootFolderId: string) => {
            if (
                !candidate.contentType ||
                !rootFolderId ||
                candidate.inLibrary ||
                addedKeys.has(candidate.key) ||
                addingKeys.has(candidate.key) ||
                bulkAdding
            ) {
                return;
            }

            setAddingKeys((current) => new Set(current).add(candidate.key));

            try {
                const id = await addContent({
                    providerType: candidate.providerType,
                    externalIdKey: candidate.externalIdSource,
                    externalId: candidate.externalId,
                    contentType: candidate.contentType,
                    ...(candidate.levelSlug ? { levelSlug: candidate.levelSlug } : {}),
                    rootFolderId,
                });
                pendingInlineFocusKeyRef.current = candidate.key;
                handleAddSuccess(candidate.key, candidate.title, id);
            } catch (error) {
                toast({
                    title: 'Failed to add content',
                    description: getErrorMessage(error, 'Failed to add content.'),
                    variant: 'error',
                });
            } finally {
                setAddingKeys((current) => {
                    const next = new Set(current);
                    next.delete(candidate.key);
                    return next;
                });
            }
        },
        [addContent, handleAddSuccess, toast, addedKeys, addingKeys, bulkAdding],
    );

    const handlePreview = useCallback(
        (candidate: AddableCandidate, invokingElement?: HTMLElement) => {
            if (candidate.inLibrary) {
                if (candidate.existingContentHierarchyItemId)
                    openLocalPreview(candidate.existingContentHierarchyItemId, invokingElement);
                return;
            }
            previewCandidateKey.current = candidate.key;
            setPreviewCandidate(candidate);
            setPreviewOpen(true);
        },
        [openLocalPreview],
    );

    const previewTarget: PreviewDrawerTarget | null = useMemo(() => {
        if (!previewCandidate || !previewCandidate.contentType) {
            return null;
        }
        return {
            providerType: previewCandidate.providerType,
            externalIdKey: previewCandidate.externalIdSource,
            externalId: previewCandidate.externalId,
            contentType: previewCandidate.contentType,
            levelSlug: previewCandidate.levelSlug,
            fallbackTitle: previewCandidate.title,
            fallbackYear: previewCandidate.year,
        };
    }, [previewCandidate]);

    const previewAddOptions = useMemo(() => {
        if (!previewCandidate) {
            return [];
        }
        return foldersForContentType(previewCandidate.contentType).map((f) => ({ id: f.id, label: f.name }));
    }, [previewCandidate, foldersForContentType]);

    const previewAddDisabledReason: string | null = useMemo(() => {
        if (!previewCandidate) {
            return null;
        }
        return previewAddOptions.length === 0 ? 'Configure a root folder for this content type before adding.' : null;
    }, [previewCandidate, previewAddOptions]);

    const handleDrawerAdd = useCallback(
        (_target: PreviewDrawerTarget, rootFolderId?: string) => {
            if (previewCandidate && rootFolderId) {
                void handleAdd(previewCandidate, rootFolderId);
            }
        },
        [handleAdd, previewCandidate],
    );

    const runCandidateSearch = useCallback(
        async (kind: AddTab, query: string, requestedPage: number, continuation = '') => {
            const key = JSON.stringify([kind, query, requestedPage, continuation]);
            searchRequestRef.current = key;
            if (!continuation && requestedPage === 1) setSearchVersion((current) => current + 1);
            const version = ++candidateRequestVersion.current;
            setCandidateSearchError(null);
            const search = kind === 'saved' ? savedCandidateSearch : liveCandidateSearch;
            const other = kind === 'saved' ? liveCandidateSearch : savedCandidateSearch;
            other.reset();
            try {
                const response = await search.mutateAsync({
                    query,
                    page: requestedPage,
                    resume: !!continuation || requestedPage > 1,
                    ...(continuation ? { continuation } : {}),
                });
                if (
                    version === candidateRequestVersion.current &&
                    response.continuation &&
                    response.continuation !== continuation
                ) {
                    searchRequestRef.current = JSON.stringify([kind, query, requestedPage, response.continuation]);
                    setUrlStateRef.current({ continuation: response.continuation }, { replace: true });
                }
            } catch (error) {
                if (version === candidateRequestVersion.current && !isAbortError(error)) {
                    setCandidateSearchError(candidateSearchErrorMessage(error));
                }
            }
        },
        [liveCandidateSearch, savedCandidateSearch],
    );

    const applySearch = useCallback(
        (query: string) => {
            liveFilter.cancelPending();
            if (page === 1 && urlState.query === query && !urlState.continuation) {
                void runCandidateSearch(tab, query, 1);
            }
            setUrlState({ query, filter: filterQuery, page: '', continuation: '' });
        },
        [filterQuery, liveFilter, page, runCandidateSearch, setUrlState, tab, urlState.query, urlState.continuation],
    );

    const handleApplyFilter = useCallback(() => {
        if (!bulkAdding && filterQuery.trim()) applySearch(filterQuery);
    }, [applySearch, filterQuery, bulkAdding]);

    const handleApplySavedFilter = useCallback(() => {
        const filter = savedContentFilters.find((f) => f.id === selectedSavedFilterId);
        if (!bulkAdding && filter?.query.trim()) applySearch(filter.query);
    }, [savedContentFilters, selectedSavedFilterId, applySearch, bulkAdding]);

    const handleSavedFilterSelectChange = useCallback(
        (value: string) =>
            setUrlState({
                savedFilter: value === SAVED_FILTER_NONE_VALUE ? '' : value,
                query: '',
                page: '',
                continuation: '',
            }),
        [setUrlState],
    );

    const handleCreateWishlistFromFilter = useCallback(async () => {
        let criteria: ContentFilterCriteria | null = null;
        let name = '';

        if (tab === 'saved') {
            const filter = savedContentFilters.find((f) => f.id === selectedSavedFilterId);
            if (filter) {
                criteria = filter.criteria;
                name = filter.name;
            }
        } else if (filterQuery.trim()) {
            const parseResult = await parseMagpieQl(filterQuery, 'content');
            if (!parseResult.error) {
                criteria = parseResult.criteria as ContentFilterCriteria;
            }
        }

        if (!criteria) {
            return;
        }
        const createState: WishlistsPageCreateState = { createCriteria: criteria, createName: name };
        void navigate('/wishlists', { state: createState });
    }, [tab, savedContentFilters, selectedSavedFilterId, filterQuery, navigate]);

    const selectedSavedFilter = useMemo(
        () => savedFilterOptions.find((f) => f.id === selectedSavedFilterId),
        [savedFilterOptions, selectedSavedFilterId],
    );

    const preload = location.state as LibraryAddPreloadState | null;
    const preloadAppliedRef = useRef<string | null>(null);
    useEffect(() => {
        if (preloadAppliedRef.current === location.key || !preload?.filterQuery) return;
        if (preload.savedFilterId && savedFiltersLoading) return;
        const saved = savedContentFilters.find((filter) => filter.id === preload.savedFilterId);
        if (saved && !saved.query.trim()) {
            if (tab !== 'saved' || selectedSavedFilterId !== saved.id) {
                setUrlState(
                    { tab: 'saved', savedFilter: saved.id, filter: preload.filterQuery },
                    {
                        replace: true,
                        state: preload,
                    },
                );
            }
            return;
        }
        preloadAppliedRef.current = location.key;
        setUrlState(
            {
                tab: saved ? 'saved' : 'live',
                savedFilter: saved?.id ?? '',
                filter: preload.filterQuery,
                query: saved?.query ?? preload.filterQuery,
                page: '',
                continuation: '',
            },
            { replace: true, state: null },
        );
    }, [location.key, preload, savedContentFilters, savedFiltersLoading, setUrlState, tab, selectedSavedFilterId]);

    const runSearchRef = useRef(runCandidateSearch);
    runSearchRef.current = runCandidateSearch;
    const resetLiveSearch = liveCandidateSearch.reset;
    const resetSavedSearch = savedCandidateSearch.reset;
    useEffect(() => {
        const contentType = new URLSearchParams(location.search).get('contentType')?.trim();
        if (!contentType || preload?.filterQuery) return;
        setUrlState(
            { filter: urlState.filter || setContentFilterSelection('', { contentType }) },
            {
                replace: true,
                resetParams: ['contentType'],
            },
        );
    }, [location.search, preload?.filterQuery, setUrlState, urlState.filter]);

    useEffect(() => {
        const key = JSON.stringify([tab, urlState.query, page, urlState.continuation]);
        if (searchRequestRef.current === key) return;
        searchRequestRef.current = key;
        if (urlState.query.trim()) {
            void runSearchRef.current(tab, urlState.query, page, urlState.continuation);
        } else {
            candidateRequestVersion.current++;
            setSearchVersion((current) => current + 1);
            setCandidateSearchError(null);
            resetLiveSearch();
            resetSavedSearch();
        }
    }, [tab, urlState.query, urlState.continuation, page, resetLiveSearch, resetSavedSearch]);

    useEffect(() => {
        setPreviewOpen(false);
    }, [tab, urlState.query, page]);

    const handleOpenSaveFilter = useCallback(() => {
        setSaveFilterError(null);
        setSaveFilterQueryError(null);
        openSaveFilterDialog();
    }, [openSaveFilterDialog]);

    const handleSaveFilter = useCallback(
        async (name: string) => {
            setSaveFilterError(null);
            setSaveFilterQueryError(null);

            const parseResult = await parseMagpieQl(filterQuery, 'content');
            if (parseResult.error) {
                setSaveFilterQueryError(parseResult.error.message);
                return;
            }

            try {
                await createFilterMutation.mutateAsync({
                    name,
                    criteria: parseResult.criteria as {
                        contentHierarchyItem?: { and?: unknown[] };
                        mediaFiles?: { and?: unknown[] };
                    },
                });

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
        [closeSaveFilterDialog, createFilterMutation, filterQuery, toast, instanceName],
    );

    const activeSearchKind = tab === 'saved' ? 'saved' : 'adhoc';

    const handleCancelCandidateSearch = useCallback(() => {
        candidateRequestVersion.current++;
        const search = tab === 'saved' ? savedCandidateSearch : liveCandidateSearch;
        const completed = search.cancel();
        const query = completed?.query ?? '';
        const restoredPage = completed?.page ?? 1;
        const continuation = completed ? (search.getContinuation(restoredPage) ?? search.data?.continuation ?? '') : '';
        searchRequestRef.current = JSON.stringify([tab, query, restoredPage, continuation]);
        setCandidateSearchError(null);
        setUrlState({ query, page: restoredPage > 1 ? String(restoredPage) : '', continuation }, { replace: true });
    }, [tab, liveCandidateSearch, savedCandidateSearch, setUrlState]);

    const providerCandidateSearch = useMemo(
        () =>
            activeSearchKind === 'saved'
                ? {
                      isPending: savedCandidateSearch.isPending,
                      isError: savedCandidateSearch.isError,
                      isSuccess: savedCandidateSearch.isSuccess,
                      data: savedCandidateSearch.data,
                  }
                : {
                      isPending: liveCandidateSearch.isPending,
                      isError: liveCandidateSearch.isError,
                      isSuccess: liveCandidateSearch.isSuccess,
                      data: liveCandidateSearch.data,
                  },
        [
            activeSearchKind,
            liveCandidateSearch.isPending,
            liveCandidateSearch.isError,
            liveCandidateSearch.isSuccess,
            liveCandidateSearch.data,
            savedCandidateSearch.isPending,
            savedCandidateSearch.isError,
            savedCandidateSearch.isSuccess,
            savedCandidateSearch.data,
        ],
    );

    const rootLevelKeys = useMemo(
        () => new Set(contentTypes.flatMap((type) => getRootLevels(type).map((level) => `${type.slug}:${level.slug}`))),
        [contentTypes],
    );

    const candidates: AddableCandidate[] = useMemo(
        () =>
            coalesceEquivalentCandidates(
                (providerCandidateSearch.data?.candidates ?? []).map((candidate) => {
                    const addableCandidate = fromProviderMetadataCandidate(candidate);
                    const addedItemId = addedItemIds.get(addableCandidate.key);
                    return addedItemId
                        ? {
                              ...addableCandidate,
                              inLibrary: true,
                              existingContentHierarchyItemId: addedItemId,
                          }
                        : addableCandidate;
                }),
                rootLevelKeys,
            ),
        [addedItemIds, providerCandidateSearch.data, rootLevelKeys],
    );

    const warnings = providerCandidateSearch.data?.warnings ?? [];

    const hasRunCandidateSearch =
        providerCandidateSearch.isSuccess || providerCandidateSearch.isError || providerCandidateSearch.isPending;

    const canGoNext = providerCandidateSearch.data?.pageInfo.hasMore ?? false;

    const showInitialEmpty = !hasRunCandidateSearch && !candidateSearchError;
    const showLoading = providerCandidateSearch.isPending;
    const showError = (!!candidateSearchError || providerCandidateSearch.isError) && !showLoading;
    const showNoResults =
        !showLoading && !showError && !showInitialEmpty && candidates.length === 0 && providerCandidateSearch.isSuccess;

    const hasResultsForPaging =
        !showLoading && !showError && !showInitialEmpty && (candidates.length > 0 || canGoNext || page > 1);

    const canCreateWishlistFromSearch = tab === 'saved' ? !!selectedSavedFilterId : filterQuery.trim().length > 0;
    const canGoPrev = page > 1;

    const goToPage = useCallback(
        (nextPage: number) => {
            const target = Math.max(1, nextPage);
            const search = tab === 'saved' ? savedCandidateSearch : liveCandidateSearch;
            setUrlState({ page: target > 1 ? String(target) : '', continuation: search.getContinuation(target) ?? '' });
        },
        [tab, liveCandidateSearch, savedCandidateSearch, setUrlState],
    );

    const handleSortingChange = useCallback(
        (updater: SortingState | ((previous: SortingState) => SortingState)) => {
            const next = typeof updater === 'function' ? updater(sorting) : updater;
            setUrlState({
                sort: next.map(({ id, desc }) => `${id}:${desc ? 'desc' : 'asc'}`).join(','),
            });
        },
        [sorting, setUrlState],
    );

    const switchTab = useCallback(
        (next: AddTab) => {
            liveFilter.cancelPending();
            setUrlState({ tab: next, filter: filterQuery, query: '', page: '', continuation: '' });
        },
        [filterQuery, liveFilter, setUrlState],
    );

    const hasFilterQuery = filterQuery.trim().length > 0;

    const rowActionContext: RowActionContext = useMemo(
        () => ({
            addingKeys,
            bulkAdding,
            foldersLoading: isLoadingFolders,
            foldersForContentType,
            onAdd: handleAdd,
            onPreview: handlePreview,
        }),
        [addingKeys, bulkAdding, isLoadingFolders, foldersForContentType, handleAdd, handlePreview],
    );

    const handleBulkAdded = useCallback((key: string, id: string) => {
        setAddedKeys((current) => new Set(current).add(key));
        setAddedItemIds((current) => new Map(current).set(key, id));
    }, []);

    const usesCardLayout = useCardLayout();

    const searchActions = (
        <div className="library-add-page__filter-actions">
            {providerCandidateSearch.isPending && (
                <Button variant="secondary" size="sm" onClick={handleCancelCandidateSearch} data-action="cancel-search">
                    Cancel search
                </Button>
            )}
            <Button
                variant="primary"
                size="sm"
                onClick={tab === 'live' ? handleApplyFilter : handleApplySavedFilter}
                loading={providerCandidateSearch.isPending}
                disabled={bulkAdding || (tab === 'live' ? !hasFilterQuery : !selectedSavedFilterId)}
                data-action={tab === 'live' ? 'apply-filter' : 'apply-saved-filter'}
            >
                <SparkleIcon weight="bold" aria-hidden="true" />
                Search
            </Button>
        </div>
    );

    return (
        <div className="library-add-page" data-page="library-add" data-layout={usesCardLayout ? 'card' : 'table'}>
            <PageHeader
                className="library-add-page__header"
                title="Add content"
                actions={<ContentViewToggle view={view} onChange={setView} />}
                description="Search for new content to add to the library"
                icon={<BinocularsIcon weight="duotone" aria-hidden="true" data-testid="library-add-page__icon" />}
            />

            <Tabs value={tab} onValueChange={(value) => switchTab(value as AddTab)}>
                <TabsList aria-label="Add mode">
                    <TabsTrigger disabled={bulkAdding} value="live" data-action="tab-live">
                        <PulseIcon weight="bold" aria-hidden="true" />
                        <span>Live filter</span>
                    </TabsTrigger>
                    <TabsTrigger disabled={bulkAdding} value="saved" data-action="tab-saved">
                        <FloppyDiskIcon weight="bold" aria-hidden="true" />
                        <span>Saved filter</span>
                    </TabsTrigger>
                </TabsList>
            </Tabs>

            {tab === 'live' && (
                <section aria-label="Live filter controls">
                    <FilterBar
                        value={filterQuery}
                        onChange={handleFilterQueryChange}
                        onSubmit={handleApplyFilter}
                        context="content"
                        loading={providerCandidateSearch.isPending}
                        readOnly={bulkAdding}
                        showSave={hasFilterQuery}
                        onSave={handleOpenSaveFilter}
                        isSaving={createFilterMutation.isPending}
                        collapsible={false}
                    >
                        {searchActions}
                    </FilterBar>
                </section>
            )}

            {tab === 'saved' && (
                <section className="library-add-page__saved-panel" aria-label="Saved filter controls">
                    <div className="library-add-page__saved-picker">
                        <div className="library-add-page__field library-add-page__field--saved-filter">
                            <Label htmlFor="library-add-page__saved-filter">Saved filter</Label>
                            <Select
                                value={selectedSavedFilterId || SAVED_FILTER_NONE_VALUE}
                                onValueChange={handleSavedFilterSelectChange}
                                disabled={bulkAdding || savedFilterOptions.length === 0}
                            >
                                <SelectTrigger
                                    id="library-add-page__saved-filter"
                                    size="sm"
                                    placeholder="Pick a saved filter..."
                                    data-field="saved-filter"
                                >
                                    {selectedSavedFilter?.name ?? 'Pick a saved filter...'}
                                </SelectTrigger>
                                <SelectContent>
                                    <SelectItem value={SAVED_FILTER_NONE_VALUE}>None</SelectItem>
                                    {savedFilterOptions.map((f) => (
                                        <SelectItem key={f.id} value={f.id}>
                                            {f.name}
                                        </SelectItem>
                                    ))}
                                </SelectContent>
                            </Select>
                        </div>
                        <div className="library-add-page__saved-actions">
                            {selectedSavedFilter && (
                                <Button asChild variant="secondary" size="sm">
                                    <Link
                                        to={`/filters?edit=${encodeURIComponent(selectedSavedFilter.id)}`}
                                        aria-label="Edit filter"
                                        data-action="open-filter-editor"
                                    >
                                        <PencilSimpleIcon weight="bold" aria-hidden="true" />
                                        Edit
                                    </Link>
                                </Button>
                            )}
                            {searchActions}
                        </div>
                    </div>
                    {selectedSavedFilter && (
                        <code className="library-add-page__saved-query" data-testid="library-add-page__saved-query">
                            {selectedSavedFilter.query}
                        </code>
                    )}
                    {savedFilterOptions.length === 0 && (
                        <p className="library-add-page__saved-hint" data-testid="library-add-page__saved-empty">
                            No saved filters yet. Build one on the Live filter tab and save it, or create one on the
                            Filters page.
                        </p>
                    )}
                </section>
            )}

            <section className="library-add-page__results" aria-label="Add results">
                {showInitialEmpty && (
                    <EmptyState
                        icon={<SparkleIcon weight="duotone" />}
                        title={tab === 'live' ? 'Search for new content' : 'Use a saved filter'}
                        size="sm"
                        data-testid="library-add-page__empty-initial"
                    />
                )}

                {showLoading &&
                    (view === 'gallery' ? (
                        <ContentGallerySkeleton data-testid="library-add-page__loading" />
                    ) : (
                        <div className="library-add-page__loading" data-testid="library-add-page__loading">
                            <Skeleton className="library-add-page__skeleton" />
                            <Skeleton className="library-add-page__skeleton" />
                            <Skeleton className="library-add-page__skeleton" />
                        </div>
                    ))}

                {showError && (
                    <EmptyState
                        icon={<WarningCircleIcon weight="duotone" />}
                        title="Search failed"
                        description={candidateSearchError ?? undefined}
                        onRetry={() => void runCandidateSearch(tab, urlState.query, page, urlState.continuation)}
                        size="sm"
                        data-testid="library-add-page__error"
                    />
                )}

                {showNoResults && (
                    <EmptyState
                        icon={<SparkleIcon weight="duotone" />}
                        title={canGoNext || page > 1 ? 'No matches on this page' : 'No matches'}
                        description={noResultsDescription(canGoNext, page, warnings.length > 0)}
                        action={
                            canCreateWishlistFromSearch ? (
                                <Button
                                    variant="secondary"
                                    size="sm"
                                    onClick={handleCreateWishlistFromFilter}
                                    data-action="create-wishlist-from-filter"
                                >
                                    <StarIcon weight="bold" aria-hidden="true" />
                                    Create a wishlist from this filter
                                </Button>
                            ) : undefined
                        }
                        size="sm"
                        data-testid="library-add-page__empty-no-results"
                    />
                )}

                {!showLoading && !showError && !showInitialEmpty && warnings.length > 0 && (
                    <ProviderSearchWarnings
                        className="library-add-page__warnings"
                        warnings={warnings}
                        hasResults={candidates.length > 0 || canGoNext}
                        data-testid="library-add-page__warnings"
                    />
                )}

                <AddContentResults
                    candidates={candidates}
                    context={rowActionContext}
                    view={view}
                    sorting={sorting}
                    sortOptions={ADD_CONTENT_SORT_OPTIONS}
                    onSortingChange={handleSortingChange}
                    searchKey={JSON.stringify([tab, urlState.query, searchVersion])}
                    page={page}
                    onAdded={handleBulkAdded}
                    onBusyChange={setBulkAdding}
                    loading={showLoading || showError || showInitialEmpty}
                />

                {hasResultsForPaging && (
                    <Pager
                        className="library-add-page__pagination"
                        aria-label="Result pages"
                        data-testid="library-add-page__pagination"
                        page={page}
                        disabled={bulkAdding}
                        canPrevious={canGoPrev}
                        canNext={canGoNext}
                        onFirst={() => goToPage(1)}
                        onPrevious={() => goToPage(page - 1)}
                        onNext={() => goToPage(page + 1)}
                        onLast={() => goToPage(page)}
                    />
                )}
            </section>

            <SaveFilterDialog
                open={saveFilterDialogOpen}
                onOpenChange={(open) => !open && closeSaveFilterDialog()}
                onSave={handleSaveFilter}
                isSaving={createFilterMutation.isPending}
                filterQuery={filterQuery}
                nameError={saveFilterError}
                queryError={saveFilterQueryError}
                existingNames={savedContentFilters.map((f) => f.name)}
            />

            <PreviewDrawer
                open={previewOpen}
                onOpenChange={setPreviewOpen}
                target={previewTarget}
                isAdded={previewCandidate ? addedKeys.has(previewCandidate.key) : false}
                isAdding={previewCandidate ? addingKeys.has(previewCandidate.key) : false}
                addDisabledReason={bulkAdding ? 'Adding selected content.' : previewAddDisabledReason}
                addOptions={previewAddOptions}
                onAdd={handleDrawerAdd}
            />
        </div>
    );
}

LibraryAddPage.displayName = 'LibraryAddPage';

export default LibraryAddPage;
