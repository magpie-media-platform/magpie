import { useQueries } from '@tanstack/react-query';

import { apiGet, buildUrl } from '@/lib/api';
import type { PagedList } from '@/types/paging';
import { queryErrorOrNull } from '@/lib/utils';
import { POLLING_INTERVALS } from '@/lib/queryClient';
import { countConfiguredIndexerInstances, indexerQueryKeys, type ApiIndexer } from './useIndexers';
import { downloadClientQueryKeys } from './useDownloadClients';
import { rootFolderQueryKeys } from './useRootFolders';
import { useInvalidateQueriesOnEvents } from './useInvalidateQueriesOnEvents';
import { useInstanceName } from './useInstanceName';

export interface OnboardingStatus {
    hasIndexer: boolean;
    hasDownloadClient: boolean;
    hasRootFolder: boolean;
    hasMonitoredContent: boolean;
    isComplete: boolean;
}

type OnboardingStepKey = 'indexer' | 'downloadClient' | 'rootFolder' | 'monitoredContent';

export interface OnboardingStep {
    key: OnboardingStepKey;
    title: string;
    reason: string;
    to: string;
    done: boolean;
    failed: boolean;
}

export function deriveOnboardingSteps(status: OnboardingStatus | undefined, instanceName: string): OnboardingStep[] {
    return [
        {
            key: 'indexer',
            title: 'Add an indexer',
            reason: `Indexers are the sources ${instanceName} searches for releases.`,
            to: '/admin/indexers',
            done: status?.hasIndexer ?? false,
            failed: false,
        },
        {
            key: 'downloadClient',
            title: 'Add a download client',
            reason: 'Grabbed releases are handed to a download client to fetch.',
            to: '/admin/download-clients',
            done: status?.hasDownloadClient ?? false,
            failed: false,
        },
        {
            key: 'rootFolder',
            title: 'Add a root folder',
            reason: 'Root folders are where finished downloads are imported and stored.',
            to: '/admin/root-folders',
            done: status?.hasRootFolder ?? false,
            failed: false,
        },
        {
            key: 'monitoredContent',
            title: 'Add content to monitor',
            reason: `Monitored content is what ${instanceName} keeps searching and grabbing for.`,
            to: '/library/add',
            done: status?.hasMonitoredContent ?? false,
            failed: false,
        },
    ];
}

export function isOnboardingComplete(status: OnboardingStatus | undefined): boolean {
    return status?.isComplete ?? false;
}

const indexerListQueryKey = indexerQueryKeys.list();
const downloadClientListQueryKey = [...downloadClientQueryKeys.list(), '/download-clients'];
const rootFolderListQueryKey = rootFolderQueryKeys.list();
const monitoredContentQueryKey = ['system', 'onboarding-monitored-content'] as const;

const anyItem = (items: unknown[]) => items.length > 0;
const anyConfiguredIndexer = (indexers: ApiIndexer[]) => countConfiguredIndexerInstances(indexers) > 0;
const presenceStaleTime = POLLING_INTERVALS.normal / 2;

export function useOnboardingStatus() {
    const instanceName = useInstanceName();
    const invalidationOptions = { debounceMs: 300, exact: true, maxWaitMs: 1200 };
    useInvalidateQueriesOnEvents(
        ['indexer.created', 'indexer.updated', 'indexer.deleted'],
        [indexerListQueryKey],
        invalidationOptions,
    );
    useInvalidateQueriesOnEvents(
        ['downloadClient.created', 'downloadClient.updated', 'downloadClient.deleted'],
        [downloadClientListQueryKey],
        invalidationOptions,
    );
    useInvalidateQueriesOnEvents(
        ['library.rootFolder.created', 'library.rootFolder.updated', 'library.rootFolder.deleted'],
        [rootFolderListQueryKey],
        invalidationOptions,
    );
    useInvalidateQueriesOnEvents(
        ['content.hierarchyItem.created', 'content.hierarchyItem.updated', 'content.hierarchyItem.deleted'],
        [monitoredContentQueryKey],
        invalidationOptions,
    );
    const queries = useQueries({
        queries: [
            {
                queryKey: indexerListQueryKey,
                queryFn: () => apiGet<ApiIndexer[]>('/indexers'),
                select: anyConfiguredIndexer,
                staleTime: presenceStaleTime,
            },
            {
                queryKey: downloadClientListQueryKey,
                queryFn: () => apiGet<unknown[]>('/download-clients'),
                select: anyItem,
                staleTime: presenceStaleTime,
            },
            {
                queryKey: rootFolderListQueryKey,
                queryFn: () => apiGet<unknown[]>('/root-folders'),
                select: anyItem,
                staleTime: presenceStaleTime,
            },
            {
                queryKey: monitoredContentQueryKey,
                queryFn: () => apiGet<PagedList<unknown>>(buildUrl('/content', { monitoring: true, pageSize: 1 })),
                select: (page: PagedList<unknown>) => page.items.length > 0,
            },
        ],
    });
    const [indexerQuery, downloadClientQuery, rootFolderQuery, monitoredContentQuery] = queries;
    const status: OnboardingStatus = {
        hasIndexer: indexerQuery.data ?? false,
        hasDownloadClient: downloadClientQuery.data ?? false,
        hasRootFolder: rootFolderQuery.data ?? false,
        hasMonitoredContent: monitoredContentQuery.data ?? false,
        isComplete: queries.every((query) => query.data === true),
    };
    const failedSteps: Record<OnboardingStepKey, boolean> = {
        indexer: indexerQuery.isError,
        downloadClient: downloadClientQuery.isError,
        rootFolder: rootFolderQuery.isError,
        monitoredContent: monitoredContentQuery.isError,
    };

    return {
        status,
        steps: deriveOnboardingSteps(status, instanceName).map((step) => ({ ...step, failed: failedSteps[step.key] })),
        isComplete: isOnboardingComplete(status),
        isLoading: queries.some((query) => query.isLoading),
        error: queries.every((query) => query.isError) ? queryErrorOrNull(indexerQuery) : null,
        refetch: () => Promise.all(queries.map((query) => query.refetch())),
    };
}
