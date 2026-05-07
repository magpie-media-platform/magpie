export interface MetadataImage {
    type: string;
    url: string;
    width?: number | null;
    height?: number | null;
}

export interface MetadataContribution {
    ordinal: number;
    title?: string | null;
    overview?: string | null;
    publishDate?: string | null;
    images?: MetadataImage[] | null;
}

export interface MetadataContainer {
    id: string;
    contributions?: MetadataContribution[] | null;
}

export function sortedContributions<T extends { ordinal: number }>(
    container?: { contributions?: T[] | null } | null,
): T[] {
    if (!container?.contributions?.length) {
        return [];
    }
    return [...container.contributions].sort((a, b) => a.ordinal - b.ordinal);
}

export function extractImageUrl(container?: MetadataContainer | null): string | undefined {
    const sorted = sortedContributions(container);
    if (!sorted.length) {
        return undefined;
    }

    for (const contribution of sorted) {
        const images = contribution.images;
        if (!images?.length) continue;

        const primaryImage = images.find(
            (img) => img.type === 'poster' || img.type === 'primary' || img.type === 'profile',
        );
        if (primaryImage?.url) {
            return primaryImage.url;
        }
    }

    for (const contribution of sorted) {
        const images = contribution.images;
        if (!images?.length) continue;

        const firstImage = images[0];
        if (firstImage?.url) {
            return firstImage.url;
        }
    }

    return undefined;
}

export function extractTitle(container?: MetadataContainer | null): string | undefined {
    for (const contribution of sortedContributions(container)) {
        if (contribution.title) {
            return contribution.title;
        }
    }
    return undefined;
}

export function extractOverview(container?: MetadataContainer | null): string | undefined {
    for (const contribution of sortedContributions(container)) {
        if (contribution.overview) {
            return contribution.overview;
        }
    }
    return undefined;
}

export function extractPublishDate(container?: MetadataContainer | null): string | undefined {
    for (const contribution of sortedContributions(container)) {
        if (contribution.publishDate) {
            return contribution.publishDate;
        }
    }
    return undefined;
}

export function extractYear(container?: MetadataContainer | null): number | undefined {
    for (const contribution of sortedContributions(container)) {
        if (contribution.publishDate) {
            const year = new Date(contribution.publishDate).getUTCFullYear();
            if (!isNaN(year)) {
                return year;
            }
        }
    }
    return undefined;
}
