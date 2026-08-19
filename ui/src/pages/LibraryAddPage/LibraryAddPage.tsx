import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { useLocation, useNavigate } from 'react-router-dom';
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
import { DataTable } from '@/components/ui/DataTable';
import { EmptyState } from '@/components/ui/EmptyState';
import { FilterBar } from '@/components/filters/FilterBar';
import { Label } from '@/components/ui/Label';
import { MobileMediaCard } from '@/components/ui/MobileMediaCard';
import { MobileListToolbar, type MobileListSortOption } from '@/components/ui/MobileListToolbar';
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
import { useSavedProviderMetadataCandidateSearch } from '@/hooks/useSavedProviderMetadataCandidateSearch';
import {
    useProviderMetadataCandidateSearch,
    type ProviderMetadataCandidate,
} from '@/hooks/useProviderMetadataCandidateSearch';
import { useDocumentTitle } from '@/hooks/useDocumentTitle';
import { getRootLevels, useLibraryTypes } from '@/hooks/useLibraryTypes';
import {
    useContentFilters,
    useCreateContentFilter,
    type ContentFilterCriteria,
    type ContentFilterItem,
} from '@/hooks/useFilters';
import { useRootFolders, type ApiRootFolder } from '@/hooks/useRootFolders';
import { useToggle } from '@/hooks/useToggle';
import { useCardLayout } from '@/hooks/ui/useCardLayout';
import { isApiError } from '@/lib/api';
import { setContentFilterSelection } from '@/lib/contentColumnFilters';
import { parseMagpieQl } from '@/lib/magpieQl';
import type { LibraryAddPreloadState, WishlistsPageCreateState } from '@/types/navigation';

import {
    buildAddableColumns,
    formatPublished,
    RowActions,
    SourceCell,
    TitleHeading,
    TypeCell,
    type AddableCandidate,
    type RowActionContext,
} from './buildAddableColumns';
import './LibraryAddPage.css';

const SAVED_FILTER_NONE_VALUE = '__none__';

const DEFAULT_SORTING: SortingState = [];

function candidateSearchErrorMessage(error: unknown): string {
    if (isApiError(error) && typeof error.details === 'object' && error.details !== null) {
        const details = error.details as { message?: unknown; position?: unknown };
        if (typeof details.message === 'string' && typeof details.position === 'number') {
            return details.message;
        }
    }
    return 'Search failed. Please try again.';
}

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
        overview: candidate.overview,
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

export type { LibraryAddPreloadState } from '@/types/navigation';

export function LibraryAddPage() {
    useDocumentTitle('Add content');
    const navigate = useNavigate();
    const location = useLocation();
    const { toast } = useToast();

    const { folders, isLoading: isLoadingFolders } = useRootFolders();
    const { contentTypes } = useLibraryTypes();
    const [tab, setTab] = useState<AddTab>('live');
    const [page, setPage] = useState<number>(1);
    const [addingKey, setAddingKey] = useState<string | null>(null);
    const [addedKeys, setAddedKeys] = useState<Set<string>>(() => new Set());
    const [addedItemIds, setAddedItemIds] = useState<Map<string, string>>(() => new Map());
    const [previewCandidate, setPreviewCandidate] = useState<AddableCandidate | null>(null);
    const [previewOpen, setPreviewOpen] = useState(false);

    const [sorting, setSorting] = useState<SortingState>(DEFAULT_SORTING);

    const [filterQuery, setFilterQuery] = useState<string>('');
    const [saveFilterDialogOpen, , openSaveFilterDialog, closeSaveFilterDialog] = useToggle();
    const [saveFilterError, setSaveFilterError] = useState<string | null>(null);
    const [candidateSearchError, setCandidateSearchError] = useState<string | null>(null);

    const candidateSearch = useProviderMetadataCandidateSearch();
    const savedCandidateSearch = useSavedProviderMetadataCandidateSearch();

    const addMutation = useAddContentByExternalId();

    const { filters: savedContentFilters, isLoading: savedFiltersLoading } = useContentFilters();
    const savedFilterOptions = useMemo(
        () => savedContentFilters.filter((f) => f.query).map((f) => ({ id: f.id, name: f.name, query: f.query })),
        [savedContentFilters],
    );
    const [selectedSavedFilterId, setSelectedSavedFilterId] = useState<string>('');
    const createFilterMutation = useCreateContentFilter();

    const lastCandidateSearchRef = useRef<{ kind: 'adhoc'; query: string } | { kind: 'saved'; query: string } | null>(
        null,
    );

    const handleFilterQueryChange = useCallback((value: string) => {
        setFilterQuery(value);
        setPage(1);
    }, []);

    const foldersForContentType = useCallback(
        (contentType: string): ApiRootFolder[] => {
            if (!contentType) {
                return [];
            }
            return folders.filter((f) => f.contentType === contentType);
        },
        [folders],
    );

    const handleAddSuccess = useCallback(
        (candidateKey: string, title: string, contentHierarchyItemId: string) => {
            setAddedKeys((current) => new Set(current).add(candidateKey));
            setAddedItemIds((current) => new Map(current).set(candidateKey, contentHierarchyItemId));
            toast({
                title: 'Added to library',
                description: `Magpie added "${title}" to the library. Magpie queued a metadata refresh.`,
                variant: 'success',
            });
            setPreviewOpen(false);
        },
        [toast],
    );

    const handleAdd = useCallback(
        (candidate: AddableCandidate, rootFolderId: string) => {
            if (!candidate.contentType || !rootFolderId) {
                return;
            }

            setAddingKey(candidate.key);

            addMutation.mutate(
                {
                    providerType: candidate.providerType,
                    externalIdKey: candidate.externalIdSource,
                    externalId: candidate.externalId,
                    contentType: candidate.contentType,
                    ...(candidate.levelSlug ? { levelSlug: candidate.levelSlug } : {}),
                    rootFolderId,
                },
                {
                    onSuccess: (contentHierarchyItemId) =>
                        handleAddSuccess(candidate.key, candidate.title, contentHierarchyItemId),
                    onError: () => {
                        toast({
                            title: 'Failed to add content',
                            description: 'The server rejected the request. Please try again.',
                            variant: 'error',
                        });
                    },
                    onSettled: () => {
                        setAddingKey((current) => (current === candidate.key ? null : current));
                    },
                },
            );
        },
        [addMutation, handleAddSuccess, toast],
    );

    const handlePreview = useCallback((candidate: AddableCandidate) => {
        if (candidate.inLibrary) {
            return;
        }
        setPreviewCandidate(candidate);
        setPreviewOpen(true);
    }, []);

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
        (target: PreviewDrawerTarget, rootFolderId?: string) => {
            if (!previewCandidate || !previewCandidate.contentType || !rootFolderId) {
                return;
            }
            const candidateKey = previewCandidate.key;

            setAddingKey(candidateKey);

            addMutation.mutate(
                {
                    providerType: target.providerType,
                    externalIdKey: target.externalIdKey,
                    externalId: target.externalId,
                    contentType: target.contentType,
                    ...(target.levelSlug ? { levelSlug: target.levelSlug } : {}),
                    rootFolderId,
                },
                {
                    onSuccess: (contentHierarchyItemId) =>
                        handleAddSuccess(candidateKey, target.fallbackTitle, contentHierarchyItemId),
                    onError: () => {
                        toast({
                            title: 'Failed to add content',
                            description: 'The server rejected the request. Please try again.',
                            variant: 'error',
                        });
                    },
                    onSettled: () => {
                        setAddingKey((current) => (current === candidateKey ? null : current));
                    },
                },
            );
        },
        [addMutation, handleAddSuccess, previewCandidate, toast],
    );

    const runCandidateSearch = useCallback(
        async (queryText: string, candidateSearchPage: number) => {
            setCandidateSearchError(null);
            lastCandidateSearchRef.current = { kind: 'adhoc', query: queryText };

            try {
                await candidateSearch.mutateAsync({
                    query: queryText,
                    page: candidateSearchPage,
                });
            } catch (error) {
                setCandidateSearchError(candidateSearchErrorMessage(error));
            }
        },
        [candidateSearch],
    );

    const handleApplyFilter = useCallback(() => {
        if (!filterQuery.trim()) {
            return;
        }
        setPage(1);
        return runCandidateSearch(filterQuery, 1);
    }, [runCandidateSearch, filterQuery]);

    const runSavedCandidateSearch = useCallback(
        async (queryText: string, candidateSearchPage: number) => {
            if (!queryText.trim()) {
                return;
            }
            setCandidateSearchError(null);
            lastCandidateSearchRef.current = { kind: 'saved', query: queryText };

            try {
                await savedCandidateSearch.mutateAsync({
                    query: queryText,
                    page: candidateSearchPage,
                });
            } catch (error) {
                setCandidateSearchError(candidateSearchErrorMessage(error));
            }
        },
        [savedCandidateSearch],
    );

    const runCandidateSearchForSavedFilter = useCallback(
        (filter: ContentFilterItem, candidateSearchPage: number) => {
            return runSavedCandidateSearch(filter.query, candidateSearchPage);
        },
        [runSavedCandidateSearch],
    );

    const handleSelectSavedFilter = useCallback((savedFilterId: string) => {
        setSelectedSavedFilterId(savedFilterId);
        setPage(1);
    }, []);

    const handleApplySavedFilter = useCallback(() => {
        const filter = savedContentFilters.find((f) => f.id === selectedSavedFilterId);
        if (!filter) {
            return;
        }
        setPage(1);
        return runCandidateSearchForSavedFilter(filter, 1);
    }, [savedContentFilters, selectedSavedFilterId, runCandidateSearchForSavedFilter]);

    const handleSavedFilterSelectChange = useCallback(
        (value: string) => {
            if (value === SAVED_FILTER_NONE_VALUE) {
                setSelectedSavedFilterId('');
                return;
            }
            handleSelectSavedFilter(value);
        },
        [handleSelectSavedFilter],
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

    const handleOpenSavedFilterEditor = useCallback(() => {
        if (!selectedSavedFilterId) {
            return;
        }
        void navigate(`/filters?edit=${encodeURIComponent(selectedSavedFilterId)}`);
    }, [navigate, selectedSavedFilterId]);

    const preload = location.state as LibraryAddPreloadState | null;
    const preloadAppliedRef = useRef(false);
    useEffect(() => {
        if (preloadAppliedRef.current || !preload?.filterQuery) {
            return;
        }
        if (preload.savedFilterId && savedFiltersLoading) {
            return;
        }

        const preloadFilter = preload.savedFilterId
            ? savedContentFilters.find((f) => f.id === preload.savedFilterId)
            : undefined;

        if (preloadFilter) {
            setTab('saved');
            setSelectedSavedFilterId(preloadFilter.id);
            setPage(1);

            if (!preloadFilter.query.trim()) {
                return;
            }

            preloadAppliedRef.current = true;
            void runCandidateSearchForSavedFilter(preloadFilter, 1);
        } else {
            preloadAppliedRef.current = true;
            setPage(1);
            setTab('live');
            setFilterQuery(preload.filterQuery);
            void runCandidateSearch(preload.filterQuery, 1);
        }
    }, [preload, savedContentFilters, savedFiltersLoading, runCandidateSearch, runCandidateSearchForSavedFilter]);

    useEffect(() => {
        if (preloadAppliedRef.current) {
            return;
        }
        const contentTypeParam = new URLSearchParams(location.search).get('contentType')?.trim();
        if (!contentTypeParam) {
            return;
        }
        preloadAppliedRef.current = true;

        const seededQuery = setContentFilterSelection('', { contentType: contentTypeParam });
        setTab('live');
        setFilterQuery(seededQuery);
        setPage(1);
    }, [location.search]);

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
                await createFilterMutation.mutateAsync({
                    name,
                    criteria: parseResult.criteria as {
                        contentHierarchyItem?: { and?: unknown[] };
                        mediaFiles?: { and?: unknown[] };
                    },
                    isShared: false,
                });

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
        [closeSaveFilterDialog, createFilterMutation, filterQuery, toast],
    );

    const activeSearchKind = lastCandidateSearchRef.current?.kind ?? (tab === 'saved' ? 'saved' : 'adhoc');

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
                      isPending: candidateSearch.isPending,
                      isError: candidateSearch.isError,
                      isSuccess: candidateSearch.isSuccess,
                      data: candidateSearch.data,
                  },
        [
            activeSearchKind,
            candidateSearch.isPending,
            candidateSearch.isError,
            candidateSearch.isSuccess,
            candidateSearch.data,
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

    const ownedCount = candidates.filter((candidate) => candidate.inLibrary).length;

    const warnings = providerCandidateSearch.data?.warnings ?? [];

    const hasRunCandidateSearch =
        providerCandidateSearch.isSuccess || providerCandidateSearch.isError || providerCandidateSearch.isPending;

    const canGoNext = providerCandidateSearch.data?.pageInfo.hasMore ?? false;

    const showInitialEmpty = !hasRunCandidateSearch && !candidateSearchError;
    const showLoading = providerCandidateSearch.isPending;
    const showError = (!!candidateSearchError || providerCandidateSearch.isError) && !showLoading;
    const showNoResults =
        !showLoading &&
        !showError &&
        !showInitialEmpty &&
        candidates.length === 0 &&
        !canGoNext &&
        providerCandidateSearch.isSuccess;

    const hasResultsForPaging =
        !showLoading && !showError && !showInitialEmpty && !showNoResults && (candidates.length > 0 || canGoNext);

    const canCreateWishlistFromSearch = tab === 'saved' ? !!selectedSavedFilterId : filterQuery.trim().length > 0;
    const canGoPrev = page > 1;

    const goToPage = useCallback(
        (nextPage: number) => {
            const target = Math.max(1, nextPage);
            setPage(target);
            const last = lastCandidateSearchRef.current;
            if (last?.kind === 'adhoc') {
                void runCandidateSearch(last.query, target);
            } else if (last?.kind === 'saved') {
                void runSavedCandidateSearch(last.query, target);
            }
        },
        [runCandidateSearch, runSavedCandidateSearch],
    );

    const handleSortingChange = useCallback(
        (updater: SortingState | ((previous: SortingState) => SortingState)) => {
            const next = typeof updater === 'function' ? updater(sorting) : updater;
            setSorting(next);
            if (page !== 1) {
                goToPage(1);
            }
        },
        [goToPage, page, sorting],
    );

    const switchTab = useCallback(
        (next: AddTab) => {
            setTab(next);
            setPage(1);
            setCandidateSearchError(null);
            lastCandidateSearchRef.current = null;
            candidateSearch.reset();
            savedCandidateSearch.reset();
        },
        [candidateSearch, savedCandidateSearch],
    );

    const hasFilterQuery = filterQuery.trim().length > 0;

    const rowActionContext: RowActionContext = useMemo(
        () => ({
            addingKey,
            addedKeys,
            foldersLoading: isLoadingFolders,
            foldersForContentType,
            onAdd: handleAdd,
            onPreview: handlePreview,
        }),
        [addingKey, addedKeys, isLoadingFolders, foldersForContentType, handleAdd, handlePreview],
    );

    const getRowId = useCallback((candidate: AddableCandidate) => candidate.key, []);

    const columns = useMemo(() => buildAddableColumns({ rowActionContext }), [rowActionContext]);

    const sortedCandidates = useMemo(() => {
        const sort = sorting[0];
        if (!sort) {
            return candidates;
        }
        const dir = sort.desc ? -1 : 1;
        const byTitle = (a: AddableCandidate, b: AddableCandidate) => a.title.localeCompare(b.title) * dir;
        if (sort.id === 'title') {
            return [...candidates].sort(byTitle);
        }
        if (sort.id === 'published') {
            return [...candidates].sort((a, b) => {
                if (a.yearValue == null && b.yearValue == null) {
                    return a.title.localeCompare(b.title);
                }
                if (a.yearValue == null) {
                    return 1;
                }
                if (b.yearValue == null) {
                    return -1;
                }
                if (a.yearValue !== b.yearValue) {
                    return (a.yearValue - b.yearValue) * dir;
                }
                return a.title.localeCompare(b.title);
            });
        }
        if (sort.id === 'type') {
            return [...candidates].sort((a, b) => {
                const left = a.levelSlug ?? a.contentType;
                const right = b.levelSlug ?? b.contentType;
                const comparison = left.localeCompare(right);
                return comparison === 0 ? a.title.localeCompare(b.title) : comparison * dir;
            });
        }
        return candidates;
    }, [candidates, sorting]);

    const usesCardLayout = useCardLayout();

    return (
        <div className="library-add-page" data-page="library-add" data-layout={usesCardLayout ? 'card' : 'table'}>
            <PageHeader
                className="library-add-page__header"
                title="Add content"
                description="Search external metadata providers for new content, or use a saved filter."
                icon={<BinocularsIcon weight="duotone" aria-hidden="true" data-testid="library-add-page__icon" />}
            />

            <Tabs value={tab} onValueChange={(value) => switchTab(value as AddTab)}>
                <TabsList aria-label="Add mode">
                    <TabsTrigger value="live" data-action="tab-live">
                        <PulseIcon weight="bold" aria-hidden="true" />
                        <span>Live filter</span>
                    </TabsTrigger>
                    <TabsTrigger value="saved" data-action="tab-saved">
                        <FloppyDiskIcon weight="bold" aria-hidden="true" />
                        <span>Saved filter</span>
                    </TabsTrigger>
                </TabsList>
            </Tabs>

            {tab === 'live' && (
                <section className="library-add-page__filter" aria-label="Live filter controls">
                    <FilterBar
                        value={filterQuery}
                        onChange={handleFilterQueryChange}
                        onSubmit={handleApplyFilter}
                        context="content"
                        loading={providerCandidateSearch.isPending}
                        showSave={hasFilterQuery}
                        onSave={handleOpenSaveFilter}
                        isSaving={createFilterMutation.isPending}
                        collapsible={false}
                    />
                    <div className="library-add-page__filter-actions">
                        {!hasFilterQuery && (
                            <p className="library-add-page__filter-hint" data-testid="library-add-page__filter-empty">
                                Add a filter criterion to search.
                            </p>
                        )}
                        <Button
                            variant="primary"
                            size="sm"
                            onClick={handleApplyFilter}
                            loading={providerCandidateSearch.isPending}
                            disabled={!hasFilterQuery}
                            data-action="apply-filter"
                        >
                            <SparkleIcon weight="bold" aria-hidden="true" />
                            Search
                        </Button>
                    </div>
                </section>
            )}

            {tab === 'saved' && (
                <section className="library-add-page__filter" aria-label="Saved filter controls">
                    <div className="library-add-page__field library-add-page__field--saved-filter">
                        <Label htmlFor="library-add-page__saved-filter">Saved filter</Label>
                        <Select
                            value={selectedSavedFilterId || SAVED_FILTER_NONE_VALUE}
                            onValueChange={handleSavedFilterSelectChange}
                            disabled={savedFilterOptions.length === 0}
                        >
                            <SelectTrigger
                                id="library-add-page__saved-filter"
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
                    {selectedSavedFilter && (
                        <div className="library-add-page__saved-summary">
                            <code
                                className="library-add-page__saved-query"
                                title={selectedSavedFilter.query}
                                data-testid="library-add-page__saved-query"
                            >
                                {selectedSavedFilter.query}
                            </code>
                            <Button
                                variant="secondary"
                                size="sm"
                                onClick={handleOpenSavedFilterEditor}
                                data-action="open-filter-editor"
                            >
                                <PencilSimpleIcon weight="bold" aria-hidden="true" />
                                Edit on Filters page
                            </Button>
                        </div>
                    )}
                    {savedFilterOptions.length === 0 && (
                        <p className="library-add-page__saved-hint" data-testid="library-add-page__saved-empty">
                            No saved filters yet. Build one on the Live filter tab and save it, or create one on the
                            Filters page.
                        </p>
                    )}
                    <div className="library-add-page__filter-actions">
                        <Button
                            variant="primary"
                            size="sm"
                            onClick={handleApplySavedFilter}
                            loading={providerCandidateSearch.isPending}
                            disabled={!selectedSavedFilterId}
                            data-action="apply-saved-filter"
                        >
                            <SparkleIcon weight="bold" aria-hidden="true" />
                            Search
                        </Button>
                    </div>
                </section>
            )}

            <section className="library-add-page__results" aria-label="Add results">
                {showInitialEmpty && (
                    <EmptyState
                        icon={<SparkleIcon weight="duotone" />}
                        title={tab === 'live' ? 'Search for new content' : 'Use a saved filter'}
                        description={
                            tab === 'live'
                                ? 'New content can be found by searching external metadata providers.'
                                : 'A saved filter can be used to search for content.'
                        }
                        size="sm"
                        data-testid="library-add-page__empty-initial"
                    />
                )}

                {showLoading && (
                    <div className="library-add-page__loading" data-testid="library-add-page__loading">
                        <Skeleton className="library-add-page__skeleton" />
                        <Skeleton className="library-add-page__skeleton" />
                        <Skeleton className="library-add-page__skeleton" />
                    </div>
                )}

                {showError && (
                    <EmptyState
                        icon={<WarningCircleIcon weight="duotone" />}
                        title="Search failed"
                        description={candidateSearchError ?? undefined}
                        action={
                            <Button variant="secondary" size="sm" onClick={() => goToPage(page)}>
                                Retry
                            </Button>
                        }
                        size="sm"
                        data-testid="library-add-page__error"
                    />
                )}

                {showNoResults && (
                    <EmptyState
                        icon={<SparkleIcon weight="duotone" />}
                        title="No matches"
                        description={
                            warnings.length > 0
                                ? 'No usable matches were returned. Review the provider warnings and adjust the filter.'
                                : 'No providers returned content matching this filter. Try broader criteria, or save it as a wishlist to keep checking for new matches.'
                        }
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
                        hasResults={candidates.length > 0}
                        data-testid="library-add-page__warnings"
                    />
                )}

                {!showLoading && !showError && !showInitialEmpty && ownedCount > 0 && (
                    <p
                        className="library-add-page__in-library-notice"
                        role="status"
                        data-testid="library-add-page__in-library-notice"
                    >
                        {ownedCount} {ownedCount === 1 ? 'match' : 'matches'} on this page{' '}
                        {ownedCount === 1 ? 'is' : 'are'} already in your library. Open the existing{' '}
                        {ownedCount === 1 ? 'item' : 'items'} below.
                    </p>
                )}

                {!showLoading &&
                    !showError &&
                    !showInitialEmpty &&
                    candidates.length > 0 &&
                    (usesCardLayout ? (
                        <>
                            <MobileListToolbar
                                className="library-add-page__mobile-toolbar"
                                sorting={sorting}
                                sortOptions={ADD_CONTENT_SORT_OPTIONS}
                                onSortingChange={handleSortingChange}
                            />
                            <div className="library-add-page__mobile-list" data-testid="library-add-page__mobile-list">
                                {sortedCandidates.map((candidate) => (
                                    <MobileMediaCard
                                        key={candidate.key}
                                        data-result-key={candidate.key}
                                        data-provider={candidate.providerType}
                                        data-source={candidate.externalIdSource}
                                        data-external-id={candidate.externalId}
                                        data-in-library={candidate.inLibrary ? 'true' : undefined}
                                        posterUrl={candidate.thumbnailUrl ?? undefined}
                                        posterAlt=""
                                        title={
                                            <TitleHeading
                                                candidate={candidate}
                                                onPreview={rowActionContext.onPreview}
                                            />
                                        }
                                        description={candidate.overview ?? undefined}
                                        trailing={
                                            <div className="library-add-page__card-actions">
                                                <RowActions candidate={candidate} ctx={rowActionContext} />
                                            </div>
                                        }
                                    >
                                        <TypeCell candidate={candidate} />
                                        <span className="library-add-page__card-published">
                                            Published: {formatPublished(candidate.releaseDate)}
                                        </span>
                                        <SourceCell candidate={candidate} />
                                    </MobileMediaCard>
                                ))}
                            </div>
                        </>
                    ) : (
                        <div className="library-add-page__table" data-testid="library-add-page__result-list">
                            <DataTable<AddableCandidate>
                                columns={columns}
                                data={candidates}
                                size="sm"
                                enableSorting
                                sorting={sorting}
                                onSortingChange={handleSortingChange}
                                getRowId={getRowId}
                            />
                        </div>
                    ))}

                {hasResultsForPaging && (
                    <Pager
                        className="library-add-page__pagination"
                        aria-label="Result pages"
                        data-testid="library-add-page__pagination"
                        page={page}
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
                error={saveFilterError}
                existingNames={savedContentFilters.map((f) => f.name)}
            />

            <PreviewDrawer
                open={previewOpen}
                onOpenChange={setPreviewOpen}
                target={previewTarget}
                isAdded={previewCandidate ? addedKeys.has(previewCandidate.key) : false}
                isAdding={previewCandidate ? addingKey === previewCandidate.key : false}
                addDisabledReason={previewAddDisabledReason}
                addOptions={previewAddOptions}
                onAdd={handleDrawerAdd}
            />
        </div>
    );
}

LibraryAddPage.displayName = 'LibraryAddPage';

export default LibraryAddPage;
