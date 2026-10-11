import { useCallback, useMemo } from 'react';
import { useQueryClient } from '@tanstack/react-query';

import { useContentList, contentQueryKeys } from '@/hooks/useContent';
import { type LibraryLevelDefinition, type LibraryTypeDefinition } from '@/hooks/useLibraryTypes';
import { magpieQlAnd, magpieQlString } from '@/lib/magpieQl';
import { type ContentListItem, type ContentQueryParams } from '@/types';

import { PAGE_SIZE, SERVER_SORT_FIELD } from './libraryPageConstants';

interface UseLibraryContentArgs {
    contentTypeSlug: string | undefined;
    currentContentType: LibraryTypeDefinition | null;
    routeContentTypeSlug: string | undefined;
    selectedLevel: LibraryLevelDefinition | null;
    allLevels?: boolean;
    libraryTypesError: unknown;
    isRestoringFilter?: boolean;
    debouncedFilterQuery: string;
    filterQuery: string;
    sortField: string;
    sortDir: string;
    pageIndex: number;
}

export function useLibraryContent({
    contentTypeSlug,
    currentContentType,
    routeContentTypeSlug,
    selectedLevel,
    allLevels = false,
    libraryTypesError,
    isRestoringFilter = false,
    debouncedFilterQuery,
    filterQuery,
    sortField,
    sortDir,
    pageIndex,
}: UseLibraryContentArgs) {
    const queryClient = useQueryClient();

    const queryParams = useMemo((): ContentQueryParams => {
        const params: ContentQueryParams = {
            pageNumber: pageIndex + 1,
            pageSize: PAGE_SIZE,
        };

        if (!allLevels) {
            params.topLevelOnly = true;
        }

        if (routeContentTypeSlug) {
            params.contentType = routeContentTypeSlug;
        }

        if (selectedLevel) {
            params.level = selectedLevel.slug;
        }

        if (debouncedFilterQuery.trim()) {
            params.filter = debouncedFilterQuery.trim();
        }

        const serverField = SERVER_SORT_FIELD[sortField];
        if (serverField) {
            params.sortBy = serverField;
            params.sortDirection = sortDir === 'asc' ? 'Ascending' : 'Descending';
        }

        return params;
    }, [routeContentTypeSlug, selectedLevel, allLevels, debouncedFilterQuery, sortField, sortDir, pageIndex]);

    const contentListEnabled = !contentTypeSlug || currentContentType !== null || libraryTypesError !== null;
    const {
        data: contentResult,
        isLoading,
        isPlaceholderData,
        error,
        refetch,
    } = useContentList(queryParams, {
        enabled: contentListEnabled && !isRestoringFilter,
        keepPreviousData: true,
    });

    const pageRowIds = useMemo(
        () => (isRestoringFilter || isPlaceholderData ? [] : (contentResult?.items ?? []).map((item) => item.id)),
        [contentResult?.items, isRestoringFilter, isPlaceholderData],
    );
    const totalItems = isRestoringFilter ? 0 : (contentResult?.totalItems ?? 0);
    const contentItems: ContentListItem[] = isRestoringFilter ? [] : (contentResult?.items ?? []);

    const bulkFilter = useMemo(() => {
        const clauses = allLevels ? [] : ['ParentId null'];
        if (routeContentTypeSlug) {
            clauses.push(`ContentType = ${magpieQlString(routeContentTypeSlug)}`);
        }
        if (selectedLevel) {
            clauses.push(`Level = ${magpieQlString(selectedLevel.slug)}`);
        }
        if (filterQuery.trim()) {
            clauses.push(`(${filterQuery.trim()})`);
        }
        return magpieQlAnd(clauses);
    }, [routeContentTypeSlug, selectedLevel, allLevels, filterQuery]);

    const handleRefresh = useCallback(async () => {
        await queryClient.invalidateQueries({ queryKey: contentQueryKeys.all });
    }, [queryClient]);

    return {
        contentItems,
        totalItems,
        pageRowIds,
        bulkFilter,
        isLoading,
        isPlaceholderData,
        error,
        refetch,
        handleRefresh,
    };
}
