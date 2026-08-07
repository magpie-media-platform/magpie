import type { LibraryTypeDefinition } from '@/types/library';

export interface ContentFilterSelection {
    contentType: string;
    level?: string;
}

export interface ContentFilterOption extends ContentFilterSelection {
    value: string;
    label: string;
}

export function contentFilterSelectionValue(selection: ContentFilterSelection): string {
    return JSON.stringify([selection.contentType, selection.level ?? null]);
}

export function parseContentFilterSelectionValue(value: string): ContentFilterSelection | null {
    if (!value) {
        return null;
    }

    try {
        const parsed: unknown = JSON.parse(value);
        if (
            !Array.isArray(parsed) ||
            parsed.length !== 2 ||
            typeof parsed[0] !== 'string' ||
            (parsed[1] !== null && typeof parsed[1] !== 'string')
        ) {
            return null;
        }

        return parsed[1] === null ? { contentType: parsed[0] } : { contentType: parsed[0], level: parsed[1] };
    } catch {
        return null;
    }
}

export function buildContentFilterOptions(
    contentTypes: LibraryTypeDefinition[],
    restrictToContentType?: string,
): ContentFilterOption[] {
    const scoped = restrictToContentType
        ? contentTypes.filter((contentType) => contentType.slug === restrictToContentType)
        : contentTypes;
    return scoped.flatMap((contentType) => {
        const broadSelection = { contentType: contentType.slug };
        const broadOption: ContentFilterOption = {
            ...broadSelection,
            value: contentFilterSelectionValue(broadSelection),
            label: contentType.name,
        };
        const rootLevels = contentType.levels.filter((level) => level.parentSlug === null);

        // A single root is equivalent to the bare content type, so only offer the
        // "Type > Root" options when a type declares more than one sibling root.
        const rootLevelOptions =
            rootLevels.length > 1
                ? rootLevels.map((level): ContentFilterOption => {
                      const selection = { contentType: contentType.slug, level: level.slug };
                      return {
                          ...selection,
                          value: contentFilterSelectionValue(selection),
                          label: `${contentType.name} > ${level.displayName}`,
                      };
                  })
                : [];

        return [broadOption, ...rootLevelOptions];
    });
}
