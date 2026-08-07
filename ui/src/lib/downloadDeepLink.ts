import type { DownloadStatusValue } from '@/types/content';
import type { DownloadStateCounts } from '@/types/system';

const widgetStateToQstateToken: Record<keyof DownloadStateCounts, DownloadStatusValue> = {
    downloading: 'Downloading',
    queued: 'Queued',
    paused: 'Paused',
    stalled: 'Stalled',
};

export function getDownloadStateUrl(state: keyof DownloadStateCounts): string {
    return `/downloads?qstate=${widgetStateToQstateToken[state]}`;
}
