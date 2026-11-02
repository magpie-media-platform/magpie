import { useCallback, useEffect, useMemo } from 'react';
import { type OnChangeFn, type PaginationState, type SortingState } from '@tanstack/react-table';

import { type ContentHeaderFilterHandlers, type ContentHeaderFilters } from '@/components/ui/ContentList';
import { useContentList } from '@/hooks/useContent';
import { useDebouncedValue } from '@/hooks/useDebouncedValue';
import { useLibraryTypes } from '@/hooks/useLibraryTypes';
import { useUrlTableState } from '@/hooks/useUrlTableState';
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
import { escapeQueryString } from '@/lib/simpleFilterQuery';
import { type ContentListItem, type ContentQueryParams } from '@/types';

const SUBJECT_CONTENT_PAGE_SIZE = 25;

const SUBJECT_CONTENT_SORT_FIELD: Record<string, string> = {
    title: 'Name',
    publishDate: 'PublishDate',
};

const DEFAULT_SORTING: SortingState = [{ id: 'publishDate', desc: true }];

export interface SubjectContentController {
    contentItems: ContentListItem[];
    totalItems: number;
    isLoading: boolean;
    error: unknown;
    sorting: SortingState;
    onSortingChange: OnChangeFn<SortingState>;
    pagination: PaginationState;
    onPaginationChange: OnChangeFn<PaginationState>;
    headerFilters: ContentHeaderFilters;
    headerFilterHandlers: ContentHeaderFilterHandlers;
    onFilterCommit: (values: Pick<ContentHeaderFilters, 'title' | 'contentType' | 'fulfillment'>) => void;
    pageCount: number;
    currentPage: number;
    goToPage: (page: number) => void;
    hasActiveFilters: boolean;
}

export function useSubjectContent(metadataContainerId: string | undefined): SubjectContentController {
    const { contentTypes } = useLibraryTypes();

    const { filters, sorting, page, setFilters, setSorting, setPage } = useUrlTableState({
        defaultFilters: { contentFilter: '' },
        defaultSort: DEFAULT_SORTING,
        sortKeys: Object.keys(SUBJECT_CONTENT_SORT_FIELD),
        pageParam: 'contentPage',
        sortParam: 'contentSort',
    });
    const filterQuery = filters.contentFilter;
    const pageIndex = page - 1;

    const hasActiveFilters = filterQuery.trim().length > 0;

    const debouncedFilterQuery = useDebouncedValue(filterQuery, 300);
    const setFilterField = useCallback(
        (mutate: (query: string) => string) => setFilters({ contentFilter: mutate(filterQuery) }),
        [filterQuery, setFilters],
    );

    const onSortingChange: OnChangeFn<SortingState> = useCallback(
        (updater) => {
            const next = typeof updater === 'function' ? updater(sorting) : updater;
            setSorting(next.length > 0 ? next : DEFAULT_SORTING);
        },
        [sorting, setSorting],
    );

    const onPaginationChange: OnChangeFn<PaginationState> = useCallback(
        (updater) => {
            const next =
                typeof updater === 'function' ? updater({ pageIndex, pageSize: SUBJECT_CONTENT_PAGE_SIZE }) : updater;
            setPage(next.pageIndex + 1);
        },
        [pageIndex, setPage],
    );

    const queryParams = useMemo((): ContentQueryParams => {
        const params: ContentQueryParams = {
            topLevelOnly: true,
            pageNumber: pageIndex + 1,
            pageSize: SUBJECT_CONTENT_PAGE_SIZE,
        };

        const scope = metadataContainerId
            ? `Relationships has (RelatedMetadataContainerId = "${escapeQueryString(metadataContainerId)}")`
            : '';
        const trimmed = debouncedFilterQuery.trim();
        const filter = [scope, trimmed ? `(${trimmed})` : ''].filter(Boolean).join(' and ');
        if (filter) {
            params.filter = filter;
        }

        const primarySort = sorting[0];
        const serverField = primarySort ? SUBJECT_CONTENT_SORT_FIELD[primarySort.id] : undefined;
        if (primarySort && serverField) {
            params.sortBy = serverField;
            params.sortDirection = primarySort.desc ? 'Descending' : 'Ascending';
        }

        return params;
    }, [metadataContainerId, pageIndex, debouncedFilterQuery, sorting]);

    const { data, isLoading, error } = useContentList(queryParams, { enabled: Boolean(metadataContainerId) });

    const contentItems = data?.items ?? [];
    const totalItems = data?.totalItems ?? 0;
    const pageCount = Math.max(1, Math.ceil(totalItems / SUBJECT_CONTENT_PAGE_SIZE));

    useEffect(() => {
        if (data && !isLoading && !error && filterQuery === debouncedFilterQuery && page > pageCount) {
            setPage(pageCount, { replace: true });
        }
    }, [data, isLoading, error, filterQuery, debouncedFilterQuery, page, pageCount, setPage]);

    const headerFilters: ContentHeaderFilters = useMemo(() => {
        const selection = readContentFilterSelection(filterQuery);
        return {
            contentTypeOptions: buildContentFilterOptions(contentTypes),
            contentType: selection ? contentFilterSelectionValue(selection) : '',
            monitoring: readContentFilter(filterQuery, 'monitoring'),
            fulfillment: readContentFilter(filterQuery, 'fulfillment'),
            title: readContentFilter(filterQuery, 'title'),
            publishDateFrom: readContentFilter(filterQuery, 'publishDateFrom'),
            publishDateTo: readContentFilter(filterQuery, 'publishDateTo'),
        };
    }, [contentTypes, filterQuery]);

    const headerFilterHandlers: ContentHeaderFilterHandlers = useMemo(
        () => ({
            onContentTypeChange: (value) =>
                setFilterField((query) => setContentFilterSelection(query, parseContentFilterSelectionValue(value))),
            onTitleChange: (value) => setFilterField((query) => setContentFilter(query, 'title', value)),
            onFulfillmentChange: (value) => setFilterField((query) => setContentFilter(query, 'fulfillment', value)),
            onMonitoringChange: (value) => setFilterField((query) => setContentFilter(query, 'monitoring', value)),
            onPublishDateChange: ({ from, to }) =>
                setFilterField((query) =>
                    setContentFilter(setContentFilter(query, 'publishDateFrom', from), 'publishDateTo', to),
                ),
        }),
        [setFilterField],
    );

    const onFilterCommit = useCallback<SubjectContentController['onFilterCommit']>(
        ({ title, contentType, fulfillment }) => {
            setFilterField((query) => {
                const withTitle = setContentFilter(query, 'title', title);
                const withType = setContentFilterSelection(withTitle, parseContentFilterSelectionValue(contentType));
                return setContentFilter(withType, 'fulfillment', fulfillment);
            });
        },
        [setFilterField],
    );

    const goToPage = useCallback((nextPage: number) => setPage(Math.max(1, nextPage)), [setPage]);

    return {
        contentItems,
        totalItems,
        isLoading,
        error,
        sorting,
        onSortingChange,
        pagination: { pageIndex, pageSize: SUBJECT_CONTENT_PAGE_SIZE },
        onPaginationChange,
        headerFilters,
        headerFilterHandlers,
        onFilterCommit,
        pageCount,
        currentPage: pageIndex + 1,
        goToPage,
        hasActiveFilters,
    };
}
