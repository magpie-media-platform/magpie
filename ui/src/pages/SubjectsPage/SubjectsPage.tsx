import { useMemo, useCallback, useEffect } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { WarningCircleIcon } from '@phosphor-icons/react';
import { type PaginationState, type SortingState, type Updater } from '@tanstack/react-table';

import { Badge } from '@/components/ui/Badge';
import { Button } from '@/components/ui/Button';
import { CdnPhosphorIcon } from '@/components/ui/CdnPhosphorIcon';
import { EmptyState } from '@/components/ui/EmptyState';
import { MobileMediaCard } from '@/components/ui/MobileMediaCard';
import { MobileListToolbar, type MobileListSortOption } from '@/components/ui/MobileListToolbar';
import { PageHeader } from '@/components/ui/PageHeader';
import { Pager } from '@/components/ui/Pager';

import { SubjectList, type SubjectItem } from '@/components/ui/SubjectList';
import { FilterBar } from '@/components/filters/FilterBar';
import { Slot } from '@/components/ui/Slot';
import { PullToRefresh } from '@/components/ui/PullToRefresh';
import { useDocumentTitle } from '@/hooks/useDocumentTitle';
import { useUrlFilterState } from '@/hooks/useUrlFilterState';
import { useDebouncedValue } from '@/hooks/useDebouncedValue';
import { useLiveUrlFilter } from '@/hooks/useLiveUrlFilter';
import { useLibraryTypes, useTypeIconName, type LibraryTypeDefinition } from '@/hooks/useLibraryTypes';
import { TABLE_LAYOUT_QUERY, useCardLayout } from '@/hooks/ui/useCardLayout';
import { extractImageUrl, extractTitle } from '@/lib/metadata';
import { getErrorMessage } from '@/lib/errors';
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

function splitTopLevelAndClauses(query: string): string[] {
    const trimmed = query.trim();
    if (!trimmed) {
        return [];
    }

    const clauses: string[] = [];
    let current = '';
    let depth = 0;
    let inString = false;
    let i = 0;

    while (i < trimmed.length) {
        const ch = trimmed[i];

        if (inString) {
            current += ch;
            if (ch === '\\' && i + 1 < trimmed.length) {
                current += trimmed[i + 1];
                i += 2;
                continue;
            }
            if (ch === '"') {
                inString = false;
            }
            i += 1;
            continue;
        }

        if (ch === '"') {
            inString = true;
            current += ch;
            i += 1;
            continue;
        }

        if (ch === '(') {
            depth += 1;
            current += ch;
            i += 1;
            continue;
        }

        if (ch === ')') {
            depth = Math.max(0, depth - 1);
            current += ch;
            i += 1;
            continue;
        }

        if (depth === 0 && /\s/.test(ch)) {
            const delimiter = trimmed.slice(i).match(/^\s+(and|or)\s+/i);
            if (delimiter) {
                if (delimiter[1].toLowerCase() === 'or') {
                    return [trimmed];
                }
                clauses.push(current);
                current = '';
                i += delimiter[0].length;
                continue;
            }
        }

        current += ch;
        i += 1;
    }

    clauses.push(current);
    return clauses.map((clause) => clause.trim()).filter(Boolean);
}

function removeSubjectKindClauses(query: string): string {
    const trimmed = query.trim();
    const clauses = splitTopLevelAndClauses(trimmed);
    if (clauses.length === 1 && clauses[0] === trimmed && /\s+or\s+/i.test(trimmed)) {
        return trimmed;
    }

    return clauses.filter((clause) => !SUBJECT_KIND_CLAUSE_PATTERN.test(clause.trim())).join(' and ');
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
    const liveFilter = useLiveUrlFilter({ value: urlVisibleFilterQuery, onCommit: commitFilter, replace: true });
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

    const { subjects, totalItems, isLoading, error, refetch } = useSubjects(queryParams);
    const pageCount = Math.max(1, Math.ceil(totalItems / PAGE_SIZE));

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
            setUrlState({ page: next.pageIndex > 0 ? String(next.pageIndex + 1) : '' }, { replace: true });
        },
        [pagination, setUrlState],
    );

    const handleSortingChange = useCallback(
        (updater: SortingState | ((prev: SortingState) => SortingState)) => {
            const next = typeof updater === 'function' ? updater(sorting) : updater;
            const first = next[0];
            if (!first || !VALID_SORT_FIELDS.includes(first.id as SortField)) {
                setUrlState({ sort: '', sortDir: '', page: '' }, { replace: true });
            } else {
                setUrlState({ sort: first.id, sortDir: first.desc ? 'desc' : 'asc', page: '' }, { replace: true });
            }
        },
        [sorting, setUrlState],
    );

    const handleRefresh = useCallback(async () => {
        await refetch();
    }, [refetch]);

    const items = useMemo(
        () => subjects.map((subject) => transformSubject(subject, subjectTypes)),
        [subjects, subjectTypes],
    );

    const hasActiveFilter = visibleFilterQuery.trim().length > 0;

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
        <div className="subjects-page" data-page="subjects" data-layout={usesCardLayout ? 'card' : 'table'}>
            <PullToRefresh onRefresh={handleRefresh} className="subjects-page__pull-to-refresh">
                <Slot slot="subject.list">
                    <PageHeader
                        className="subjects-page__header"
                        title={pageTitle}
                        description={pageDescription}
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
                                defaultExpanded={visibleFilterQuery.length > 0 ? true : undefined}
                                tableLayoutQuery={TABLE_LAYOUT_QUERY}
                                defaultSubjectKind={scopedSubjectKind ?? undefined}
                                lockSubjectKind={Boolean(scopedSubjectKind)}
                            />
                        </div>
                    </section>

                    {error && subjects.length === 0 ? (
                        <EmptyState
                            icon={<WarningCircleIcon weight="duotone" />}
                            title={`Failed to load ${pageTitle.toLowerCase()}`}
                            description={getErrorMessage(error)}
                            action={
                                <Button variant="secondary" size="sm" onClick={() => refetch()}>
                                    Retry
                                </Button>
                            }
                        />
                    ) : (
                        <>
                            <div className="subjects-page__content">
                                {usesCardLayout ? (
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
                                            {pageCount > 1 && (
                                                <Pager
                                                    page={pageNumber}
                                                    pageCount={pageCount}
                                                    totalItems={totalItems}
                                                    onFirst={() =>
                                                        handlePaginationChange({ ...pagination, pageIndex: 0 })
                                                    }
                                                    onPrevious={() =>
                                                        handlePaginationChange({
                                                            ...pagination,
                                                            pageIndex: pageNumber - 2,
                                                        })
                                                    }
                                                    onNext={() =>
                                                        handlePaginationChange({ ...pagination, pageIndex: pageNumber })
                                                    }
                                                    onLast={() =>
                                                        handlePaginationChange({
                                                            ...pagination,
                                                            pageIndex: pageCount - 1,
                                                        })
                                                    }
                                                />
                                            )}
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
