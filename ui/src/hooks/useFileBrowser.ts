import { useCallback, useState } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';

import { apiGet, buildUrl } from '@/lib/api';
import { useInvalidateQueriesOnEvents } from '@/hooks/useInvalidateQueriesOnEvents';
import { queryErrorOrNull } from '@/lib/utils';

export interface FileSystemEntry {
    name: string;

    path: string;

    type: 'file' | 'folder';

    size?: number;
}

async function fetchContents(endpoint: string, path: string): Promise<FileSystemEntry[]> {
    return apiGet<FileSystemEntry[]>(buildUrl(endpoint, { path }));
}

const fileBrowserQueryKey = (endpoint: string, path: string | undefined) => ['file-browser', endpoint, path] as const;

export function useFileBrowser(endpoint = '/system/files') {
    const queryClient = useQueryClient();
    const [path, setPath] = useState<string | undefined>(undefined);
    useInvalidateQueriesOnEvents(
        path ? ['download.imported', 'library.scan.completed'] : [],
        [fileBrowserQueryKey(endpoint, path)],
        { debounceMs: 300, exact: true, maxWaitMs: 1200, owner: 'file-browser' },
    );

    const query = useQuery({
        queryKey: fileBrowserQueryKey(endpoint, path),
        queryFn: () => fetchContents(endpoint, path!),
        enabled: path !== undefined,
    });

    const navigate = useCallback((next: string) => {
        setPath(next);
    }, []);

    const navigateAsync = useCallback(
        (next: string) => {
            setPath(next);
            return queryClient.fetchQuery({
                queryKey: fileBrowserQueryKey(endpoint, next),
                queryFn: () => fetchContents(endpoint, next),
                staleTime: 0,
            });
        },
        [endpoint, queryClient],
    );

    const reset = useCallback(() => {
        setPath(undefined);
    }, []);

    return {
        path,

        navigate,

        navigateAsync,

        entries: path !== undefined && query.data ? query.data : [],

        isLoading: query.isFetching,

        error: queryErrorOrNull(query),

        reset,
    };
}
