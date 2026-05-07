import { useMemo, useCallback } from 'react';
import { useNavigate, useParams, useSearchParams } from 'react-router-dom';
import { Cube, WarningCircle } from '@phosphor-icons/react';

import { Button } from '@/components/ui/Button';
import { EmptyState } from '@/components/ui/EmptyState';

import { SubjectList, type SubjectItem } from '@/components/ui/SubjectList';
import { ContentSearch } from '@/components/ui/ContentSearch';
import { SubjectFilters, type SubjectFilterState, type TagItem } from '@/components/ui/SubjectFilters';
import {
    SubjectSorting,
    type SubjectSortState,
    type SubjectSortField,
    type SubjectSortDirection,
} from '@/components/ui/SubjectSorting';
import { SubjectListSlot } from '@/components/ui/Slot/SubjectSlots';
import { PullToRefresh } from '@/components/ui/PullToRefresh';
import { useDocumentTitle } from '@/hooks/useDocumentTitle';
import { useLibraryTypes } from '@/hooks/useLibraryTypes';
import { useTags } from '@/hooks/useTags';
import { extractImageUrl, extractTitle } from '@/lib/metadata';
import { getErrorMessage } from '@/lib/errors';
import { useSubjects, type ApiSubject } from '@/hooks/useSubjects';

import './SubjectsPage.css';

const VALID_SORT_FIELDS: SubjectSortField[] = ['title', 'dateAdded'];

const VALID_SORT_DIRECTIONS: SubjectSortDirection[] = ['asc', 'desc'];

function transformSubject(subject: ApiSubject): SubjectItem {
    const title = extractTitle(subject.metadataContainer) ?? `Subject ${subject.id.slice(0, 8)}`;
    const imageUrl = extractImageUrl(subject.metadataContainer);

    return {
        id: subject.id,
        title,
        imageUrl,
        kind: subject.kind,
        tags: undefined,
        createdAt: subject.createdAt,
    };
}

export function SubjectsPage() {
    useDocumentTitle('Subjects');
    const { subjectType: subjectTypeSlug } = useParams<{ subjectType?: string }>();
    const [searchParams, setSearchParams] = useSearchParams();
    const navigate = useNavigate();

    const search = searchParams.get('search') ?? '';

    const kindParam = searchParams.get('kind') ?? '';
    const tagsParam = searchParams.get('tags') ?? '';
    const filters: SubjectFilterState = useMemo(
        () => ({
            kind: kindParam as SubjectFilterState['kind'],
            tags: tagsParam ? tagsParam.split(',') : [],
        }),
        [kindParam, tagsParam],
    );

    const sortFieldParam = searchParams.get('sort');
    const sortDirParam = searchParams.get('sortDir');
    const sort: SubjectSortState = useMemo(
        () => ({
            field:
                sortFieldParam && VALID_SORT_FIELDS.includes(sortFieldParam as SubjectSortField)
                    ? (sortFieldParam as SubjectSortField)
                    : 'title',
            direction:
                sortDirParam && VALID_SORT_DIRECTIONS.includes(sortDirParam as SubjectSortDirection)
                    ? (sortDirParam as SubjectSortDirection)
                    : 'asc',
        }),
        [sortFieldParam, sortDirParam],
    );

    const updateParams = useCallback(
        (updates: Partial<Record<'kind' | 'tags' | 'search' | 'sort' | 'sortDir', string>>) => {
            setSearchParams(
                (prev) => {
                    const next = new URLSearchParams(prev);
                    for (const [key, val] of Object.entries(updates)) {
                        const isDefault =
                            (key === 'sort' && (!val || val === 'title')) ||
                            (key === 'sortDir' && (!val || val === 'asc')) ||
                            (key === 'kind' && !val) ||
                            (key === 'tags' && !val) ||
                            (key === 'search' && !val);
                        if (isDefault) {
                            next.delete(key);
                        } else if (val) {
                            next.set(key, val);
                        }
                    }
                    return next;
                },
                { replace: true },
            );
        },
        [setSearchParams],
    );

    const { subjectTypes } = useLibraryTypes();

    const queryParams = useMemo(() => {
        const kind = subjectTypeSlug && subjectTypeSlug !== 'all' ? subjectTypeSlug : filters.kind || undefined;
        return { kind };
    }, [subjectTypeSlug, filters.kind]);

    const { subjects, isLoading, error, refetch } = useSubjects(queryParams);

    const { tags: apiTags } = useTags();

    const availableTags: TagItem[] = useMemo(() => {
        return apiTags.map((tag) => ({
            id: tag.name,
            name: tag.name,
        }));
    }, [apiTags]);

    const currentSubjectType = useMemo(() => {
        if (!subjectTypeSlug || subjectTypeSlug === 'all') {
            return null;
        }
        return subjectTypes.find((type) => type.slug === subjectTypeSlug) ?? null;
    }, [subjectTypeSlug, subjectTypes]);

    const pageTitle = currentSubjectType?.name ?? 'Subjects';
    const PageIcon = Cube;

    const handleSubjectClick = useCallback(
        (item: SubjectItem) => {
            navigate(`/subjects/${item.id}`);
        },
        [navigate],
    );

    const handleFiltersChange = useCallback(
        (newFilters: SubjectFilterState) => {
            updateParams({
                kind: newFilters.kind,
                tags: newFilters.tags.length > 0 ? newFilters.tags.join(',') : '',
            });
        },
        [updateParams],
    );

    const handleSortChange = useCallback(
        (newSort: SubjectSortState) => {
            updateParams({
                sort: newSort.field,
                sortDir: newSort.direction,
            });
        },
        [updateParams],
    );

    const handleRefresh = useCallback(async () => {
        await refetch();
    }, [refetch]);

    const filteredAndSortedItems = useMemo(() => {
        const items = subjects.map(transformSubject);

        const searchQuery = search.trim().toLowerCase();

        const filtered = items.filter((item) => {
            if (searchQuery && !item.title.toLowerCase().includes(searchQuery)) {
                return false;
            }

            if (filters.tags.length > 0) {
                const itemTags = item.tags ?? [];
                const hasMatchingTag = filters.tags.some((tagId) => itemTags.includes(tagId));
                if (!hasMatchingTag) {
                    return false;
                }
            }

            return true;
        });

        const sorted = [...filtered].sort((a, b) => {
            let comparison = 0;

            switch (sort.field) {
                case 'title':
                    comparison = a.title.localeCompare(b.title);
                    break;
                case 'dateAdded': {
                    const dateA = a.createdAt ? new Date(a.createdAt).getTime() : 0;
                    const dateB = b.createdAt ? new Date(b.createdAt).getTime() : 0;
                    comparison = dateA - dateB;
                    break;
                }
            }

            return sort.direction === 'asc' ? comparison : -comparison;
        });

        return sorted;
    }, [subjects, filters, sort, search]);

    return (
        <div className="subjects-page" data-page="subjects">
            <PullToRefresh onRefresh={handleRefresh} className="subjects-page__pull-to-refresh">
                <SubjectListSlot>
                    <header className="subjects-page__header">
                        <div className="subjects-page__header-left">
                            <PageIcon
                                weight="duotone"
                                className="subjects-page__icon"
                                aria-hidden="true"
                                data-testid="subjects-page__icon"
                            />
                            <h1 className="subjects-page__title">{pageTitle}</h1>
                        </div>
                        <div className="subjects-page__header-right">
                            <ContentSearch
                                value={search}
                                onChange={(val) => updateParams({ search: val })}
                                placeholder="Search by title..."
                            />
                        </div>
                    </header>

                    {error && subjects.length === 0 ? (
                        <EmptyState
                            icon={<WarningCircle weight="duotone" />}
                            title="Failed to load subjects"
                            description={getErrorMessage(error)}
                            action={
                                <Button variant="secondary" size="sm" onClick={() => refetch()}>
                                    Retry
                                </Button>
                            }
                        />
                    ) : (
                        <>
                            <div className="subjects-page__controls">
                                <div className="subjects-page__filters">
                                    <SubjectFilters
                                        filters={filters}
                                        availableTags={availableTags}
                                        onChange={handleFiltersChange}
                                        loading={isLoading}
                                    />
                                </div>

                                <div className="subjects-page__sorting">
                                    <SubjectSorting sort={sort} onChange={handleSortChange} />
                                </div>
                            </div>

                            <div className="subjects-page__content">
                                <SubjectList
                                    items={filteredAndSortedItems}
                                    onSubjectClick={handleSubjectClick}
                                    enableSorting={false}
                                    enablePagination={false}
                                    loading={isLoading}
                                />
                            </div>
                        </>
                    )}
                </SubjectListSlot>
            </PullToRefresh>
        </div>
    );
}

SubjectsPage.displayName = 'SubjectsPage';

export default SubjectsPage;
