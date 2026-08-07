import { useCallback, useMemo } from 'react';
import { useQueryClient } from '@tanstack/react-query';

import { useContentList, contentQueryKeys } from '@/hooks/useContent';
import { type LibraryLevelDefinition, type LibraryTypeDefinition } from '@/hooks/useLibraryTypes';
import { magpieQlAnd, magpieQlString } from '@/lib/magpieQl';
import { type ApiFulfillmentStatus, type ContentListItem, type ContentQueryParams } from '@/types';

import { PAGE_SIZE, SERVER_SORT_FIELD } from './libraryPageConstants';

interface UseLibraryContentArgs {
    contentTypeSlug: string | undefined;
    currentContentType: LibraryTypeDefinition | null;
    routeContentTypeSlug: string | undefined;
    selectedLevel: LibraryLevelDefinition | null;
    libraryTypesError: unknown;
    debouncedFilterQuery: string;
    fulfillment: ApiFulfillmentStatus | '';
    sortField: string;
    sortDir: string;
    pageIndex: number;
}

export function useLibraryContent({
    contentTypeSlug,
    currentContentType,
    routeContentTypeSlug,
    selectedLevel,
    libraryTypesError,
    debouncedFilterQuery,
    fulfillment,
    sortField,
    sortDir,
    pageIndex,
}: UseLibraryContentArgs) {
    const queryClient = useQueryClient();

    const queryParams = useMemo((): ContentQueryParams => {
        const params: ContentQueryParams = {
            pageNumber: pageIndex + 1,
            pageSize: PAGE_SIZE,
            topLevelOnly: true,
        };

        if (routeContentTypeSlug) {
            params.contentType = routeContentTypeSlug;
        }

        if (selectedLevel) {
            params.level = selectedLevel.slug;
        }

        if (debouncedFilterQuery.trim()) {
            params.filter = debouncedFilterQuery.trim();
        }

        if (fulfillment) {
            params.fulfillment = fulfillment;
        }

        const serverField = SERVER_SORT_FIELD[sortField];
        if (serverField) {
            params.sortBy = serverField;
            params.sortDirection = sortDir === 'asc' ? 'Ascending' : 'Descending';
        }

        return params;
    }, [routeContentTypeSlug, selectedLevel, debouncedFilterQuery, fulfillment, sortField, sortDir, pageIndex]);

    const contentListEnabled = !contentTypeSlug || currentContentType !== null || libraryTypesError !== null;
    const {
        data: contentResult,
        isLoading,
        error,
        refetch,
    } = useContentList(queryParams, {
        enabled: contentListEnabled,
    });

    const pageRowIds = useMemo(() => (contentResult?.items ?? []).map((item) => item.id), [contentResult?.items]);
    const totalItems = contentResult?.totalItems ?? 0;
    const contentItems: ContentListItem[] = contentResult?.items ?? [];

    const bulkFilter = useMemo(() => {
        const clauses = ['ParentId null'];
        if (routeContentTypeSlug) {
            clauses.push(`ContentType = ${magpieQlString(routeContentTypeSlug)}`);
        }
        if (selectedLevel) {
            clauses.push(`Level = ${magpieQlString(selectedLevel.slug)}`);
        }
        if (fulfillment) {
            clauses.push(`Fulfillment = ${fulfillment}`);
        }
        if (debouncedFilterQuery.trim()) {
            clauses.push(`(${debouncedFilterQuery.trim()})`);
        }
        return magpieQlAnd(clauses);
    }, [routeContentTypeSlug, selectedLevel, debouncedFilterQuery, fulfillment]);

    const handleRefresh = useCallback(async () => {
        await queryClient.invalidateQueries({ queryKey: contentQueryKeys.all });
    }, [queryClient]);

    return {
        contentItems,
        totalItems,
        pageRowIds,
        bulkFilter,
        isLoading,
        error,
        refetch,
        handleRefresh,
    };
}
