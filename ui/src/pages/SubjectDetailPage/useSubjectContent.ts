import { useCallback, useMemo, useState } from 'react';
import { type OnChangeFn, type PaginationState, type SortingState } from '@tanstack/react-table';

import { type ContentHeaderFilterHandlers, type ContentHeaderFilters } from '@/components/ui/ContentList';
import { useCardLayout } from '@/hooks/ui/useCardLayout';
import { useContentList } from '@/hooks/useContent';
import { useDebouncedValue } from '@/hooks/useDebouncedValue';
import { useLibraryTypes } from '@/hooks/useLibraryTypes';
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
    pageCount: number;
    currentPage: number;
    goToPage: (page: number) => void;
    hasActiveFilters: boolean;
}

export function useSubjectContent(metadataContainerId: string | undefined): SubjectContentController {
    const { contentTypes } = useLibraryTypes();
    const usesCardLayout = useCardLayout();

    const [filterQuery, setFilterQuery] = useState('');
    const [sorting, setSorting] = useState<SortingState>(DEFAULT_SORTING);
    const [pageIndex, setPageIndex] = useState(0);

    const hasActiveFilters = !usesCardLayout && filterQuery.trim().length > 0;
    const [pagedWithFilters, setPagedWithFilters] = useState(hasActiveFilters);
    if (pagedWithFilters !== hasActiveFilters) {
        setPagedWithFilters(hasActiveFilters);
        setPageIndex(0);
    }

    const debouncedFilterQuery = useDebouncedValue(filterQuery, 300);
    const setFilterField = useCallback((mutate: (query: string) => string) => {
        setFilterQuery((current) => mutate(current));
        setPageIndex(0);
    }, []);

    const onSortingChange: OnChangeFn<SortingState> = useCallback((updater) => {
        setSorting((current) => {
            const next = typeof updater === 'function' ? updater(current) : updater;
            return next.length > 0 ? next : DEFAULT_SORTING;
        });
        setPageIndex(0);
    }, []);

    const onPaginationChange: OnChangeFn<PaginationState> = useCallback((updater) => {
        setPageIndex((current) => {
            const next =
                typeof updater === 'function'
                    ? updater({ pageIndex: current, pageSize: SUBJECT_CONTENT_PAGE_SIZE })
                    : updater;
            return next.pageIndex;
        });
    }, []);

    const queryParams = useMemo((): ContentQueryParams => {
        const params: ContentQueryParams = {
            topLevelOnly: true,
            pageNumber: pageIndex + 1,
            pageSize: SUBJECT_CONTENT_PAGE_SIZE,
        };

        const scope = metadataContainerId
            ? `Relationships has (RelatedMetadataContainerId = "${escapeQueryString(metadataContainerId)}")`
            : '';
        const trimmed = usesCardLayout ? '' : debouncedFilterQuery.trim();
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
    }, [metadataContainerId, pageIndex, usesCardLayout, debouncedFilterQuery, sorting]);

    const { data, isLoading, error } = useContentList(queryParams, { enabled: Boolean(metadataContainerId) });

    const contentItems = data?.items ?? [];
    const totalItems = data?.totalItems ?? 0;
    const pageCount = Math.max(1, Math.ceil(totalItems / SUBJECT_CONTENT_PAGE_SIZE));

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

    const goToPage = useCallback((page: number) => {
        setPageIndex(Math.max(0, page - 1));
    }, []);

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
        pageCount,
        currentPage: pageIndex + 1,
        goToPage,
        hasActiveFilters,
    };
}
