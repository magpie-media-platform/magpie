import { afterEach, describe, expect, it, vi } from 'vitest';

import { parseFilterQuery, formatFilterCriteria } from './filterQuery';

vi.mock('@/lib/api', () => ({
    apiFetch: vi.fn(),
    isApiError: (value: unknown): value is { status: number; message: string; details?: unknown } =>
        typeof value === 'object' &&
        value !== null &&
        'status' in value &&
        'message' in value &&
        typeof (value as Record<string, unknown>).status === 'number' &&
        typeof (value as Record<string, unknown>).message === 'string',
}));

import { apiFetch } from '@/lib/api';

const mockApiFetch = vi.mocked(apiFetch);

describe('filterQuery', () => {
    afterEach(() => {
        vi.clearAllMocks();
    });

    describe('parseFilterQuery', () => {
        it('returns criteria when parse succeeds', async () => {
            const mockCriteria = {
                contentHierarchyItem: {
                    and: [{ monitoring: { equals: { value: 1 } } }],
                },
            };

            mockApiFetch.mockResolvedValueOnce(mockCriteria);

            const result = await parseFilterQuery('monitoring = Active', 'content');

            expect(result.criteria).toEqual(mockCriteria);
            expect(result.error).toBeUndefined();
            expect(mockApiFetch).toHaveBeenCalledWith('/filter-query/content/parse', {
                method: 'POST',
                body: { query: 'monitoring = Active' },
            });
        });

        it('returns error when parse fails with ApiError', async () => {
            const parseError = {
                message: 'Unexpected token',
                position: 15,
                expected: ['operator', 'and', 'or'],
            };

            mockApiFetch.mockRejectedValueOnce({
                status: 400,
                message: 'Parse error',
                details: parseError,
            });

            const result = await parseFilterQuery('invalid query here', 'content');

            expect(result.criteria).toBeUndefined();
            expect(result.error).toEqual(parseError);
        });

        it('re-throws non-parse ApiError', async () => {
            mockApiFetch.mockRejectedValueOnce({
                status: 500,
                message: 'Internal server error',
            });

            await expect(parseFilterQuery('test', 'content')).rejects.toEqual({
                status: 500,
                message: 'Internal server error',
            });
        });

        it('re-throws network errors', async () => {
            mockApiFetch.mockRejectedValueOnce(new Error('Network error'));

            await expect(parseFilterQuery('test', 'content')).rejects.toThrow('Network error');
        });

        it('uses correct endpoint for subject context', async () => {
            mockApiFetch.mockResolvedValueOnce({});

            await parseFilterQuery('kind = Movie', 'subject');

            expect(mockApiFetch).toHaveBeenCalledWith('/filter-query/subject/parse', expect.any(Object));
        });
    });

    describe('formatFilterCriteria', () => {
        it('returns formatted query string', async () => {
            const mockResponse = { query: "monitoring = Active and tag has 'HD'" };
            mockApiFetch.mockResolvedValueOnce(mockResponse);

            const criteria = {
                contentHierarchyItem: {
                    and: [{ monitoring: { equals: { value: 1 } } }, { tags: { has: { value: 'HD' } } }],
                },
            };

            const result = await formatFilterCriteria(criteria, 'content');

            expect(result.query).toBe("monitoring = Active and tag has 'HD'");
            expect(mockApiFetch).toHaveBeenCalledWith('/filter-query/content/format', {
                method: 'POST',
                body: criteria,
            });
        });

        it('uses correct endpoint for subject context', async () => {
            mockApiFetch.mockResolvedValueOnce({ query: 'kind = Movie' });

            await formatFilterCriteria({ subject: { and: [] } }, 'subject');

            expect(mockApiFetch).toHaveBeenCalledWith('/filter-query/subject/format', expect.any(Object));
        });

        it('returns empty query on API error', async () => {
            mockApiFetch.mockRejectedValueOnce({
                status: 500,
                message: 'Internal server error',
            });

            const result = await formatFilterCriteria({}, 'content');
            expect(result).toEqual({ query: '' });
        });
    });
});
