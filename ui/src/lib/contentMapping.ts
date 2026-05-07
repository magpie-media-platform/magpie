import { extractImageUrl, extractPublishDate, extractTitle, type MetadataContainer } from '@/lib/metadata';
import type { ApiContentHierarchyItem, ContentKind, FulfillmentStatus, FulfillmentStatusValue } from '@/types/content';
import { mapContentType, mapFulfillmentStatus } from '@/types/content';

export interface ApiContentItemWithMetadata extends Omit<ApiContentHierarchyItem, 'metadataContainer'> {
    metadataContainer?: MetadataContainer | null;
}

export interface MappedContentFields {
    title: string;
    subtitle?: string;
    date: string | undefined;
    posterUrl?: string;
    kind: ContentKind;
    fulfillment: FulfillmentStatus;
}

export function extractContentFields(item: ApiContentItemWithMetadata): MappedContentFields {
    const metadata = item.metadataContainer;
    const contentItem = item.contentItem;

    const kind: ContentKind = mapContentType(item.contentType);
    const fulfillment: FulfillmentStatus = contentItem
        ? mapFulfillmentStatus(contentItem.fulfillment as FulfillmentStatusValue)
        : 'missing';

    const title = extractTitle(metadata) ?? item.name;

    let subtitle: string | undefined;
    if (kind === 'episode' && item.level === 'episode') {
        subtitle = item.name !== title ? item.name : undefined;
    }

    return {
        title,
        subtitle,
        date: extractPublishDate(metadata),
        posterUrl: extractImageUrl(metadata) ?? undefined,
        kind,
        fulfillment,
    };
}
