import { afterEach, describe, expect, it, vi } from 'vitest';

import { parseMagpieQl, formatMagpieQlCriteria, magpieQlAnd, magpieQlString } from './magpieQl';

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

describe('magpieQl', () => {
    afterEach(() => {
        vi.clearAllMocks();
    });

    describe('parseMagpieQl', () => {
        it('returns criteria when parse succeeds', async () => {
            const mockCriteria = {
                contentHierarchyItem: {
                    and: [{ monitoring: { equals: { value: 1 } } }],
                },
            };

            mockApiFetch.mockResolvedValueOnce(mockCriteria);

            const result = await parseMagpieQl('monitoring = true', 'content');

            expect(result.criteria).toEqual(mockCriteria);
            expect(result.error).toBeUndefined();
            expect(mockApiFetch).toHaveBeenCalledWith('/magpie-ql/content/parse', {
                method: 'POST',
                body: { query: 'monitoring = true' },
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

            const result = await parseMagpieQl('invalid query here', 'content');

            expect(result.criteria).toBeUndefined();
            expect(result.error).toEqual(parseError);
        });

        it('re-throws non-parse ApiError', async () => {
            mockApiFetch.mockRejectedValueOnce({
                status: 500,
                message: 'Internal server error',
            });

            await expect(parseMagpieQl('test', 'content')).rejects.toEqual({
                status: 500,
                message: 'Internal server error',
            });
        });

        it('re-throws network errors', async () => {
            mockApiFetch.mockRejectedValueOnce(new Error('Network error'));

            await expect(parseMagpieQl('test', 'content')).rejects.toThrow('Network error');
        });

        it('uses correct endpoint for subject context', async () => {
            mockApiFetch.mockResolvedValueOnce({});

            await parseMagpieQl('kind = Movie', 'subject');

            expect(mockApiFetch).toHaveBeenCalledWith('/magpie-ql/subject/parse', expect.any(Object));
        });
    });

    describe('formatMagpieQlCriteria', () => {
        it('returns formatted query string', async () => {
            const mockResponse = { query: "monitoring = true and tag has 'HD'" };
            mockApiFetch.mockResolvedValueOnce(mockResponse);

            const criteria = {
                contentHierarchyItem: {
                    and: [{ monitoring: { equals: { value: 1 } } }, { tags: { has: { value: 'HD' } } }],
                },
            };

            const result = await formatMagpieQlCriteria(criteria, 'content');

            expect(result.query).toBe("monitoring = true and tag has 'HD'");
            expect(mockApiFetch).toHaveBeenCalledWith('/magpie-ql/content/format', {
                method: 'POST',
                body: criteria,
            });
        });

        it('uses correct endpoint for subject context', async () => {
            mockApiFetch.mockResolvedValueOnce({ query: 'kind = Movie' });

            await formatMagpieQlCriteria({ subject: { and: [] } }, 'subject');

            expect(mockApiFetch).toHaveBeenCalledWith('/magpie-ql/subject/format', expect.any(Object));
        });

        it('rethrows API errors', async () => {
            const error = new Error('Internal server error');
            mockApiFetch.mockRejectedValueOnce(error);

            await expect(formatMagpieQlCriteria({}, 'content')).rejects.toBe(error);
        });
    });
});

describe('magpieQlString', () => {
    it('quotes a plain value', () => {
        expect(magpieQlString('kind-x-scene')).toBe('"kind-x-scene"');
    });

    it('escapes embedded quotes and backslashes', () => {
        expect(magpieQlString('a"b\\c')).toBe('"a\\"b\\\\c"');
    });
});

describe('magpieQlAnd', () => {
    it('joins clauses with the and keyword', () => {
        expect(magpieQlAnd(['ParentId null', 'ContentType = "tv"'])).toBe('ParentId null and ContentType = "tv"');
    });

    it('drops blank clauses', () => {
        expect(magpieQlAnd(['ParentId null', '', '   '])).toBe('ParentId null');
    });
});
