import { splitTopLevelAndClauses } from './queryClauses';

export type YearOp = 'before' | 'in' | 'after';

export const YEAR_OPS: YearOp[] = ['before', 'in', 'after'];

export interface FieldState {
    contentType: string;
    level: string;
    monitoring: string;
    fulfillment: string;
    genre: string;
    tag: string;
    title: string;
    titlePrefix: string;
    kind: string;
    relationshipKind: string;
    relationshipName: string;
    releaseYear: string;
    yearOps: YearOp[];
}

const DEFAULT_YEAR_OPS: YearOp[] = ['in'];

export const EMPTY_FIELDS: FieldState = {
    contentType: '',
    level: '',
    monitoring: '',
    fulfillment: '',
    genre: '',
    tag: '',
    title: '',
    titlePrefix: '',
    kind: '',
    relationshipKind: '',
    relationshipName: '',
    releaseYear: '',
    yearOps: [...DEFAULT_YEAR_OPS],
};

export function escapeQueryString(value: string): string {
    return value.replace(/\\/g, '\\\\').replace(/"/g, '\\"');
}

export function unescapeQueryString(value: string): string {
    return value.replace(/\\(.)/g, '$1');
}

export function monitoringClause(value: string): string {
    if (value === 'shotgun') return 'monitoring = Shotgun';
    return value === 'true' ? 'monitoring != Unmonitored' : 'monitoring = Unmonitored';
}

export function parseMonitoringClause(clause: string): string | null {
    const match = clause.match(/^monitoring\s*(!?=)\s*(Unmonitored|Shotgun)$/i);
    if (!match) return null;
    if (match[2].toLowerCase() === 'shotgun') return match[1] === '=' ? 'shotgun' : null;
    return match[1] === '=' ? 'false' : 'true';
}

/** The MagpieQL relationship clause for a subject kind and name, in the shape the simple filter bar parses back. */
export function relationshipClause(kind: string, name: string): string {
    const parts: string[] = [];
    if (name) {
        parts.push(`Name contains "${escapeQueryString(name)}"`);
    }
    if (kind) {
        parts.push(`Type = "${escapeQueryString(kind)}"`);
    }
    return `Relationships has (${parts.join(' and ')})`;
}

function parseRelationshipClause(clause: string): Pick<FieldState, 'relationshipKind' | 'relationshipName'> | null {
    const state = { relationshipKind: '', relationshipName: '' };
    const fields = new Set<string>();
    const parts = splitTopLevelAndClauses(clause);
    if (!parts?.length) return null;
    for (const part of parts) {
        const nameMatch = part.match(/^(?:Name|Subject)\s+contains\s+"((?:\\.|[^"\\])*)"$/i);
        if (nameMatch) {
            if (fields.has('name') || !nameMatch[1]) return null;
            fields.add('name');
            state.relationshipName = unescapeQueryString(nameMatch[1]);
            continue;
        }
        const typeMatch = part.match(/^Type\s*=\s*"((?:\\.|[^"\\])*)"$/i);
        if (typeMatch) {
            if (fields.has('type') || !typeMatch[1]) return null;
            fields.add('type');
            state.relationshipKind = unescapeQueryString(typeMatch[1]);
            continue;
        }
        return null;
    }
    return state.relationshipKind || state.relationshipName ? state : null;
}

export function normalizeYearOps(ops: YearOp[]): YearOp[] {
    return YEAR_OPS.filter((op) => ops.includes(op));
}

function yearOperator(ops: YearOp[]): string | null {
    const before = ops.includes('before');
    const inYear = ops.includes('in');
    const after = ops.includes('after');

    if (before && inYear && after) {
        return null;
    }
    if (before && inYear) {
        return '<=';
    }
    if (inYear && after) {
        return '>=';
    }
    if (before && after) {
        return '!=';
    }
    if (before) {
        return '<';
    }
    if (inYear) {
        return '=';
    }
    if (after) {
        return '>';
    }
    return null;
}

const OPERATOR_TO_YEAR_OPS: Record<string, YearOp[]> = {
    '=': ['in'],
    '<': ['before'],
    '>': ['after'],
    '<=': ['before', 'in'],
    '>=': ['in', 'after'],
    '!=': ['before', 'after'],
};

function releaseYearClause(year: string, ops: YearOp[]): string | null {
    const trimmed = year.trim();
    if (!trimmed) {
        return null;
    }
    const operator = yearOperator(ops);
    if (!operator) {
        return null;
    }
    return `publishDate ${operator} "${trimmed}"`;
}

function parseReleaseYearClause(clause: string): { operator: string; year: string } | null {
    const trimmed = clause.trim();

    const canonicalMatch = trimmed.match(/^publishDate\s*(!=|<=|>=|=|<|>)\s*"(\d{1,4})"$/i);
    if (canonicalMatch) {
        return { operator: canonicalMatch[1], year: canonicalMatch[2] };
    }

    const inMatch = trimmed.match(
        /^\(\s*publishDate\s*>=\s*"(\d{1,4})-01-01"\s+and\s+publishDate\s*<=\s*"\1-12-31"\s*\)$/i,
    );
    if (inMatch) {
        return { operator: '=', year: inMatch[1] };
    }

    const notMatch = trimmed.match(
        /^\(\s*publishDate\s*<\s*"(\d{1,4})-01-01"\s+or\s+publishDate\s*>\s*"\1-12-31"\s*\)$/i,
    );
    if (notMatch) {
        return { operator: '!=', year: notMatch[1] };
    }

    const afterMatch = trimmed.match(/^publishDate\s*>\s*"(\d{1,4})-12-31"$/i);
    if (afterMatch) {
        return { operator: '>', year: afterMatch[1] };
    }

    const onBeforeMatch = trimmed.match(/^publishDate\s*<=\s*"(\d{1,4})-12-31"$/i);
    if (onBeforeMatch) {
        return { operator: '<=', year: onBeforeMatch[1] };
    }

    const beforeMatch = trimmed.match(/^publishDate\s*<\s*"(\d{1,4})-01-01"$/i);
    if (beforeMatch) {
        return { operator: '<', year: beforeMatch[1] };
    }

    const onAfterMatch = trimmed.match(/^publishDate\s*>=\s*"(\d{1,4})-01-01"$/i);
    if (onAfterMatch) {
        return { operator: '>=', year: onAfterMatch[1] };
    }

    return null;
}

export function buildQuery(fields: FieldState, context: 'content' | 'subject'): string {
    const clauses: string[] = [];

    if (context === 'content') {
        if (fields.contentType) {
            clauses.push(`contentType = "${escapeQueryString(fields.contentType)}"`);
        }
        if (fields.level) {
            clauses.push(`level = "${escapeQueryString(fields.level)}"`);
        }
        if (fields.monitoring) {
            clauses.push(monitoringClause(fields.monitoring));
        }
        if (fields.fulfillment) {
            clauses.push(`fulfillment = ${fields.fulfillment}`);
        }
    } else {
        if (fields.kind) {
            clauses.push(`kind = "${escapeQueryString(fields.kind)}"`);
        }
    }

    if (fields.title) {
        clauses.push(`title contains "${escapeQueryString(fields.title)}"`);
    }
    if (fields.titlePrefix) {
        clauses.push(`title startswith "${escapeQueryString(fields.titlePrefix)}"`);
    }
    if (fields.genre && context === 'content') {
        clauses.push(`genre has "${escapeQueryString(fields.genre)}"`);
    }
    if (fields.tag) {
        clauses.push(`tag has "${escapeQueryString(fields.tag)}"`);
    }
    if (context === 'content') {
        const yearClause = releaseYearClause(fields.releaseYear, fields.yearOps);
        if (yearClause) {
            clauses.push(yearClause);
        }
    }
    if (fields.relationshipName || fields.relationshipKind) {
        clauses.push(relationshipClause(fields.relationshipKind, fields.relationshipName));
    }

    return clauses.join(' and ');
}

export function parseQuery(query: string, context: 'content' | 'subject'): FieldState | null {
    const state: FieldState = { ...EMPTY_FIELDS, yearOps: [...DEFAULT_YEAR_OPS] };

    if (!query.trim()) {
        return state;
    }

    const parts = splitTopLevelAndClauses(query);
    if (!parts) return null;

    for (const trimmed of parts) {
        const relationshipMatch = trimmed.match(/^Relationships\s+has\s+\(([\s\S]*)\)$/i);
        if (relationshipMatch) {
            if (state.relationshipName || state.relationshipKind) return null;
            const relationship = parseRelationshipClause(relationshipMatch[1]);
            if (!relationship) return null;
            state.relationshipName = relationship.relationshipName;
            state.relationshipKind = relationship.relationshipKind;
            continue;
        }

        const year = context === 'content' ? parseReleaseYearClause(trimmed) : null;
        if (year) {
            if (state.releaseYear) return null;
            state.releaseYear = year.year;
            state.yearOps = [...OPERATOR_TO_YEAR_OPS[year.operator]];
            continue;
        }

        const contentTypeMatch = trimmed.match(/^contentType\s*=\s*"((?:\\.|[^"\\])*)"$/i);
        if (contentTypeMatch && context === 'content') {
            if (state.contentType || !contentTypeMatch[1]) return null;
            state.contentType = unescapeQueryString(contentTypeMatch[1]);
            continue;
        }

        const levelMatch = trimmed.match(/^level\s*=\s*"((?:\\.|[^"\\])*)"$/i);
        if (levelMatch && context === 'content') {
            if (state.level || !levelMatch[1]) return null;
            state.level = unescapeQueryString(levelMatch[1]);
            continue;
        }

        const monitoring = parseMonitoringClause(trimmed);
        if (monitoring && context === 'content') {
            if (state.monitoring) return null;
            state.monitoring = monitoring;
            continue;
        }

        const fulfillmentMatch = trimmed.match(
            /^fulfillment\s*=\s*(?:"(Missing|Partial|Upgradable|Fulfilled|FulfilledExternally)"|(Missing|Partial|Upgradable|Fulfilled|FulfilledExternally))$/i,
        );
        if (fulfillmentMatch && context === 'content') {
            if (state.fulfillment) return null;
            state.fulfillment = fulfillmentMatch[1] ?? fulfillmentMatch[2];
            continue;
        }

        const kindMatch = trimmed.match(/^kind\s*=\s*"((?:\\.|[^"\\])*)"$/i);
        if (kindMatch && context === 'subject') {
            if (state.kind || !kindMatch[1]) return null;
            state.kind = unescapeQueryString(kindMatch[1]);
            continue;
        }

        const titleMatch = trimmed.match(/^title\s+contains\s+"((?:\\.|[^"\\])*)"$/i);
        if (titleMatch) {
            if (state.title || !titleMatch[1]) return null;
            state.title = unescapeQueryString(titleMatch[1]);
            continue;
        }

        const titlePrefixMatch = trimmed.match(/^title\s+startswith\s+"((?:\\.|[^"\\])*)"$/i);
        if (titlePrefixMatch) {
            if (state.titlePrefix || !titlePrefixMatch[1]) return null;
            state.titlePrefix = unescapeQueryString(titlePrefixMatch[1]);
            continue;
        }

        const genreMatch = trimmed.match(/^genre\s+has\s+"((?:\\.|[^"\\])*)"$/i);
        if (genreMatch && context === 'content') {
            if (state.genre || !genreMatch[1]) return null;
            state.genre = unescapeQueryString(genreMatch[1]);
            continue;
        }

        const tagMatch = trimmed.match(/^tag\s+has\s+"((?:\\.|[^"\\])*)"$/i);
        if (tagMatch) {
            if (state.tag || !tagMatch[1]) return null;
            state.tag = unescapeQueryString(tagMatch[1]);
            continue;
        }

        return null;
    }

    return state;
}

export function isSimpleQuery(
    query: string,
    context: 'content' | 'subject',
    options: { allowTitlePrefix?: boolean } = {},
): boolean {
    if (!query.trim()) {
        return true;
    }
    const fields = parseQuery(query, context);
    return fields !== null && (options.allowTitlePrefix === true || !fields.titlePrefix);
}
