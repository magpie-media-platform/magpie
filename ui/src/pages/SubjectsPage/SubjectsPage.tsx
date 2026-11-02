import { useMemo, useCallback, useEffect } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { WarningCircleIcon } from '@phosphor-icons/react';
import { type PaginationState, type SortingState, type Updater } from '@tanstack/react-table';

import { Badge } from '@/components/ui/Badge';
import { CdnPhosphorIcon } from '@/components/ui/CdnPhosphorIcon';
import { ClearFiltersButton } from '@/components/ui/ClearFiltersButton';
import { EmptyState } from '@/components/ui/EmptyState';
import { MobileMediaCard } from '@/components/ui/MobileMediaCard';
import { MobileListToolbar, type MobileListSortOption } from '@/components/ui/MobileListToolbar';
import { PageHeader } from '@/components/ui/PageHeader';
import { Pager } from '@/components/ui/Pager';
import { AlphabetRail } from '@/components/ui/AlphabetRail';
import { SubjectCard } from '@/components/ui/SubjectCard';
import { Skeleton } from '@/components/ui/Skeleton';
import { ListSortControl } from '@/components/ui/ListSortControl';
import { ContentViewToggle } from '@/components/content/ContentGallery';

import { SubjectList, type SubjectItem } from '@/components/ui/SubjectList';
import { FilterBar } from '@/components/filters/FilterBar';
import { Slot } from '@/components/ui/Slot';
import { PullToRefresh } from '@/components/ui/PullToRefresh';
import { useDocumentTitle } from '@/hooks/useDocumentTitle';
import { useContentView } from '@/hooks/useContentView';
import { useUrlFilterState } from '@/hooks/useUrlFilterState';
import { useDebouncedValue } from '@/hooks/useDebouncedValue';
import { useLiveUrlFilter } from '@/hooks/useLiveUrlFilter';
import { useLibraryTypes, useTypeIconName, type LibraryTypeDefinition } from '@/hooks/useLibraryTypes';
import { TABLE_LAYOUT_QUERY, useCardLayout } from '@/hooks/ui/useCardLayout';
import { extractImageUrl, extractTitle } from '@/lib/metadata';
import { getErrorMessage } from '@/lib/errors';
import { readContentFilter, setContentFilter } from '@/lib/contentColumnFilters';
import { splitTopLevelClauses } from '@/lib/queryClauses';
import { useSubjects, type ApiSubject } from '@/hooks/useSubjects';
import { resolveSubjectKindLabel } from '@/types';

import './SubjectsPage.css';

const VALID_SORT_FIELDS = ['title', 'kind'] as const;

type SortField = (typeof VALID_SORT_FIELDS)[number];

const VALID_SORT_DIRECTIONS = ['asc', 'desc'] as const;

type SortDirection = (typeof VALID_SORT_DIRECTIONS)[number];

const UMBRELLA_LABEL = 'Subjects';
const SUBJECT_KIND_CLAUSE_PATTERN = /^kind\b/i;
const PAGE_SIZE = 50;
const SUBJECT_SORT_OPTIONS: MobileListSortOption[] = [
    { id: 'title', label: 'Title' },
    { id: 'kind', label: 'Type' },
];

function removeSubjectKindClauses(query: string): string {
    const trimmed = query.trim();
    const { clauses, hasTopLevelOr } = splitTopLevelClauses(trimmed);
    if (hasTopLevelOr) {
        return trimmed;
    }

    return clauses.filter((clause) => Boolean(clause) && !SUBJECT_KIND_CLAUSE_PATTERN.test(clause)).join(' and ');
}

function transformSubject(subject: ApiSubject, subjectTypes: LibraryTypeDefinition[]): SubjectItem {
    const subjectType = subjectTypes.find((type) => type.slug === subject.kind);
    const kindLabel = resolveSubjectKindLabel(subject.kind, subjectType?.name);
    const title = extractTitle(subject.metadataContainer) ?? `${kindLabel} ${subject.id.slice(0, 8)}`;
    const imageUrl = extractImageUrl(subject.metadataContainer);

    return {
        id: subject.id,
        title,
        imageUrl,
        kind: subject.kind,
        kindLabel,
        tags: undefined,
        createdAt: subject.createdAt,
    };
}

export function SubjectsPage() {
    const usesCardLayout = useCardLayout();
    const { view, setView } = useContentView('magpie-subjects-view');
    const { subjectType: subjectTypeSlug } = useParams<{ subjectType?: string }>();
    const navigate = useNavigate();

    const [urlState, setUrlState] = useUrlFilterState({
        filter: '',
        sort: 'title',
        sortDir: 'asc',
        page: '',
    });

    const pageNumber = useMemo(() => {
        const raw = Number(urlState.page);
        return Number.isInteger(raw) && raw >= 1 ? raw : 1;
    }, [urlState.page]);

    const pagination = useMemo<PaginationState>(
        () => ({ pageIndex: pageNumber - 1, pageSize: PAGE_SIZE }),
        [pageNumber],
    );

    const filterQuery = urlState.filter;
    const scopedSubjectKind = subjectTypeSlug && subjectTypeSlug !== 'all' ? subjectTypeSlug : null;
    const urlVisibleFilterQuery = scopedSubjectKind ? removeSubjectKindClauses(filterQuery) : filterQuery;
    const commitFilter = useCallback(
        (value: string, replace: boolean, state: { liveFilterCommit: string }) => {
            setUrlState({ filter: value, page: '' }, { replace, state });
        },
        [setUrlState],
    );
    const liveFilter = useLiveUrlFilter({ value: urlVisibleFilterQuery, onCommit: commitFilter });
    const visibleFilterQuery = liveFilter.value;
    const debouncedFilter = useDebouncedValue(visibleFilterQuery, 300);

    useEffect(() => {
        if (scopedSubjectKind && urlVisibleFilterQuery !== filterQuery) {
            setUrlState({ filter: urlVisibleFilterQuery }, { replace: true });
        }
    }, [scopedSubjectKind, urlVisibleFilterQuery, filterQuery, setUrlState]);

    const sortField: SortField = VALID_SORT_FIELDS.includes(urlState.sort as SortField)
        ? (urlState.sort as SortField)
        : 'title';
    const sortDirection: SortDirection = VALID_SORT_DIRECTIONS.includes(urlState.sortDir as SortDirection)
        ? (urlState.sortDir as SortDirection)
        : 'asc';

    const sorting: SortingState = useMemo(
        () => [{ id: sortField, desc: sortDirection === 'desc' }],
        [sortField, sortDirection],
    );

    const { subjectTypes } = useLibraryTypes();

    const serverFilter = useMemo(() => {
        const trimmed = debouncedFilter.trim();
        if (scopedSubjectKind) {
            const kindClause = `kind = "${scopedSubjectKind}"`;
            return trimmed ? `${kindClause} and (${trimmed})` : kindClause;
        }
        return trimmed;
    }, [scopedSubjectKind, debouncedFilter]);

    const queryParams = useMemo(
        () => ({
            ...(serverFilter ? { filter: serverFilter } : {}),
            sortBy: sortField,
            sortDirection: sortDirection === 'desc' ? ('Descending' as const) : ('Ascending' as const),
            pageNumber,
            pageSize: PAGE_SIZE,
        }),
        [serverFilter, sortField, sortDirection, pageNumber],
    );

    const { subjects, totalItems, isLoading, isPlaceholderData, error, refetch } = useSubjects(queryParams);
    const pageCount = Math.max(1, Math.ceil(totalItems / PAGE_SIZE));

    useEffect(() => {
        if (
            !isLoading &&
            !isPlaceholderData &&
            !error &&
            visibleFilterQuery === debouncedFilter &&
            pageNumber > pageCount
        ) {
            setUrlState({ page: pageCount > 1 ? String(pageCount) : '' }, { replace: true });
        }
    }, [isLoading, isPlaceholderData, error, visibleFilterQuery, debouncedFilter, pageNumber, pageCount, setUrlState]);

    const typeIconName = useTypeIconName();

    const currentSubjectType = useMemo(() => {
        if (!subjectTypeSlug || subjectTypeSlug === 'all') {
            return null;
        }
        return subjectTypes.find((type) => type.slug === subjectTypeSlug) ?? null;
    }, [subjectTypeSlug, subjectTypes]);

    const scoped = currentSubjectType !== null;
    const pageTitle = scoped ? currentSubjectType.pluralName : UMBRELLA_LABEL;
    const pageDescription = scoped
        ? `Browse and manage ${pageTitle.toLowerCase()}.`
        : 'Subjects that content can be related to.';
    const pageIconName = typeIconName(currentSubjectType?.slug, { subject: true });

    useDocumentTitle(pageTitle);

    const handleSubjectClick = useCallback(
        (item: SubjectItem) => {
            void navigate(`/subjects/${item.id}`);
        },
        [navigate],
    );

    const handleFilterChange = useCallback(
        (value: string) => {
            liveFilter.change(value);
        },
        [liveFilter],
    );

    const handlePaginationChange = useCallback(
        (updater: Updater<PaginationState>) => {
            const next = typeof updater === 'function' ? updater(pagination) : updater;
            setUrlState({ page: next.pageIndex > 0 ? String(next.pageIndex + 1) : '' });
        },
        [pagination, setUrlState],
    );

    const handleSortingChange = useCallback(
        (updater: SortingState | ((prev: SortingState) => SortingState)) => {
            const next = typeof updater === 'function' ? updater(sorting) : updater;
            const first = next[0];
            if (!first || !VALID_SORT_FIELDS.includes(first.id as SortField)) {
                setUrlState({ sort: '', sortDir: '', page: '' });
            } else {
                setUrlState({ sort: first.id, sortDir: first.desc ? 'desc' : 'asc', page: '' });
            }
        },
        [sorting, setUrlState],
    );

    const handleRefresh = useCallback(async () => {
        await refetch();
    }, [refetch]);

    const titlePrefix = readContentFilter(visibleFilterQuery, 'titlePrefix');
    const handleTitlePrefixChange = (prefix: string) => {
        liveFilter.change(setContentFilter(visibleFilterQuery, 'titlePrefix', prefix), true, true);
    };

    const items = useMemo(
        () => subjects.map((subject) => transformSubject(subject, subjectTypes)),
        [subjects, subjectTypes],
    );

    const hasActiveFilter = visibleFilterQuery.trim().length > 0;

    const pager = pageCount > 1 && (
        <Pager
            page={pageNumber}
            pageCount={pageCount}
            totalItems={totalItems}
            onFirst={() => handlePaginationChange({ ...pagination, pageIndex: 0 })}
            onPrevious={() => handlePaginationChange({ ...pagination, pageIndex: pageNumber - 2 })}
            onNext={() => handlePaginationChange({ ...pagination, pageIndex: pageNumber })}
            onLast={() => handlePaginationChange({ ...pagination, pageIndex: pageCount - 1 })}
        />
    );

    const subjectsEmptyState = isLoading ? (
        <div
            className="subjects-page__loading"
            data-testid="subjects-page__loading"
            role="status"
            aria-label={`Loading ${pageTitle.toLowerCase()}`}
        />
    ) : hasActiveFilter ? (
        <EmptyState
            title="No matches found"
            description={`No ${pageTitle.toLowerCase()} match the current filter.`}
            size="sm"
        />
    ) : (
        <EmptyState
            icon={<CdnPhosphorIcon name={pageIconName} weight="duotone" />}
            title={`No ${pageTitle.toLowerCase()} yet`}
            description={`${pageTitle} appear here once content links to them.`}
            size="sm"
        />
    );

    return (
        <div
            className="subjects-page"
            data-page="subjects"
            data-layout={view === 'gallery' ? 'grid' : usesCardLayout ? 'card' : 'table'}
        >
            <PullToRefresh onRefresh={handleRefresh} className="subjects-page__pull-to-refresh">
                <Slot slot="subject.list">
                    <PageHeader
                        className="subjects-page__header"
                        title={pageTitle}
                        description={pageDescription}
                        actions={
                            <ContentViewToggle
                                view={view}
                                onChange={setView}
                                galleryLabel="Grid"
                                label="Subject view"
                            />
                        }
                        icon={
                            <CdnPhosphorIcon
                                name={pageIconName}
                                weight="duotone"
                                aria-hidden="true"
                                data-testid="subjects-page__icon"
                            />
                        }
                    />

                    <section className="subjects-page__controls" aria-label="Filters and sorting">
                        <div className="subjects-page__filters">
                            <FilterBar
                                value={visibleFilterQuery}
                                onChange={handleFilterChange}
                                context="subject"
                                allowTitlePrefix
                                defaultExpanded={visibleFilterQuery.length > 0 ? true : undefined}
                                tableLayoutQuery={TABLE_LAYOUT_QUERY}
                                defaultSubjectKind={scopedSubjectKind ?? undefined}
                                lockSubjectKind={Boolean(scopedSubjectKind)}
                            />
                        </div>
                    </section>

                    {view === 'list' && titlePrefix && (
                        <ClearFiltersButton
                            label={`Clear first letter: ${titlePrefix.toUpperCase()}`}
                            onClick={() => handleTitlePrefixChange('')}
                        />
                    )}

                    {error && subjects.length === 0 ? (
                        <EmptyState
                            icon={<WarningCircleIcon weight="duotone" />}
                            title={`Failed to load ${pageTitle.toLowerCase()}`}
                            description={getErrorMessage(error)}
                            onRetry={refetch}
                        />
                    ) : (
                        <>
                            <div className="subjects-page__content">
                                {view === 'gallery' ? (
                                    <>
                                        <ListSortControl
                                            className="subjects-page__grid-sort"
                                            aria-label="Sort subjects"
                                            sorting={sorting}
                                            sortOptions={SUBJECT_SORT_OPTIONS}
                                            onSortingChange={handleSortingChange}
                                        />
                                        <div className="subjects-page__grid-body">
                                            <div className="subjects-page__grid-results">
                                                {isLoading ? (
                                                    <div
                                                        className="subject-grid"
                                                        role="status"
                                                        aria-label={`Loading ${pageTitle.toLowerCase()}`}
                                                    >
                                                        {Array.from({ length: 12 }, (_, index) => (
                                                            <div
                                                                key={index}
                                                                className="subject-card"
                                                                aria-hidden="true"
                                                            >
                                                                <Skeleton variant="circle" width={88} height={88} />
                                                                <Skeleton width="75%" />
                                                            </div>
                                                        ))}
                                                    </div>
                                                ) : items.length > 0 ? (
                                                    <ul
                                                        className="subject-grid"
                                                        role="list"
                                                        aria-label={`${pageTitle} grid`}
                                                    >
                                                        {items.map((item) => (
                                                            <li key={item.id} className="subject-grid__item">
                                                                <SubjectCard
                                                                    subjectId={item.id}
                                                                    name={item.title}
                                                                    imageUrl={item.imageUrl}
                                                                    qualifier={scoped ? undefined : item.kindLabel}
                                                                />
                                                            </li>
                                                        ))}
                                                    </ul>
                                                ) : (
                                                    subjectsEmptyState
                                                )}
                                            </div>
                                            <AlphabetRail
                                                titlePrefix={titlePrefix}
                                                onTitlePrefixChange={handleTitlePrefixChange}
                                            />
                                        </div>
                                        {pager}
                                    </>
                                ) : usesCardLayout ? (
                                    items.length > 0 ? (
                                        <>
                                            <MobileListToolbar
                                                className="subjects-page__mobile-toolbar"
                                                sorting={sorting}
                                                sortOptions={SUBJECT_SORT_OPTIONS}
                                                onSortingChange={handleSortingChange}
                                            />
                                            <div
                                                className="subjects-page__mobile-list"
                                                data-testid="subjects-page__mobile-list"
                                            >
                                                {items.map((item) => (
                                                    <MobileMediaCard
                                                        key={item.id}
                                                        to={`/subjects/${item.id}`}
                                                        data-subject-id={item.id}
                                                        posterUrl={item.imageUrl}
                                                        posterAlt={`${item.title} image`}
                                                        title={item.title}
                                                    >
                                                        <Badge variant="default" size="sm">
                                                            {item.kindLabel}
                                                        </Badge>
                                                    </MobileMediaCard>
                                                ))}
                                            </div>
                                            {pager}
                                        </>
                                    ) : (
                                        subjectsEmptyState
                                    )
                                ) : (
                                    <div className="subjects-page__table">
                                        <SubjectList
                                            items={items}
                                            onSubjectClick={handleSubjectClick}
                                            enableSorting
                                            manualSorting
                                            sorting={sorting}
                                            onSortingChange={handleSortingChange}
                                            enablePagination
                                            manualPagination
                                            pagination={pagination}
                                            onPaginationChange={handlePaginationChange}
                                            rowCount={totalItems}
                                            pageSize={PAGE_SIZE}
                                            loading={isLoading}
                                            emptyState={subjectsEmptyState}
                                            keepHeaderWhenEmpty={hasActiveFilter}
                                        />
                                    </div>
                                )}
                            </div>
                        </>
                    )}
                </Slot>
            </PullToRefresh>
        </div>
    );
}

SubjectsPage.displayName = 'SubjectsPage';

export default SubjectsPage;
