import { mapFulfillmentStatusToApiName, type ApiFulfillmentStatus, type FulfillmentStatus } from '@/types';
import { type SetFilterOptions } from '@/hooks/useUrlFilterState';

export const SERVER_SORT_FIELD: Record<string, string> = {
    title: 'Name',
    publishDate: 'PublishDate',
    dateAdded: 'CreatedAt',
    fulfilledDate: 'FulfilledAt',
};

export const VALID_SORT_FIELDS = Object.keys(SERVER_SORT_FIELD);

export const VALID_SORT_DIRECTIONS = ['asc', 'desc'];

const VALID_FULFILLMENT_STATUSES: FulfillmentStatus[] = [
    'missing',
    'partial',
    'upgradable',
    'fulfilled',
    'fulfilledExternally',
];

const VALID_FULFILLMENT_API_NAMES: ApiFulfillmentStatus[] = [
    'Missing',
    'Partial',
    'Upgradable',
    'Fulfilled',
    'FulfilledExternally',
];

export const LIBRARY_FILTER_DEFAULTS = {
    filter: '',
    sort: 'publishDate',
    sortDir: 'desc',
    fulfillment: '',
    savedFilter: '',
    page: '1',
};

export const PAGE_SIZE = 50;

export type LibraryFilterParams = typeof LIBRARY_FILTER_DEFAULTS;

export type UpdateLibraryFilterParams = (updates: Partial<LibraryFilterParams>, options?: SetFilterOptions) => void;

function normalizeFulfillmentStatus(value: string): FulfillmentStatus | null {
    return VALID_FULFILLMENT_STATUSES.includes(value as FulfillmentStatus) ? (value as FulfillmentStatus) : null;
}

export function normalizeFulfillmentApiName(value: string): ApiFulfillmentStatus | null {
    if (VALID_FULFILLMENT_API_NAMES.includes(value as ApiFulfillmentStatus)) {
        return value as ApiFulfillmentStatus;
    }
    const status = normalizeFulfillmentStatus(value);
    return status ? mapFulfillmentStatusToApiName(status) : null;
}

export interface SavedFilterReference {
    id: string;
    name: string;
    query: string;
}
