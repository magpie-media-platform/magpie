export type SubjectKind = 'show' | 'movie' | 'person' | 'album' | 'book' | 'unknown';

export type FilterableSubjectKind = Exclude<SubjectKind, 'unknown'>;

const kindLabels: Record<string, string> = {
    show: 'Show',
    movie: 'Movie',
    person: 'Person',
    album: 'Album',
    book: 'Book',
    unknown: 'Unknown',
};

const kindFilterLabels: Record<string, string> = {
    show: 'Shows',
    movie: 'Movies',
    person: 'People',
    album: 'Albums',
    book: 'Books',
};

function capitalize(value: string): string {
    return value.charAt(0).toUpperCase() + value.slice(1);
}

export function formatSubjectKindLabel(kind: string): string {
    return kindLabels[kind] ?? capitalize(kind);
}

export function formatSubjectKindFilterLabel(kind: string): string {
    if (!kind) return 'All Kinds';
    return kindFilterLabels[kind] ?? capitalize(kind);
}
