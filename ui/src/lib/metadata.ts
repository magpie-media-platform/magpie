import type { GalleryImage } from '@/types/images';

export interface MetadataImage {
    type: string;
    url: string;
    ordinal?: number | null;
    isPrimary?: boolean | null;
    width?: number | null;
    height?: number | null;
}

export interface MetadataExternalId {
    ordinal: number;
    value: string;
    source?: string | null;
}

export interface MetadataGenre {
    ordinal: number;
    name: string;
}

export interface MetadataContentRating {
    ordinal: number;
    rating?: string | null;
}

export interface MetadataReference {
    ordinal: number;
    kind: string;
    source: string;
    url: string;
    displayName?: string | null;
}

export interface MetadataCustomValue {
    key: string;
    value?: string | null;
}

export interface MetadataPreview {
    ordinal: number;
    url: string;
    kind: string;
    title?: string | null;
    mimeType?: string | null;
    embedKind?: string | null;
    language?: string | null;
}

export interface MetadataContribution {
    ordinal: number;
    providerType?: string | null;
    title?: string | null;
    overview?: string | null;
    publishDate?: string | null;
    runtime?: number | null;
    sequenceNumber?: number | null;
    productionStatus?: string | null;
    language?: string | null;
    images?: MetadataImage[] | null;
    externalIds?: MetadataExternalId[] | null;
    genres?: MetadataGenre[] | null;
    contentRatings?: MetadataContentRating[] | null;
    references?: MetadataReference[] | null;
    customMetadata?: MetadataCustomValue[] | null;
    previews?: MetadataPreview[] | null;
}

export interface MetadataContainer {
    id: string;
    contributions?: MetadataContribution[] | null;
}

export interface ContentExternalId {
    source: string;
    value: string;
    url?: string;
}

export interface ContentReference {
    url: string;
    displayName: string;
    source: string;
    kind: string;
}

export function sortedContributions<T extends { ordinal: number }>(
    container?: { contributions?: T[] | null } | null,
): T[] {
    if (!container?.contributions?.length) {
        return [];
    }
    return [...container.contributions].sort((a, b) => a.ordinal - b.ordinal);
}

const byOrdinal = (a: MetadataImage, b: MetadataImage): number => (a.ordinal ?? 0) - (b.ordinal ?? 0);

const IMAGE_TYPE_PREFERENCE = ['poster', 'primary', 'profile'];

export function extractImageUrl(container?: MetadataContainer | null): string | undefined {
    const sorted = sortedContributions(container);
    if (!sorted.length) {
        return undefined;
    }

    const marked = sorted
        .flatMap((c) => (c.images ?? []).map((img) => ({ contribution: c, img })))
        .filter((x) => x.img.isPrimary)
        .sort((a, b) => a.contribution.ordinal - b.contribution.ordinal || byOrdinal(a.img, b.img))[0];
    if (marked?.img.url) {
        return marked.img.url;
    }

    for (const contribution of sorted) {
        const images = contribution.images;
        if (!images?.length) continue;
        for (const type of IMAGE_TYPE_PREFERENCE) {
            const match = [...images].filter((img) => img.type?.toLowerCase() === type).sort(byOrdinal)[0];
            if (match?.url) {
                return match.url;
            }
        }
    }

    for (const contribution of sorted) {
        const images = contribution.images;
        if (!images?.length) continue;
        const firstImage = [...images].sort(byOrdinal)[0];
        if (firstImage?.url) {
            return firstImage.url;
        }
    }

    return undefined;
}

export function extractImages(container?: MetadataContainer | null): GalleryImage[] {
    const images: GalleryImage[] = [];
    for (const contribution of sortedContributions(container)) {
        if (!contribution.images?.length) {
            continue;
        }
        for (const img of contribution.images) {
            images.push({
                id: `${contribution.ordinal}-${img.url}`,
                url: img.url,
                type: img.type,
                isPrimary: img.isPrimary ?? undefined,
                width: img.width ?? undefined,
                height: img.height ?? undefined,
            });
        }
    }
    return images;
}

export function extractTitle(container?: MetadataContainer | null): string | undefined {
    for (const contribution of sortedContributions(container)) {
        if (contribution.title) {
            return contribution.title;
        }
    }
    return undefined;
}

function tidyTrailingEllipsis(text: string): string {
    const match = /[.…][\s.…]*$/.exec(text);
    if (!match) {
        return text;
    }
    const dotCount = (match[0].match(/[.…]/g) ?? []).length;
    if (dotCount < 2) {
        return text;
    }
    return `${text.slice(0, match.index).trimEnd()}…`;
}

export function extractOverview(container?: MetadataContainer | null): string | undefined {
    for (const contribution of sortedContributions(container)) {
        if (contribution.overview) {
            return tidyTrailingEllipsis(contribution.overview);
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

export function extractSequenceNumber(container?: MetadataContainer | null): number | undefined {
    for (const contribution of sortedContributions(container)) {
        if (typeof contribution.sequenceNumber === 'number') {
            return contribution.sequenceNumber;
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

export function extractGenres(container?: MetadataContainer | null): string[] {
    const seen = new Set<string>();
    const out: string[] = [];
    for (const contribution of sortedContributions(container)) {
        const genres = contribution.genres;
        if (!genres?.length) {
            continue;
        }
        const sorted = [...genres].sort((a, b) => a.ordinal - b.ordinal);
        for (const genre of sorted) {
            if (!genre?.name) {
                continue;
            }
            const key = genre.name.toLowerCase();
            if (seen.has(key)) {
                continue;
            }
            seen.add(key);
            out.push(genre.name);
        }
    }
    return out;
}

export function extractStatus(container?: MetadataContainer | null): string | undefined {
    for (const contribution of sortedContributions(container)) {
        if (contribution.productionStatus) {
            return contribution.productionStatus;
        }
    }
    return undefined;
}

export function extractLanguage(container?: MetadataContainer | null): string | undefined {
    for (const contribution of sortedContributions(container)) {
        if (contribution.language) {
            return contribution.language;
        }
    }
    return undefined;
}

function extractRuntime(container?: MetadataContainer | null): number | undefined {
    for (const contribution of sortedContributions(container)) {
        if (typeof contribution.runtime === 'number' && contribution.runtime > 0) {
            return contribution.runtime;
        }
    }
    return undefined;
}

function extractCertification(container?: MetadataContainer | null): string | undefined {
    for (const contribution of sortedContributions(container)) {
        const ratings = contribution.contentRatings;
        if (!ratings?.length) {
            continue;
        }
        const sorted = [...ratings].sort((a, b) => a.ordinal - b.ordinal);
        if (sorted[0]?.rating) {
            return sorted[0].rating;
        }
    }
    return undefined;
}

export function isInternalSource(source: string | null | undefined): boolean {
    return source?.startsWith('internal.') ?? false;
}

export function extractExternalIds(container?: MetadataContainer | null): ContentExternalId[] {
    const seen = new Set<string>();
    const out: ContentExternalId[] = [];
    for (const contribution of sortedContributions(container)) {
        const ids = contribution.externalIds;
        if (!ids?.length) {
            continue;
        }
        const sorted = [...ids].sort((a, b) => a.ordinal - b.ordinal);
        for (const id of sorted) {
            if (!id?.value || isInternalSource(id.source)) {
                continue;
            }
            const source = id.source ?? 'unknown';
            const key = `${source.toLowerCase()}:${id.value}`;
            if (seen.has(key)) {
                continue;
            }
            seen.add(key);
            out.push({ source, value: id.value });
        }
    }
    return out;
}

export function extractReferences(
    container?: { contributions?: { ordinal: number; references?: MetadataReference[] | null }[] | null } | null,
): ContentReference[] {
    const seen = new Set<string>();
    const out: ContentReference[] = [];
    for (const contribution of sortedContributions(container)) {
        const references = contribution.references;
        if (!references?.length) {
            continue;
        }
        const sorted = [...references].sort((a, b) => a.ordinal - b.ordinal);
        for (const reference of sorted) {
            const url = reference.url?.trim();
            if (!url) {
                continue;
            }
            const key = url.toLowerCase();
            if (seen.has(key)) {
                continue;
            }
            seen.add(key);
            out.push({
                url,
                displayName: reference.displayName?.trim() || url,
                source: reference.source,
                kind: reference.kind,
            });
        }
    }
    return out;
}

export type StandardFactValue = string | number;

export function extractStandardFactValues(container?: MetadataContainer | null): Record<string, StandardFactValue> {
    const values: Record<string, StandardFactValue> = {};

    const status = extractStatus(container);
    if (status) {
        values.status = status;
    }

    const language = extractLanguage(container);
    if (language) {
        values.language = language;
    }

    const runtime = extractRuntime(container);
    if (runtime !== undefined) {
        values.runtime = runtime;
    }

    const certification = extractCertification(container);
    if (certification) {
        values.certification = certification;
    }

    return values;
}

export interface CustomMetadataValue {
    key: string;
    value: string;
}

export function extractCustomValues(
    container?: { contributions?: { ordinal: number; customMetadata?: MetadataCustomValue[] | null }[] | null } | null,
): CustomMetadataValue[] {
    const seen = new Set<string>();
    const out: CustomMetadataValue[] = [];
    for (const contribution of sortedContributions(container)) {
        const entries = contribution.customMetadata;
        if (!entries?.length) {
            continue;
        }
        for (const entry of entries) {
            const key = entry.key?.trim();
            const value = entry.value?.trim();
            if (!key || !value) {
                continue;
            }
            const dedupeKey = key.toLowerCase();
            if (seen.has(dedupeKey)) {
                continue;
            }
            seen.add(dedupeKey);
            out.push({ key, value });
        }
    }
    return out;
}
