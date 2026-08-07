import type { DownloadStatusValue } from '@/hooks/useDownloads';
import { ACTIVE_DOWNLOAD_STATUSES } from '@/types/downloads';

export const DOWNLOAD_URL_PARAMS = {
    tab: 'tab',
    queueClient: 'qclient',
    queueState: 'qstate',
    queuePage: 'qpage',
    queueSort: 'qsort',
    historyClient: 'hclient',
    historyState: 'hstate',
    historyPage: 'hpage',
    historySort: 'hsort',
} as const;

export const DOWNLOAD_TABS = ['queue', 'history'] as const;

type DownloadTab = (typeof DOWNLOAD_TABS)[number];

export const DEFAULT_DOWNLOAD_TAB: DownloadTab = 'queue';

export const HISTORY_DOWNLOAD_STATUSES: DownloadStatusValue[] = [
    'Downloaded',
    'Importing',
    'Imported',
    'Failed',
    'Cancelled',
];

export const QUEUE_DOWNLOAD_SORT_KEYS = ['title', 'client', 'status', 'progress', 'added', 'lastStatus'] as const;

export const HISTORY_DOWNLOAD_SORT_KEYS = ['title', 'client', 'status', 'completed', 'lastStatus'] as const;

const DEFAULT_QUEUE_DOWNLOAD_SORT = 'added:desc';

const DEFAULT_HISTORY_DOWNLOAD_SORT = 'completed:desc';

function normalizeTextParam(params: URLSearchParams, paramName: string) {
    const raw = params.get(paramName);
    if (raw === null) {
        return;
    }

    const normalized = raw.trim();
    if (!normalized) {
        params.delete(paramName);
    } else if (normalized !== raw) {
        params.set(paramName, normalized);
    }
}

function normalizeTabParam(params: URLSearchParams) {
    const raw = params.get(DOWNLOAD_URL_PARAMS.tab);
    if (raw === null) {
        return;
    }

    const normalized = raw.trim().toLowerCase();
    if (normalized === DEFAULT_DOWNLOAD_TAB || !DOWNLOAD_TABS.includes(normalized as DownloadTab)) {
        params.delete(DOWNLOAD_URL_PARAMS.tab);
    } else if (normalized !== raw) {
        params.set(DOWNLOAD_URL_PARAMS.tab, normalized);
    }
}

function normalizeStateParam(
    params: URLSearchParams,
    paramName: string,
    allowedStatuses: readonly DownloadStatusValue[],
) {
    const raw = params.get(paramName);
    if (raw === null) {
        return;
    }

    const normalized = allowedStatuses.find((status) => status.toLowerCase() === raw.trim().toLowerCase());
    if (!normalized) {
        params.delete(paramName);
    } else if (normalized !== raw) {
        params.set(paramName, normalized);
    }
}

function normalizePageParam(params: URLSearchParams, paramName: string) {
    const raw = params.get(paramName);
    if (raw === null) {
        return;
    }

    const trimmed = raw.trim();
    const page = Number(trimmed);
    if (!/^\d+$/.test(trimmed) || !Number.isSafeInteger(page) || page <= 1) {
        params.delete(paramName);
    } else if (String(page) !== raw) {
        params.set(paramName, String(page));
    }
}

function normalizeSortParam(
    params: URLSearchParams,
    paramName: string,
    sortKeys: readonly string[],
    defaultSort: string,
) {
    const raw = params.get(paramName);
    if (raw === null) {
        return;
    }

    const parts = raw.trim().split(':');
    const key =
        parts.length === 2
            ? sortKeys.find((sortKey) => sortKey.toLowerCase() === (parts[0] ?? '').toLowerCase())
            : undefined;
    const direction = (parts[1] ?? '').toLowerCase();
    const normalized = key && (direction === 'asc' || direction === 'desc') ? `${key}:${direction}` : '';

    if (!normalized || normalized === defaultSort) {
        params.delete(paramName);
    } else if (normalized !== raw) {
        params.set(paramName, normalized);
    }
}

export function canonicalizeDownloadSearchParams(searchParams: URLSearchParams): URLSearchParams | null {
    const next = new URLSearchParams(searchParams);

    normalizeTabParam(next);
    normalizeTextParam(next, DOWNLOAD_URL_PARAMS.queueClient);
    normalizeTextParam(next, DOWNLOAD_URL_PARAMS.historyClient);
    normalizeStateParam(next, DOWNLOAD_URL_PARAMS.queueState, ACTIVE_DOWNLOAD_STATUSES);
    normalizeStateParam(next, DOWNLOAD_URL_PARAMS.historyState, HISTORY_DOWNLOAD_STATUSES);
    normalizePageParam(next, DOWNLOAD_URL_PARAMS.queuePage);
    normalizePageParam(next, DOWNLOAD_URL_PARAMS.historyPage);
    normalizeSortParam(next, DOWNLOAD_URL_PARAMS.queueSort, QUEUE_DOWNLOAD_SORT_KEYS, DEFAULT_QUEUE_DOWNLOAD_SORT);
    normalizeSortParam(
        next,
        DOWNLOAD_URL_PARAMS.historySort,
        HISTORY_DOWNLOAD_SORT_KEYS,
        DEFAULT_HISTORY_DOWNLOAD_SORT,
    );

    return next.toString() === searchParams.toString() ? null : next;
}
