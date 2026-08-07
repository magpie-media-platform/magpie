import { extractImageUrl, extractPublishDate, extractTitle, type MetadataContainer } from '@/lib/metadata';
import type { ApiContentHierarchyItem, FulfillmentStatus } from '@/types/content';
import { formatLevelLabel, mapFulfillmentStatus } from '@/types/content';

export interface ApiContentItemWithMetadata extends Omit<ApiContentHierarchyItem, 'metadataContainer'> {
    metadataContainer?: MetadataContainer | null;
}

interface MappedContentFields {
    title: string;
    subtitle?: string;
    date: string | undefined;
    posterUrl?: string;
    contentType: string;
    level: string;
    levelLabel: string;
    fulfillment: FulfillmentStatus;
}

export function extractContentFields(item: ApiContentItemWithMetadata): MappedContentFields {
    const metadata = item.metadataContainer;
    const contentItem = item.contentItem;

    const fulfillment: FulfillmentStatus = contentItem ? mapFulfillmentStatus(contentItem.fulfillment) : 'missing';

    const title = extractTitle(metadata) ?? item.name;

    const trimmedName = item.name?.trim();
    const subtitle = trimmedName && trimmedName !== title ? trimmedName : undefined;

    return {
        title,
        subtitle,
        date: extractPublishDate(metadata),
        posterUrl: extractImageUrl(metadata) ?? undefined,
        contentType: item.contentType,
        level: item.level,
        levelLabel: formatLevelLabel(item.level),
        fulfillment,
    };
}
