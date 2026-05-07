import type { useSearchParams } from 'react-router-dom';

import type { DownloadFilterState } from '@/types';

export const stateNameToValue: Record<string, string> = {
    grabbed: '0',
    queued: '1',
    downloading: '2',
    paused: '3',
    stalled: '4',
    downloaded: '5',
    imported: '6',
    failed: '7',
};

export const stateValueToName: Record<string, string> = {
    '0': 'grabbed',
    '1': 'queued',
    '2': 'downloading',
    '3': 'paused',
    '4': 'stalled',
    '5': 'downloaded',
    '6': 'imported',
    '7': 'failed',
};

export function readFiltersFromParams(
    searchParams: URLSearchParams,
    clientKey: string,
    stateKey: string,
): DownloadFilterState {
    const stateParam = searchParams.get(stateKey);
    return {
        client: searchParams.get(clientKey) ?? '',
        state: stateParam ? (stateNameToValue[stateParam.toLowerCase()] ?? '') : '',
    };
}

export function writeFiltersToParams(
    setSearchParams: ReturnType<typeof useSearchParams>[1],
    filters: DownloadFilterState,
    clientKey: string,
    stateKey: string,
): void {
    setSearchParams(
        (prev) => {
            const next = new URLSearchParams(prev);
            if (filters.client) {
                next.set(clientKey, filters.client);
            } else {
                next.delete(clientKey);
            }
            if (filters.state) {
                const name = stateValueToName[filters.state];
                if (name) {
                    next.set(stateKey, name);
                } else {
                    next.delete(stateKey);
                }
            } else {
                next.delete(stateKey);
            }
            return next;
        },
        { replace: true },
    );
}
