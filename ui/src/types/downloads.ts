import type { DownloadStatusValue } from './content';

export const ACTIVE_DOWNLOAD_STATUSES: readonly DownloadStatusValue[] = [
    'Grabbed',
    'Queued',
    'Downloading',
    'Paused',
    'Stalled',
];

export interface DownloadClientOption {
    id: string;

    name: string;
}

export interface DownloadFilterState {
    client: string;

    state: string;
}
