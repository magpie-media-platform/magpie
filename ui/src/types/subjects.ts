import { humanizeSlug } from '@/lib/contentTypeDisplay';

export function resolveSubjectKindLabel(kind: string | undefined, displayName?: string | null): string {
    if (!kind) {
        return 'Subject';
    }
    return displayName?.trim() || humanizeSlug(kind);
}

export function resolveSubjectKindPluralLabel(
    kind: string | undefined,
    pluralDisplayName?: string | null,
    displayName?: string | null,
): string {
    if (!kind) {
        return 'Subjects';
    }
    return pluralDisplayName?.trim() || displayName?.trim() || humanizeSlug(kind);
}
