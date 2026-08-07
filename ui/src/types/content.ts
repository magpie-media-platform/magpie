import {
    extractCustomValues,
    extractExternalIds,
    extractReferences,
    extractGenres,
    extractImages,
    extractImageUrl,
    extractPublishDate,
    extractSequenceNumber,
    extractStandardFactValues,
    extractTitle,
    extractOverview,
    extractYear,
    type ContentExternalId,
    type ContentReference,
    type CustomMetadataValue,
    type MetadataContainer,
    type StandardFactValue,
} from '@/lib/metadata';
import type { GalleryImage } from '@/types/images';
import type { BadgeVariant } from './badge';
import type { PagedList } from './paging';

export type FulfillmentStatusValue = 'Missing' | 'Upgradable' | 'Fulfilled' | 'FulfilledExternally' | 'Partial';
export type ApiFulfillmentStatus = FulfillmentStatusValue;

export type FulfillmentStatus = 'missing' | 'upgradable' | 'fulfilled' | 'fulfilledExternally' | 'partial';

export function formatLevelLabel(level: string | undefined): string {
    const trimmed = level?.trim();
    if (trimmed) {
        const lastSegment = trimmed.slice(trimmed.lastIndexOf('-') + 1);
        return lastSegment.charAt(0).toUpperCase() + lastSegment.slice(1).toLowerCase();
    }
    return '';
}

export function mapFulfillmentStatus(value: FulfillmentStatusValue): FulfillmentStatus {
    const statusMap: Record<FulfillmentStatusValue, FulfillmentStatus> = {
        Missing: 'missing',
        Upgradable: 'upgradable',
        Fulfilled: 'fulfilled',
        FulfilledExternally: 'fulfilledExternally',
        Partial: 'partial',
    };
    return statusMap[value] ?? 'missing';
}

export function mapFulfillmentStatusToApiName(status: FulfillmentStatus): ApiFulfillmentStatus {
    const statusMap: Record<FulfillmentStatus, ApiFulfillmentStatus> = {
        missing: 'Missing',
        upgradable: 'Upgradable',
        fulfilled: 'Fulfilled',
        fulfilledExternally: 'FulfilledExternally',
        partial: 'Partial',
    };
    return statusMap[status];
}

export interface FulfillmentBadge {
    variant: BadgeVariant;
    label: string;
}

const FULFILLMENT_BADGE_MAP: Record<FulfillmentStatus, FulfillmentBadge> = {
    missing: { variant: 'missing', label: 'Missing' },
    upgradable: { variant: 'upgradable', label: 'Upgradable' },
    fulfilled: { variant: 'fulfilled', label: 'Fulfilled' },
    fulfilledExternally: { variant: 'fulfilledExternally', label: 'External' },
    partial: { variant: 'partial', label: 'Partial' },
};

export function getFulfillmentBadge(status: FulfillmentStatus): FulfillmentBadge {
    return FULFILLMENT_BADGE_MAP[status] ?? FULFILLMENT_BADGE_MAP.missing;
}

interface ApiMediaInfoTrack {
    type: string;
    name: string;
    codec?: string | null;
    bitRate?: number | null;
    durationSeconds?: number | null;
    width?: number | null;
    height?: number | null;
    frameRate?: number | null;
    videoRange?: string | null;
    bitDepth?: number | null;
    channels?: number | null;
    sampleRate?: number | null;
    language?: string | null;
    forced?: boolean | null;
    default?: boolean | null;
    sdh?: boolean | null;
    properties?: Record<string, string> | null;
}

export interface ApiMediaInfo {
    container?: string | null;
    durationSeconds?: number | null;
    bitRate?: number | null;
    tracks?: ApiMediaInfoTrack[] | null;
}

interface ApiMediaFile {
    id: string;
    contentItemId: string;
    fileKind: string;
    mimeType?: string | null;
    filePath: string;
    fileSize: number;
    mediaInfo?: ApiMediaInfo | null;
    createdAt: string;
    createdByUserId?: string | null;
    modifiedAt?: string | null;
    modifiedByUserId?: string | null;
}

export type DownloadStatusValue =
    | 'Grabbed'
    | 'Queued'
    | 'Downloading'
    | 'Paused'
    | 'Stalled'
    | 'Downloaded'
    | 'Importing'
    | 'Imported'
    | 'Failed'
    | 'Cancelled';

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
    activeAcquisitionStatus?: DownloadStatusValue | null;
}

export type ApiMetadataContainer = MetadataContainer;

export interface ApiContentHierarchyItem {
    id: string;
    metadataContainerId: string;
    parentId?: string | null;
    qualityTierListId?: string | null;
    effectiveQualityTierListId?: string | null;
    rootFolderId?: string | null;
    effectiveRootFolderId?: string | null;
    contentType: string;
    level: string;
    slug: string;
    name: string;
    monitoring: boolean | null;
    effectiveMonitoring: boolean;
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
    partial: number;
}

export type AcquisitionStatus = 'grabbed' | 'queued' | 'downloading' | 'paused' | 'stalled' | 'downloaded';

const ACQUISITION_STATUS_MAP: Partial<Record<DownloadStatusValue, AcquisitionStatus>> = {
    Grabbed: 'grabbed',
    Queued: 'queued',
    Downloading: 'downloading',
    Paused: 'paused',
    Stalled: 'stalled',
    Downloaded: 'downloaded',
};

export function mapAcquisitionStatus(value?: DownloadStatusValue | null): AcquisitionStatus | undefined {
    if (!value) {
        return undefined;
    }
    return ACQUISITION_STATUS_MAP[value];
}

export interface ContentListItem {
    id: string;
    title: string;
    parentId?: string;
    parentTitle?: string;
    posterUrl?: string;
    level: string;
    levelLabel: string;
    contentType: string;
    monitoring: boolean | null;
    effectiveMonitoring: boolean;
    fulfillment: FulfillmentStatus;
    year?: number;
    publishDate?: string;
    sequenceNumber?: number;
    overview?: string;
    dateAdded?: string;
    lastSearched?: string;
    acquisitionStatus?: AcquisitionStatus;
}

export interface ContentDetailItem {
    id: string;
    title: string;
    posterUrl?: string;
    level: string;
    levelLabel: string;
    monitoring: boolean | null;
    effectiveMonitoring: boolean;
    fulfillment: FulfillmentStatus;
    year?: number;
    publishDate?: string;
    overview?: string;
    parentId?: string;
    parentTitle?: string;
    path?: string | null;
    dateAdded?: string;
    lastSearched?: string;
    lastRefreshed?: string;
    mediaFiles?: ApiMediaFile[];
    externalIds?: ContentExternalId[];
    references?: ContentReference[];
    genres?: string[];
    factValues?: Record<string, StandardFactValue>;
    sequenceNumber?: number;
    customValues?: CustomMetadataValue[];
    qualityTierListId?: string;
    contentType: string;
    rootFolderId?: string | null;
    contentItemId?: string | null;
    acquisitionStatus?: AcquisitionStatus;
    previews: ContentPreviewClip[];
    images: GalleryImage[];
}

export interface ContentPreviewClip {
    url: string;
    kind: string;
    title?: string;
    mimeType?: string;
    embedKind?: string;
    language?: string;
}

export function mapToContentListItem(item: ApiContentHierarchyItem): ContentListItem {
    const metadata = item.metadataContainer;
    const contentItem = item.contentItem;

    return {
        id: item.id,
        title: extractTitle(metadata) ?? item.name,
        parentId: item.parentId ?? undefined,
        posterUrl: extractImageUrl(metadata) ?? undefined,
        level: item.level,
        levelLabel: formatLevelLabel(item.level),
        contentType: item.contentType,
        monitoring: item.monitoring,
        effectiveMonitoring: item.effectiveMonitoring,
        fulfillment: contentItem ? mapFulfillmentStatus(contentItem.fulfillment) : 'missing',
        year: extractYear(metadata) ?? undefined,
        publishDate: extractPublishDate(metadata) ?? undefined,
        sequenceNumber: extractSequenceNumber(metadata) ?? undefined,
        overview: extractOverview(metadata) ?? undefined,
        dateAdded: item.createdAt,
        lastSearched: contentItem?.dateLastSearched ?? undefined,
        acquisitionStatus: mapAcquisitionStatus(contentItem?.activeAcquisitionStatus),
    };
}

export function mapToContentDetailItem(item: ApiContentHierarchyItem): ContentDetailItem {
    const metadata = item.metadataContainer;
    const contentItem = item.contentItem;

    return {
        id: item.id,
        title: extractTitle(metadata) ?? item.name,
        posterUrl: extractImageUrl(metadata) ?? undefined,
        level: item.level,
        levelLabel: formatLevelLabel(item.level),
        monitoring: item.monitoring,
        effectiveMonitoring: item.effectiveMonitoring,
        fulfillment: contentItem ? mapFulfillmentStatus(contentItem.fulfillment) : 'missing',
        year: extractYear(metadata) ?? undefined,
        publishDate: extractPublishDate(metadata) ?? undefined,
        overview: extractOverview(metadata) ?? undefined,
        parentId: item.parentId ?? undefined,
        path: item.path ?? null,
        dateAdded: item.createdAt,
        lastSearched: contentItem?.dateLastSearched ?? undefined,
        lastRefreshed: item.dateLastRefreshed ?? undefined,
        mediaFiles: contentItem?.mediaFiles ?? undefined,
        externalIds: extractExternalIds(metadata),
        references: extractReferences(metadata),
        genres: extractGenres(metadata),
        factValues: extractStandardFactValues(metadata),
        sequenceNumber: extractSequenceNumber(metadata) ?? undefined,
        customValues: extractCustomValues(metadata),
        qualityTierListId: item.qualityTierListId ?? undefined,
        contentType: item.contentType,
        rootFolderId: item.rootFolderId ?? undefined,
        contentItemId: contentItem?.id ?? undefined,
        acquisitionStatus: mapAcquisitionStatus(contentItem?.activeAcquisitionStatus),
        previews: [...(metadata?.contributions ?? [])]
            .sort((left, right) => left.ordinal - right.ordinal)
            .flatMap((contribution) =>
                [...(contribution.previews ?? [])].sort((left, right) => left.ordinal - right.ordinal),
            )
            .filter((p) => !!p?.url && !!p?.kind)
            .map((p) => ({
                url: p.url,
                kind: p.kind,
                title: p.title ?? undefined,
                mimeType: p.mimeType ?? undefined,
                embedKind: p.embedKind ?? undefined,
                language: p.language ?? undefined,
            })),
        images: extractImages(metadata),
    };
}

export interface ContentQueryParams {
    contentType?: string;
    level?: string;
    parentId?: string;
    topLevelOnly?: boolean;
    rootFolderId?: string;
    monitoring?: boolean;
    fulfillment?: ApiFulfillmentStatus;
    filter?: string;
    pageNumber?: number;
    pageSize?: number;
    sortBy?: string;
    sortDirection?: string;
}
