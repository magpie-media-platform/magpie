import { extractImageUrl, extractTitle, extractOverview, extractYear, type MetadataContainer } from '@/lib/metadata';
import type { PagedList } from './paging';

export type MonitoringIntentValue = 0 | 1 | 2;

export type FulfillmentStatusValue = 0 | 1 | 2 | 3;

export type ContentKind = 'episode' | 'movie' | 'music' | 'book' | 'unknown';

export type MonitoringStatus = 'none' | 'active' | 'excluded';

export type FulfillmentStatus = 'missing' | 'upgradable' | 'fulfilled' | 'fulfilledExternally';

export function mapContentType(contentType: string): ContentKind {
    const typeMap: Record<string, ContentKind> = {
        episodes: 'episode',
        movies: 'movie',
        music: 'music',
        books: 'book',
    };
    return typeMap[contentType.toLowerCase()] ?? 'unknown';
}

export function mapContentKindToApi(kind: ContentKind): string | undefined {
    const kindMap: Record<ContentKind, string | undefined> = {
        episode: 'episodes',
        movie: 'movies',
        music: 'music',
        book: 'books',
        unknown: undefined,
    };
    return kindMap[kind];
}

export function mapMonitoringIntent(value: MonitoringIntentValue): MonitoringStatus {
    const statusMap: Record<MonitoringIntentValue, MonitoringStatus> = {
        0: 'none',
        1: 'active',
        2: 'excluded',
    };
    return statusMap[value];
}

export function mapMonitoringStatusToApi(status: MonitoringStatus): MonitoringIntentValue {
    const statusMap: Record<MonitoringStatus, MonitoringIntentValue> = {
        none: 0,
        active: 1,
        excluded: 2,
    };
    return statusMap[status];
}

export function mapFulfillmentStatus(value: FulfillmentStatusValue): FulfillmentStatus {
    const statusMap: Record<FulfillmentStatusValue, FulfillmentStatus> = {
        0: 'missing',
        1: 'upgradable',
        2: 'fulfilled',
        3: 'fulfilledExternally',
    };
    return statusMap[value];
}

export function mapFulfillmentStatusToApi(status: FulfillmentStatus): FulfillmentStatusValue {
    const statusMap: Record<FulfillmentStatus, FulfillmentStatusValue> = {
        missing: 0,
        upgradable: 1,
        fulfilled: 2,
        fulfilledExternally: 3,
    };
    return statusMap[status];
}

export interface ApiMediaFile {
    id: string;
    contentItemId: string;
    fileKind: string;
    mimeType?: string | null;
    filePath: string;
    fileSize: number;
    createdAt: string;
    createdByUserId?: string | null;
    modifiedAt?: string | null;
    modifiedByUserId?: string | null;
}

export interface ApiContentItem {
    id: string;
    contentHierarchyItemId: string;
    fulfillment: FulfillmentStatusValue;
    requestedAt?: string | null;
    requestedByUserId?: string | null;
    requestedViaWishlistId?: string | null;
    dateLastSearched?: string | null;
    createdAt: string;
    createdByUserId?: string | null;
    modifiedAt?: string | null;
    modifiedByUserId?: string | null;
    mediaFiles?: ApiMediaFile[];
}

export type ApiMetadataContainer = MetadataContainer;

export interface ApiContentHierarchyItem {
    id: string;
    metadataContainerId: string;
    parentId?: string | null;
    qualityTierListId?: string | null;
    rootFolderId?: string | null;
    contentType: string;
    level: string;
    slug: string;
    name: string;
    monitoring: MonitoringIntentValue;
    path?: string | null;
    languagePreferences?: string[];
    settings?: Record<string, string>;
    dateLastRefreshed?: string | null;
    createdAt: string;
    createdByUserId?: string | null;
    modifiedAt?: string | null;
    modifiedByUserId?: string | null;

    contentItem?: ApiContentItem | null;
    metadataContainer?: ApiMetadataContainer | null;
}

export type PagedContentHierarchyItems = PagedList<ApiContentHierarchyItem>;

export interface ApiContentStats {
    total: number;
    missing: number;
    upgradable: number;
    fulfilled: number;
    fulfilledExternally: number;
}

export interface ContentListItem {
    id: string;
    title: string;
    posterUrl?: string;
    kind: ContentKind;
    monitoring: MonitoringStatus;
    fulfillment: FulfillmentStatus;
    year?: number;
    dateAdded?: string;
    lastSearched?: string;
}

export interface ContentDetailItem {
    id: string;
    title: string;
    posterUrl?: string;
    kind: ContentKind;
    monitoring: MonitoringStatus;
    fulfillment: FulfillmentStatus;
    year?: number;
    overview?: string;
    subjectId?: string;
    subjectName?: string;
    parentId?: string;
    parentTitle?: string;
    path?: string;
    dateAdded?: string;
    lastSearched?: string;
    lastRefreshed?: string;
}

export function mapToContentListItem(item: ApiContentHierarchyItem): ContentListItem {
    const metadata = item.metadataContainer;
    const contentItem = item.contentItem;

    return {
        id: item.id,
        title: extractTitle(metadata) ?? item.name,
        posterUrl: extractImageUrl(metadata) ?? undefined,
        kind: mapContentType(item.contentType),
        monitoring: mapMonitoringIntent(item.monitoring),
        fulfillment: contentItem ? mapFulfillmentStatus(contentItem.fulfillment) : 'missing',
        year: extractYear(metadata) ?? undefined,
        dateAdded: item.createdAt,
        lastSearched: contentItem?.dateLastSearched ?? undefined,
    };
}

export function mapToContentDetailItem(item: ApiContentHierarchyItem): ContentDetailItem {
    const metadata = item.metadataContainer;
    const contentItem = item.contentItem;

    return {
        id: item.id,
        title: extractTitle(metadata) ?? item.name,
        posterUrl: extractImageUrl(metadata) ?? undefined,
        kind: mapContentType(item.contentType),
        monitoring: mapMonitoringIntent(item.monitoring),
        fulfillment: contentItem ? mapFulfillmentStatus(contentItem.fulfillment) : 'missing',
        year: extractYear(metadata) ?? undefined,
        overview: extractOverview(metadata) ?? undefined,
        subjectId: item.metadataContainerId,
        subjectName: extractTitle(metadata) ?? item.name,
        parentId: item.parentId ?? undefined,
        path: item.path ?? undefined,
        dateAdded: item.createdAt,
        lastSearched: contentItem?.dateLastSearched ?? undefined,
        lastRefreshed: item.dateLastRefreshed ?? undefined,
    };
}

export interface ContentQueryParams {
    contentType?: string;
    level?: string;
    parentId?: string;
    rootFolderId?: string;
    monitoring?: MonitoringIntentValue;
    search?: string;
    tag?: string;
    filter?: string;
    page?: number;
    pageSize?: number;
    sortBy?: string;
    sortDirection?: string;
}
