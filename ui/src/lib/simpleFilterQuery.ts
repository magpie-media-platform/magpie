export type YearOp = 'before' | 'in' | 'after';

export const YEAR_OPS: YearOp[] = ['before', 'in', 'after'];

export interface FieldState {
    contentType: string;
    level: string;
    monitoring: string;
    genre: string;
    tag: string;
    title: string;
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
    genre: '',
    tag: '',
    title: '',
    kind: '',
    relationshipKind: '',
    relationshipName: '',
    releaseYear: '',
    yearOps: [...DEFAULT_YEAR_OPS],
};

export function escapeQueryString(value: string): string {
    return value.replace(/\\/g, '\\\\').replace(/"/g, '\\"');
}

function unescapeQueryString(value: string): string {
    return value.replace(/\\(.)/g, '$1');
}

function relationshipClause(kind: string, name: string): string {
    const parts: string[] = [];
    if (name) {
        parts.push(`Name contains "${escapeQueryString(name)}"`);
    }
    if (kind) {
        parts.push(`Type = "${escapeQueryString(kind)}"`);
    }
    return `Relationships has (${parts.join(' and ')})`;
}

const RELATIONSHIP_CLAUSE_PATTERN =
    /Relationships\s+has\s+\(\s*((?:Name|Subject)\s+contains\s+"(?:\\.|[^"\\])*"|Type\s*=\s*"(?:\\.|[^"\\])*")(?:\s+and\s+((?:Name|Subject)\s+contains\s+"(?:\\.|[^"\\])*"|Type\s*=\s*"(?:\\.|[^"\\])*"))?\s*\)/i;

function parseRelationshipClause(clause: string): Pick<FieldState, 'relationshipKind' | 'relationshipName'> | null {
    const state = { relationshipKind: '', relationshipName: '' };
    const parts = clause
        .trim()
        .split(/\s+and\s+/i)
        .map((part) => part.trim())
        .filter(Boolean);
    if (parts.length === 0) {
        return null;
    }
    for (const part of parts) {
        const nameMatch = part.match(/^(?:Name|Subject)\s+contains\s+"((?:\\.|[^"\\])*)"$/i);
        if (nameMatch) {
            state.relationshipName = unescapeQueryString(nameMatch[1]);
            continue;
        }
        const typeMatch = part.match(/^Type\s*=\s*"((?:\\.|[^"\\])*)"$/i);
        if (typeMatch) {
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

const RELEASE_YEAR_CLAUSE_PATTERN = new RegExp(
    [
        'publishDate\\s*(?:!=|<=|>=|=|<|>)\\s*"\\d{1,4}"',
        '\\(\\s*publishDate\\s*>=\\s*"\\d{1,4}-01-01"\\s+and\\s+publishDate\\s*<=\\s*"\\d{1,4}-12-31"\\s*\\)',
        '\\(\\s*publishDate\\s*<\\s*"\\d{1,4}-01-01"\\s+or\\s+publishDate\\s*>\\s*"\\d{1,4}-12-31"\\s*\\)',
        'publishDate\\s*(?:>=|<=|<|>)\\s*"\\d{1,4}-(?:01-01|12-31)"',
    ].join('|'),
    'i',
);

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
            clauses.push(`contentType = "${fields.contentType}"`);
        }
        if (fields.level) {
            clauses.push(`level = "${fields.level}"`);
        }
        if (fields.monitoring) {
            clauses.push(`monitoring = ${fields.monitoring}`);
        }
    } else {
        if (fields.kind) {
            clauses.push(`kind = "${fields.kind}"`);
        }
    }

    if (fields.title) {
        clauses.push(`title contains "${fields.title}"`);
    }
    if (fields.genre && context === 'content') {
        clauses.push(`genre has "${fields.genre}"`);
    }
    if (fields.tag) {
        clauses.push(`tag has "${fields.tag}"`);
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

    let scalarQuery = query;
    const relationshipMatch = query.match(RELATIONSHIP_CLAUSE_PATTERN);
    if (relationshipMatch) {
        const relationship = parseRelationshipClause(
            relationshipMatch[0].replace(/^Relationships\s+has\s+\(\s*/i, '').replace(/\s*\)\s*$/i, ''),
        );
        if (!relationship) {
            return null;
        }
        state.relationshipName = relationship.relationshipName;
        state.relationshipKind = relationship.relationshipKind;
        scalarQuery = scalarQuery
            .replace(RELATIONSHIP_CLAUSE_PATTERN, '')
            .replace(/\s+and\s+and\s+/i, ' and ')
            .replace(/^\s*and\s+|\s+and\s+$/i, '');
    }

    if (context === 'content') {
        const yearMatch = scalarQuery.match(RELEASE_YEAR_CLAUSE_PATTERN);
        if (yearMatch) {
            const parsed = parseReleaseYearClause(yearMatch[0]);
            if (!parsed) {
                return null;
            }
            const ops = OPERATOR_TO_YEAR_OPS[parsed.operator];
            if (!ops) {
                return null;
            }
            state.releaseYear = parsed.year;
            state.yearOps = [...ops];
            scalarQuery = scalarQuery
                .replace(RELEASE_YEAR_CLAUSE_PATTERN, '')
                .replace(/\s+and\s+and\s+/i, ' and ')
                .replace(/^\s*and\s+|\s+and\s+$/i, '');
        }
    }

    if (!scalarQuery.trim()) {
        return state;
    }

    const parts = scalarQuery.split(/\s+and\s+/i);

    for (const part of parts) {
        const trimmed = part.trim();

        const contentTypeMatch = trimmed.match(/^contentType\s*=\s*"([^"]*)"$/i);
        if (contentTypeMatch) {
            state.contentType = contentTypeMatch[1];
            continue;
        }

        const levelMatch = trimmed.match(/^level\s*=\s*"([^"]*)"$/i);
        if (levelMatch) {
            state.level = levelMatch[1];
            continue;
        }

        const monitoringMatch = trimmed.match(/^monitoring\s*=\s*(true|false)$/i);
        if (monitoringMatch) {
            state.monitoring = monitoringMatch[1].toLowerCase();
            continue;
        }

        const kindMatch = trimmed.match(/^kind\s*=\s*"([^"]*)"$/i);
        if (kindMatch) {
            state.kind = kindMatch[1];
            continue;
        }

        const titleMatch = trimmed.match(/^title\s+contains\s+"([^"]*)"$/i);
        if (titleMatch) {
            state.title = titleMatch[1];
            continue;
        }

        const genreMatch = trimmed.match(/^genre\s+has\s+"([^"]*)"$/i);
        if (genreMatch && context === 'content') {
            state.genre = genreMatch[1];
            continue;
        }

        const tagMatch = trimmed.match(/^tag\s+has\s+"([^"]*)"$/i);
        if (tagMatch) {
            state.tag = tagMatch[1];
            continue;
        }

        return null;
    }

    return state;
}

export function isSimpleQuery(query: string, context: 'content' | 'subject'): boolean {
    if (!query.trim()) {
        return true;
    }
    return parseQuery(query, context) !== null;
}
