type Uuid = string;

export interface DiskSpaceInfo {
    id: Uuid;

    name: string;

    path: string;

    usedBytes: number;

    totalBytes: number;

    freeBytes: number;
}

export interface DiskSpaceTotals {
    usedBytes: number;

    totalBytes: number;

    freeBytes: number;
}

export type DownloadState = 'downloading' | 'queued' | 'paused' | 'stalled' | 'completed' | 'failed';

export interface DownloadStateCounts {
    downloading: number;
    queued: number;
    paused: number;
    stalled: number;
}

export interface ActiveDownloadItem {
    id: string;
    contentId?: string;
    parentId?: string;
    title: string;
    progress: number;
    state: DownloadState;
    size?: string;
    eta?: string;
}
