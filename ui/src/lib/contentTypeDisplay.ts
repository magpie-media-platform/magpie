import type { LibraryTypeDefinition } from '@/types/library';

interface ContentTypeDisplayInput {
    contentType?: string | null;

    level?: string | null;

    levelLabel?: string | null;
}

export function humanizeSlug(slug: string | null | undefined): string {
    const trimmed = slug?.trim();
    if (!trimmed) {
        return '';
    }
    return trimmed
        .split(/[-_]/)
        .filter(Boolean)
        .map((word) => {
            const lower = word.toLowerCase();
            return lower.charAt(0).toUpperCase() + lower.slice(1);
        })
        .join(' ');
}

export function humanizeCustomKey(key: string | null | undefined): string {
    const trimmed = key?.trim();
    if (!trimmed) {
        return '';
    }

    const lastSegment = trimmed.split('.').pop() ?? trimmed;

    const words = lastSegment
        .split(/[-_\s]+/)
        .flatMap((part) => part.replace(/([a-z0-9])([A-Z])/g, '$1 $2').split(' '))
        .filter(Boolean)
        .map((word) => {
            const lower = word.toLowerCase();
            return lower.charAt(0).toUpperCase() + lower.slice(1);
        });

    return words.length > 0 ? words.join(' ') : trimmed;
}

export function resolveContentTypeLabel(
    input: ContentTypeDisplayInput,
    contentTypes: LibraryTypeDefinition[] = [],
): string {
    const level = input.level?.trim();
    const slug = input.contentType?.trim();

    if (isUnrecognizedContentLevel(input, contentTypes)) {
        return 'Needs classification';
    }

    if (level) {
        const matchingTypes = slug
            ? contentTypes.filter((t) => t.slug.toLowerCase() === slug.toLowerCase())
            : contentTypes;
        const registeredLevel = matchingTypes
            .flatMap((t) => t.levels)
            .find((l) => l.slug.toLowerCase() === level.toLowerCase());

        if (registeredLevel?.displayName?.trim()) {
            return registeredLevel.displayName;
        }
    }

    const levelLabel = input.levelLabel?.trim();
    if (levelLabel) {
        return levelLabel;
    }

    if (level) {
        return humanizeSlug(level);
    }

    if (!slug) {
        return '';
    }

    const registered = contentTypes.find((t) => t.slug.toLowerCase() === slug.toLowerCase());
    if (registered?.name?.trim()) {
        return registered.name;
    }

    return humanizeSlug(slug);
}

export function isUnrecognizedContentLevel(
    input: ContentTypeDisplayInput,
    contentTypes: LibraryTypeDefinition[] = [],
): boolean {
    const level = input.level?.trim().toLowerCase();
    const slug = input.contentType?.trim().toLowerCase();
    if (!level || !slug) {
        return false;
    }

    const type = contentTypes.find((candidate) => candidate.slug.toLowerCase() === slug);
    return !!type?.levels.length && !type.levels.some((candidate) => candidate.slug.toLowerCase() === level);
}
