import { apiFetch, isApiError } from '@/lib/api';

export interface FilterQueryParseError {
    message: string;
    position: number;
    expected: string[];
}

export interface FilterQueryParseResult {
    criteria?: unknown;
    error?: FilterQueryParseError;
}

export interface FilterQueryFormatResult {
    query: string;
}

function isParseError(value: unknown): value is FilterQueryParseError {
    return typeof value === 'object' && value !== null && 'message' in value && 'position' in value;
}

export async function parseFilterQuery(query: string, context: 'content' | 'subject'): Promise<FilterQueryParseResult> {
    try {
        const criteria = await apiFetch<unknown>(`/filter-query/${context}/parse`, {
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

export async function formatFilterCriteria(
    criteria: unknown,
    context: 'content' | 'subject',
): Promise<FilterQueryFormatResult> {
    try {
        return await apiFetch<FilterQueryFormatResult>(`/filter-query/${context}/format`, {
            method: 'POST',
            body: criteria,
        });
    } catch {
        return { query: '' };
    }
}
