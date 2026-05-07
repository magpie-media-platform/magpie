import { describe, it, expect, vi, beforeEach } from 'vitest';
import { renderHook } from '@testing-library/react';

import { useApiError } from './useApiError';

describe('useApiError', () => {
    beforeEach(() => {
        vi.spyOn(console, 'error').mockImplementation(() => {});
    });

    describe('handleError', () => {
        it('returns error display info', () => {
            const { result } = renderHook(() => useApiError({ showToast: false }));

            const error = { name: 'ApiError', status: 404, message: 'Not found' };
            const info = result.current.handleError(error);

            expect(info.message).toBe('Not found');
            expect(info.status).toBe(404);
            expect(info.retryable).toBe(false);
        });

        it('calls onToast with error info', () => {
            const onToast = vi.fn();
            const { result } = renderHook(() => useApiError({ onToast }));

            const error = { name: 'ApiError', status: 500, message: 'Server error' };
            result.current.handleError(error);

            expect(onToast).toHaveBeenCalledWith({
                title: 'Error',
                description: 'Server error',
                variant: 'error',
            });
        });

        it('includes context in toast title', () => {
            const onToast = vi.fn();
            const { result } = renderHook(() => useApiError({ onToast }));

            const error = { name: 'ApiError', status: 400, message: 'Bad request' };
            result.current.handleError(error, 'saving user');

            expect(onToast).toHaveBeenCalledWith({
                title: 'Error saving user',
                description: 'Bad request',
                variant: 'error',
            });
        });

        it('uses warning variant for network errors', () => {
            const onToast = vi.fn();
            const { result } = renderHook(() => useApiError({ onToast }));

            const error = { name: 'ApiError', status: 0, message: 'Network error' };
            result.current.handleError(error);

            expect(onToast).toHaveBeenCalledWith(expect.objectContaining({ variant: 'warning' }));
        });

        it('skips toast for auth errors', () => {
            const onToast = vi.fn();
            const { result } = renderHook(() => useApiError({ onToast }));

            const error = { name: 'ApiError', status: 401, message: 'Unauthorized' };
            result.current.handleError(error);

            expect(onToast).not.toHaveBeenCalled();
        });

        it('does not show toast when showToast is false', () => {
            const onToast = vi.fn();
            const { result } = renderHook(() => useApiError({ onToast, showToast: false }));

            const error = { name: 'ApiError', status: 500, message: 'Error' };
            result.current.handleError(error);

            expect(onToast).not.toHaveBeenCalled();
        });

        it('logs error in development', () => {
            const consoleSpy = vi.spyOn(console, 'error');
            const { result } = renderHook(() => useApiError({ showToast: false, logErrors: true }));

            const error = { name: 'ApiError', status: 500, message: 'Error' };
            result.current.handleError(error, 'testing');

            expect(consoleSpy).toHaveBeenCalledWith('Error testing:', error);
        });

        it('does not log when logErrors is false', () => {
            const consoleSpy = vi.spyOn(console, 'error');
            const { result } = renderHook(() => useApiError({ showToast: false, logErrors: false }));

            result.current.handleError({ name: 'ApiError', status: 500, message: 'Error' });

            expect(consoleSpy).not.toHaveBeenCalled();
        });
    });

    describe('getError', () => {
        it('returns error message', () => {
            const { result } = renderHook(() => useApiError());

            const message = result.current.getError({ name: 'ApiError', status: 404, message: 'Not found' });
            expect(message).toBe('Not found');
        });

        it('returns fallback for unknown errors', () => {
            const { result } = renderHook(() => useApiError());

            const message = result.current.getError(null, 'Custom fallback');
            expect(message).toBe('Custom fallback');
        });

        it('handles Error objects', () => {
            const { result } = renderHook(() => useApiError());

            const message = result.current.getError(new Error('Something broke'));
            expect(message).toBe('Something broke');
        });
    });

    describe('onMutationError', () => {
        it('returns a callback function', () => {
            const { result } = renderHook(() => useApiError());

            const callback = result.current.onMutationError('creating user');
            expect(typeof callback).toBe('function');
        });

        it('callback handles errors with context', () => {
            const onToast = vi.fn();
            const { result } = renderHook(() => useApiError({ onToast }));

            const callback = result.current.onMutationError('creating user');
            callback({ name: 'ApiError', status: 400, message: 'Validation failed' });

            expect(onToast).toHaveBeenCalledWith({
                title: 'Error creating user',
                description: 'Validation failed',
                variant: 'error',
            });
        });

        it('callback works without context', () => {
            const onToast = vi.fn();
            const { result } = renderHook(() => useApiError({ onToast }));

            const callback = result.current.onMutationError();
            callback({ name: 'ApiError', status: 500, message: 'Server error' });

            expect(onToast).toHaveBeenCalledWith({
                title: 'Error',
                description: 'Server error',
                variant: 'error',
            });
        });
    });

    describe('stability', () => {
        it('handleError reference is stable', () => {
            const { result, rerender } = renderHook(() => useApiError());

            const firstRef = result.current.handleError;
            rerender();
            const secondRef = result.current.handleError;

            expect(firstRef).toBe(secondRef);
        });

        it('getError reference is stable', () => {
            const { result, rerender } = renderHook(() => useApiError());

            const firstRef = result.current.getError;
            rerender();
            const secondRef = result.current.getError;

            expect(firstRef).toBe(secondRef);
        });

        it('onMutationError reference is stable', () => {
            const { result, rerender } = renderHook(() => useApiError());

            const firstRef = result.current.onMutationError;
            rerender();
            const secondRef = result.current.onMutationError;

            expect(firstRef).toBe(secondRef);
        });
    });
});
