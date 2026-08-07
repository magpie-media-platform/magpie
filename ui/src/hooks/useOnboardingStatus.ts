import { useQuery } from '@tanstack/react-query';

import { apiGet } from '@/lib/api';
import { queryErrorOrNull } from '@/lib/utils';
import { useInvalidateQueriesOnEvents } from './useInvalidateQueriesOnEvents';

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
}

export function deriveOnboardingSteps(status: OnboardingStatus | undefined): OnboardingStep[] {
    return [
        {
            key: 'indexer',
            title: 'Add an indexer',
            reason: 'Indexers are the sources Magpie searches for releases.',
            to: '/admin/indexers',
            done: status?.hasIndexer ?? false,
        },
        {
            key: 'downloadClient',
            title: 'Add a download client',
            reason: 'Grabbed releases are handed to a download client to fetch.',
            to: '/admin/download-clients',
            done: status?.hasDownloadClient ?? false,
        },
        {
            key: 'rootFolder',
            title: 'Add a root folder',
            reason: 'Root folders are where finished downloads are imported and stored.',
            to: '/admin/root-folders',
            done: status?.hasRootFolder ?? false,
        },
        {
            key: 'monitoredContent',
            title: 'Add content to monitor',
            reason: 'Monitored content is what Magpie keeps searching and grabbing for.',
            to: '/library/add',
            done: status?.hasMonitoredContent ?? false,
        },
    ];
}

export function isOnboardingComplete(status: OnboardingStatus | undefined): boolean {
    if (!status) {
        return false;
    }
    if (typeof status.isComplete === 'boolean') {
        return status.isComplete;
    }
    return status.hasIndexer && status.hasDownloadClient && status.hasRootFolder && status.hasMonitoredContent;
}

const onboardingStatusQueryKey = ['system', 'onboarding-status'] as const;

async function fetchOnboardingStatus(): Promise<OnboardingStatus> {
    return apiGet<OnboardingStatus>('/system/onboarding-status');
}

export function useOnboardingStatus() {
    useInvalidateQueriesOnEvents(
        [
            'indexer.created',
            'indexer.updated',
            'indexer.deleted',
            'downloadClient.created',
            'downloadClient.updated',
            'downloadClient.deleted',
            'library.rootFolder.created',
            'library.rootFolder.updated',
            'library.rootFolder.deleted',
            'content.hierarchyItem.created',
            'content.hierarchyItem.updated',
            'content.hierarchyItem.deleted',
        ],
        [onboardingStatusQueryKey],
        { debounceMs: 300, exact: true, maxWaitMs: 1200, owner: 'onboarding-shell' },
    );
    const query = useQuery({
        queryKey: onboardingStatusQueryKey,
        queryFn: fetchOnboardingStatus,
    });

    return {
        status: query.data,
        steps: deriveOnboardingSteps(query.data),
        isComplete: isOnboardingComplete(query.data),
        isLoading: query.isLoading,
        error: queryErrorOrNull(query),
        refetch: query.refetch,
    };
}
