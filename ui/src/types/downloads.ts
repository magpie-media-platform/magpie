import type { DownloadStatusValue } from '@/hooks/useDownloads';

export interface DownloadClientOption {
    id: string;

    name: string;
}

export interface StateFilterOption {
    value: DownloadStatusValue | 'all';

    label: string;
}

export interface DownloadFilterState {
    client: string;

    state: string;
}
