import type { ContentFilterSelection } from './contentFilterOptions';
import { escapeQueryString, monitoringClause, parseMonitoringClause, unescapeQueryString } from './simpleFilterQuery';

type ContentFilterField = 'monitoring' | 'fulfillment' | 'title' | 'publishDateFrom' | 'publishDateTo';

type ClassifiedContentFilterField = ContentFilterField | 'contentType' | 'level';

interface Predicate {
    field: ClassifiedContentFilterField | null;
    raw: string;
    value?: string;
    grouped?: boolean;
}

const CONTENT_TYPE_RE = /^contentType\s*=\s*"((?:\\.|[^"\\])*)"$/i;
const LEVEL_RE = /^level\s*=\s*"((?:\\.|[^"\\])*)"$/i;
const FULFILLMENT_RE =
    /^fulfillment\s*=\s*(?:"(Missing|Partial|Upgradable|Fulfilled|FulfilledExternally)"|(Missing|Partial|Upgradable|Fulfilled|FulfilledExternally))$/i;
const TITLE_RE = /^title\s+contains\s+"((?:\\.|[^"\\])*)"$/i;
const PUBLISH_FROM_RE = /^publishDate\s*>=\s*"(\d{4}-\d{2}-\d{2})"$/i;
const PUBLISH_TO_RE = /^publishDate\s*<=\s*"(\d{4}-\d{2}-\d{2})"$/i;

function classify(clause: string): Predicate {
    const trimmed = clause.trim();

    const contentType = trimmed.match(CONTENT_TYPE_RE);
    if (contentType) {
        return { field: 'contentType', raw: trimmed, value: unescapeQueryString(contentType[1]) };
    }

    const level = trimmed.match(LEVEL_RE);
    if (level) {
        return { field: 'level', raw: trimmed, value: unescapeQueryString(level[1]) };
    }

    const monitoring = parseMonitoringClause(trimmed);
    if (monitoring) {
        return { field: 'monitoring', raw: trimmed, value: monitoring };
    }

    const fulfillment = trimmed.match(FULFILLMENT_RE);
    if (fulfillment) {
        const value = ['Missing', 'Partial', 'Upgradable', 'Fulfilled', 'FulfilledExternally'].find(
            (status) => status.toLowerCase() === (fulfillment[1] ?? fulfillment[2]).toLowerCase(),
        );
        return { field: 'fulfillment', raw: trimmed, value };
    }

    const title = trimmed.match(TITLE_RE);
    if (title) {
        return { field: 'title', raw: trimmed, value: unescapeQueryString(title[1]) };
    }

    const from = trimmed.match(PUBLISH_FROM_RE);
    if (from) {
        return { field: 'publishDateFrom', raw: trimmed, value: unescapeQueryString(from[1]) };
    }

    const to = trimmed.match(PUBLISH_TO_RE);
    if (to) {
        return { field: 'publishDateTo', raw: trimmed, value: unescapeQueryString(to[1]) };
    }

    return { field: null, raw: trimmed };
}

const AND_OR_DELIMITER = /^\s+(and|or)\s+/i;

export function splitClauses(query: string): Predicate[] {
    const trimmed = query.trim();
    if (!trimmed) {
        return [];
    }

    const clauses: string[] = [];
    let current = '';
    let depth = 0;
    let inString = false;
    let i = 0;

    while (i < trimmed.length) {
        const ch = trimmed[i];

        if (inString) {
            current += ch;
            if (ch === '\\' && i + 1 < trimmed.length) {
                current += trimmed[i + 1];
                i += 2;
                continue;
            }
            if (ch === '"') {
                inString = false;
            }
            i += 1;
            continue;
        }

        if (ch === '"') {
            inString = true;
            current += ch;
            i += 1;
            continue;
        }

        if (ch === '(') {
            depth += 1;
            current += ch;
            i += 1;
            continue;
        }

        if (ch === ')') {
            depth = Math.max(0, depth - 1);
            current += ch;
            i += 1;
            continue;
        }

        if (depth === 0 && /\s/.test(ch)) {
            const delimiter = trimmed.slice(i).match(AND_OR_DELIMITER);
            if (delimiter) {
                if (delimiter[1].toLowerCase() === 'or') {
                    return [{ field: null, raw: trimmed, grouped: true }];
                }
                clauses.push(current);
                current = '';
                i += delimiter[0].length;
                continue;
            }
        }

        current += ch;
        i += 1;
    }

    clauses.push(current);

    return clauses
        .map((c) => c.trim())
        .filter(Boolean)
        .map(classify);
}

function serializeClause(field: ClassifiedContentFilterField, value: string): string {
    const quoted = escapeQueryString(value);
    switch (field) {
        case 'contentType':
            return `contentType = "${quoted}"`;
        case 'level':
            return `level = "${quoted}"`;
        case 'monitoring':
            return monitoringClause(value);
        case 'fulfillment':
            return `fulfillment = ${value}`;
        case 'title':
            return `title contains "${quoted}"`;
        case 'publishDateFrom':
            return `publishDate >= "${quoted}"`;
        case 'publishDateTo':
            return `publishDate <= "${quoted}"`;
    }
}

export function readContentFilter(query: string, field: ContentFilterField): string {
    if (hasComplexContentFilter(query, field)) return '';
    const predicate = splitClauses(query).find((p) => p.field === field);
    return predicate?.value ?? '';
}

export function hasComplexContentFilter(query: string, field: ClassifiedContentFilterField): boolean {
    const predicates = splitClauses(query);
    const name = field.startsWith('publishDate') ? 'publishDate' : field;
    const fieldPattern = new RegExp(`\\b${name}\\s*(?:[=!<>]|!?[a-z]+\\b)`, 'i');
    return (
        predicates.filter((predicate) => predicate.field === field).length > 1 ||
        predicates.some(
            (predicate) => !predicate.field && fieldPattern.test(predicate.raw.replace(/"(?:\\.|[^"\\])*"/g, '""')),
        )
    );
}

function preservedClause(predicate: Predicate): string {
    return predicate.grouped ? `(${predicate.raw})` : predicate.raw;
}

export function setContentFilter(query: string, field: ContentFilterField, value: string): string {
    const predicates = splitClauses(query);
    const cleaned = value.trim();

    const kept = predicates.filter((p) => p.field !== field);
    const clauses = kept.map(preservedClause);

    if (cleaned) {
        clauses.push(serializeClause(field, cleaned));
    }

    return clauses.join(' and ');
}

export function readContentFilterSelection(query: string): ContentFilterSelection | null {
    if (hasComplexContentFilter(query, 'contentType') || hasComplexContentFilter(query, 'level')) return null;
    const predicates = splitClauses(query);
    const contentType = predicates.find((predicate) => predicate.field === 'contentType')?.value;
    if (!contentType) {
        return null;
    }

    const level = predicates.find((predicate) => predicate.field === 'level')?.value;
    return level ? { contentType, level } : { contentType };
}

export function setContentFilterSelection(query: string, selection: ContentFilterSelection | null): string {
    const predicates = splitClauses(query);
    const kept = predicates.filter((predicate) => predicate.field !== 'contentType' && predicate.field !== 'level');
    const clauses = kept.map(preservedClause);

    if (selection) {
        clauses.push(serializeClause('contentType', selection.contentType));
        if (selection.level !== undefined) {
            clauses.push(serializeClause('level', selection.level));
        }
    }

    return clauses.join(' and ');
}
