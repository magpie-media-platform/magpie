type Uuid = string;

export interface DiskSpaceInfo {
    id: Uuid;

    name: string;

    path: string;

    usedBytes: number;

    totalBytes: number;

    freeBytes: number;
}

export type DownloadState = 'downloading' | 'queued' | 'stalled' | 'completed' | 'failed';

export interface DownloadStateCounts {
    downloading: number;
    queued: number;
    stalled: number;
}

export interface ActiveDownloadItem {
    id: string;
    title: string;
    progress: number;
    state: DownloadState;
    size?: string;
    eta?: string;
}
