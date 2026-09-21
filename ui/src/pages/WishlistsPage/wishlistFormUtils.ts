const CONTENT_TYPE_EQUALS = /\bcontentType\s*=\s*"([^"]*)"/i;
const CONTENT_TYPE_IN = /\bcontentType\s+in\s*\(\s*"([^"]*)"/i;

export function getQueryContentType(query: string): string {
    const equals = query.match(CONTENT_TYPE_EQUALS);
    if (equals) {
        return equals[1];
    }
    const inList = query.match(CONTENT_TYPE_IN);
    if (inList) {
        return inList[1];
    }
    return '';
}

export function setQueryContentType(query: string, slug: string): string {
    const remainder = stripContentTypeClause(query);
    if (!slug) {
        return remainder;
    }
    const clause = `contentType = "${slug}"`;
    if (!remainder) {
        return clause;
    }
    return `${clause} and ${needsGrouping(remainder) ? `(${remainder})` : remainder}`;
}

function needsGrouping(remainder: string): boolean {
    if (isSingleGroup(remainder)) {
        return false;
    }
    return /\bor\b/i.test(remainder);
}

function isSingleGroup(remainder: string): boolean {
    if (!remainder.startsWith('(') || !remainder.endsWith(')')) {
        return false;
    }
    let depth = 0;
    for (let i = 0; i < remainder.length; i++) {
        if (remainder[i] === '(') {
            depth++;
        } else if (remainder[i] === ')') {
            depth--;
            if (depth === 0 && i < remainder.length - 1) {
                return false;
            }
        }
    }
    return depth === 0;
}

function stripContentTypeClause(query: string): string {
    const parts = query
        .split(/\s+and\s+/i)
        .map((part) => part.trim())
        .filter((part) => part.length > 0 && !CONTENT_TYPE_EQUALS.test(part) && !CONTENT_TYPE_IN.test(part));
    return parts.join(' and ');
}

export function resolveFilterContentType(criteria: unknown): string | undefined {
    if (!criteria || typeof criteria !== 'object') {
        return undefined;
    }
    const chi = (criteria as { contentHierarchyItem?: { and?: unknown[] } }).contentHierarchyItem;
    for (const entry of chi?.and ?? []) {
        const contentType = (entry as { contentType?: { and?: unknown[] } } | null)?.contentType;
        for (const c of contentType?.and ?? []) {
            const crit = c as { operator?: string; value?: string; values?: string[] } | null;
            if (!crit) {
                continue;
            }
            if (crit.operator === 'Equals' && crit.value) {
                return crit.value;
            }
            if (crit.operator === 'In') {
                const first = crit.values?.find((v) => !!v);
                if (first) {
                    return first;
                }
            }
        }
    }
    return undefined;
}
