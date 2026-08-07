import { useQuery } from '@tanstack/react-query';

import { useInvalidateQueriesOnEvents } from '@/hooks/useInvalidateQueriesOnEvents';
import { apiGet } from '@/lib/api';
import { POLLING_INTERVALS } from '@/lib/queryClient';

export const QualityStatusState = {
    NoTierList: 0,
    Missing: 1,
    MetCutoff: 2,
    Upgradable: 3,
} as const;

export type QualityStatusStateValue = (typeof QualityStatusState)[keyof typeof QualityStatusState];

interface QualityStatusResponse {
    tierListName?: string | null;
    bestScore?: number | null;
    cutoffScore?: number | null;
    status: QualityStatusStateValue;
}

const qualityStatusQueryKeys = {
    forContent: (contentId: string) => ['quality-status', 'content', contentId] as const,
};

async function fetchQualityStatus(contentId: string): Promise<QualityStatusResponse> {
    return apiGet<QualityStatusResponse>(`/content/${contentId}/quality-status`);
}

export function useQualityStatus(contentId: string | null | undefined) {
    useInvalidateQueriesOnEvents(
        contentId
            ? [
                  'content.hierarchyItem.updated',
                  'content.hierarchyItem.deleted',
                  'content.fulfillment.changed',
                  'metadata.refreshed',
                  'download.created',
                  'download.updated',
                  'download.deleted',
                  'download.completed',
                  'download.failed',
                  'download.stalled',
                  'download.imported',
              ]
            : [],
        [qualityStatusQueryKeys.forContent(contentId ?? '')],
        {
            entityIds: contentId ? [contentId] : [],
            exact: true,
            owner: 'content-quality-status',
        },
    );

    useInvalidateQueriesOnEvents(
        contentId ? ['qualityTierList.created', 'qualityTierList.updated', 'qualityTierList.deleted'] : [],
        [qualityStatusQueryKeys.forContent(contentId ?? '')],
        { exact: true, owner: 'content-quality-status' },
    );

    return useQuery({
        queryKey: qualityStatusQueryKeys.forContent(contentId ?? ''),
        queryFn: () => fetchQualityStatus(contentId!),
        enabled: !!contentId,
        staleTime: POLLING_INTERVALS.normal / 2,
    });
}
