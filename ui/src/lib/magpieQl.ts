import { apiFetch, isApiError } from '@/lib/api';
import { escapeQueryString } from '@/lib/simpleFilterQuery';

interface MagpieQlParseError {
    message: string;
    position: number;
    expected: string[];
}

interface MagpieQlParseResult {
    criteria?: unknown;
    error?: MagpieQlParseError;
}

interface MagpieQlFormatResult {
    query: string;
}

function isParseError(value: unknown): value is MagpieQlParseError {
    return typeof value === 'object' && value !== null && 'message' in value && 'position' in value;
}

export function magpieQlString(value: string): string {
    return `"${escapeQueryString(value)}"`;
}

export function magpieQlAnd(clauses: readonly string[]): string {
    return clauses.filter((clause) => clause.trim().length > 0).join(' and ');
}

export async function parseMagpieQl(query: string, context: 'content' | 'subject'): Promise<MagpieQlParseResult> {
    try {
        const criteria = await apiFetch<unknown>(`/magpie-ql/${context}/parse`, {
            method: 'POST',
            body: { query },
        });
        return { criteria };
    } catch (err) {
        if (isApiError(err) && err.details && isParseError(err.details)) {
            return { error: err.details };
        }
        throw err;
    }
}

export async function formatMagpieQlCriteria(
    criteria: unknown,
    context: 'content' | 'subject',
): Promise<MagpieQlFormatResult> {
    return apiFetch<MagpieQlFormatResult>(`/magpie-ql/${context}/format`, {
        method: 'POST',
        body: criteria,
    });
}
