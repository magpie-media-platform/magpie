import { describe, expect, it } from 'vitest';

import { ApiError } from './api';
import { retryUnlessNotFound } from './queryClient';

describe('retryUnlessNotFound', () => {
    it('retries the first failure of a server error', () => {
        expect(retryUnlessNotFound(0, new ApiError(500, 'The server request failed.'))).toBe(true);
    });

    it('stops retrying after the first retry', () => {
        expect(retryUnlessNotFound(1, new ApiError(500, 'The server request failed.'))).toBe(false);
    });

    it('does not retry a not-found error', () => {
        expect(retryUnlessNotFound(0, new ApiError(404, 'The requested resource does not exist.'))).toBe(false);
    });

    it('retries an error that is not an api error', () => {
        expect(retryUnlessNotFound(0, new Error('Failed to fetch'))).toBe(true);
    });
});
