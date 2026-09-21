import type { ApiIndexer } from '@/hooks/useIndexers';

export interface QueryMode {
    key: string;

    label: string;

    fields: string[];
}

function normalizeFields(fields: readonly string[]): string[] {
    return [
        ...new Set(
            fields
                .map((field) => field.toLowerCase().replace(/[^a-z0-9]/g, ''))
                .map((field) => (field.length > 2 && field.endsWith('id') ? field.slice(0, -2) : field))
                .filter(Boolean),
        ),
    ].sort();
}

const FIELD_DISPLAY_NAMES: Record<string, string> = {
    q: 'Keyword',
    imdb: 'IMDb',
    tmdb: 'TMDB',
    tvdb: 'TVDB',
    tvmaze: 'TVmaze',
};

export function fieldLabel(field: string): string {
    return FIELD_DISPLAY_NAMES[field] ?? field.charAt(0).toUpperCase() + field.slice(1);
}

function indexerModes(indexer: ApiIndexer): Map<string, string[]> {
    const modes = new Map<string, string[]>();
    if (indexer.supportsKeywordSearch) {
        modes.set('q', ['q']);
    }
    for (const facetSet of indexer.searchFacets ?? []) {
        const fields = normalizeFields(facetSet);
        if (fields.length === 0 || (fields.includes('q') && !indexer.supportsKeywordSearch)) {
            continue;
        }
        modes.set(fields.join(','), fields);
    }
    return modes;
}

/** Lists the complete query modes that at least one selected indexer supports. */
export function availableQueryModes(indexers: readonly ApiIndexer[]): QueryMode[] {
    if (indexers.length === 0) {
        return [];
    }

    const modes = new Map(indexers.flatMap((indexer) => [...indexerModes(indexer)]));
    return [...modes.entries()]
        .map(([key, fields]) => ({ key, fields, label: fields.map(fieldLabel).join(' + ') }))
        .sort((left, right) => left.fields.length - right.fields.length || left.key.localeCompare(right.key));
}
