function titleCaseSlug(slug: string): string {
    return slug
        .split(/[-_]/)
        .filter((part) => part.length > 0)
        .map((part) => part.charAt(0).toUpperCase() + part.slice(1))
        .join(' ');
}

export function formatSubjectKindLabel(kind: string, displayName?: string | null): string {
    return displayName?.trim() || titleCaseSlug(kind);
}

export function resolveSubjectKindLabel(kind: string | undefined, displayName?: string | null): string {
    if (!kind) {
        return 'Subject';
    }
    return displayName?.trim() || titleCaseSlug(kind);
}

export function resolveSubjectKindPluralLabel(
    kind: string | undefined,
    pluralDisplayName?: string | null,
    displayName?: string | null,
): string {
    if (!kind) {
        return 'Subjects';
    }
    return pluralDisplayName?.trim() || displayName?.trim() || titleCaseSlug(kind);
}
