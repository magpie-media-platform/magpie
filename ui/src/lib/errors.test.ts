import { describe, it, expect } from 'vitest';

import {
    isNetworkError,
    isAuthError,
    isNotFoundError,
    isValidationError,
    isServerError,
    isRetryableError,
    isNoDownloadClientError,
    getErrorMessage,
    getValidationErrors,
    getFieldError,
    getErrorDisplayInfo,
    createApiError,
    HTTP_STATUS,
    NETWORK_ERROR_STATUS,
} from './errors';

describe('Error type checks', () => {
    describe('isNetworkError', () => {
        it('returns true for ApiError with status 0', () => {
            expect(isNetworkError({ name: 'ApiError', status: 0, message: 'Network error' })).toBe(true);
        });

        it('returns false for ApiError with non-zero status', () => {
            expect(isNetworkError({ name: 'ApiError', status: 500, message: 'Server error' })).toBe(false);
        });

        it('returns true for Error with network-related message', () => {
            expect(isNetworkError(new Error('NetworkError: failed to fetch'))).toBe(true);
            expect(isNetworkError(new Error('network request failed'))).toBe(true);
            expect(isNetworkError(new Error('Failed to fetch'))).toBe(true);
        });

        it('returns false for Error with non-network message', () => {
            expect(isNetworkError(new Error('Something went wrong'))).toBe(false);
        });

        it('returns false for non-error values', () => {
            expect(isNetworkError(null)).toBe(false);
            expect(isNetworkError(undefined)).toBe(false);
            expect(isNetworkError('error')).toBe(false);
        });
    });

    describe('isAuthError', () => {
        it('returns true for 401 status', () => {
            expect(isAuthError({ name: 'ApiError', status: 401, message: 'Unauthorized' })).toBe(true);
        });

        it('returns false for other statuses', () => {
            expect(isAuthError({ name: 'ApiError', status: 403, message: 'Forbidden' })).toBe(false);
            expect(isAuthError({ name: 'ApiError', status: 500, message: 'Error' })).toBe(false);
        });

        it('returns false for non-ApiError', () => {
            expect(isAuthError(new Error('Unauthorized'))).toBe(false);
        });
    });

    describe('isNotFoundError', () => {
        it('returns true for 404 status', () => {
            expect(isNotFoundError({ name: 'ApiError', status: 404, message: 'Not found' })).toBe(true);
        });

        it('returns false for other statuses', () => {
            expect(isNotFoundError({ name: 'ApiError', status: 500, message: 'Error' })).toBe(false);
        });
    });

    describe('isValidationError', () => {
        it('returns true for 400 status', () => {
            expect(isValidationError({ name: 'ApiError', status: 400, message: 'Bad request' })).toBe(true);
        });

        it('returns true for 422 status', () => {
            expect(isValidationError({ name: 'ApiError', status: 422, message: 'Unprocessable' })).toBe(true);
        });

        it('returns false for other statuses', () => {
            expect(isValidationError({ name: 'ApiError', status: 500, message: 'Error' })).toBe(false);
        });
    });

    describe('isServerError', () => {
        it('returns true for 5xx statuses', () => {
            expect(isServerError({ name: 'ApiError', status: 500, message: 'Error' })).toBe(true);
            expect(isServerError({ name: 'ApiError', status: 502, message: 'Bad gateway' })).toBe(true);
            expect(isServerError({ name: 'ApiError', status: 503, message: 'Unavailable' })).toBe(true);
        });

        it('returns false for non-5xx statuses', () => {
            expect(isServerError({ name: 'ApiError', status: 400, message: 'Bad request' })).toBe(false);
            expect(isServerError({ name: 'ApiError', status: 404, message: 'Not found' })).toBe(false);
        });
    });

    describe('isRetryableError', () => {
        it('returns true for network errors', () => {
            expect(isRetryableError({ name: 'ApiError', status: 0, message: 'Network error' })).toBe(true);
        });

        it('returns true for server errors', () => {
            expect(isRetryableError({ name: 'ApiError', status: 500, message: 'Error' })).toBe(true);
            expect(isRetryableError({ name: 'ApiError', status: 503, message: 'Unavailable' })).toBe(true);
        });

        it('returns true for rate limiting', () => {
            expect(isRetryableError({ name: 'ApiError', status: 429, message: 'Too many requests' })).toBe(true);
        });

        it('returns false for client errors', () => {
            expect(isRetryableError({ name: 'ApiError', status: 400, message: 'Bad request' })).toBe(false);
            expect(isRetryableError({ name: 'ApiError', status: 404, message: 'Not found' })).toBe(false);
        });
    });

    describe('isNoDownloadClientError', () => {
        it('returns true when the message mentions a download client', () => {
            expect(
                isNoDownloadClientError({ name: 'ApiError', status: 400, message: 'No download client configured' }),
            ).toBe(true);
            expect(isNoDownloadClientError(new Error('The Download Client is missing'))).toBe(true);
        });

        it('returns false for unrelated errors', () => {
            expect(isNoDownloadClientError({ name: 'ApiError', status: 500, message: 'Server error' })).toBe(false);
            expect(isNoDownloadClientError(null)).toBe(false);
        });
    });
});

describe('getErrorMessage', () => {
    it('returns API error message when informative', () => {
        const error = { name: 'ApiError', status: 400, message: 'Email already exists' };
        expect(getErrorMessage(error)).toBe('Email already exists');
    });

    it('returns status-based message for generic API error message', () => {
        const error = { name: 'ApiError', status: 404, message: 'Request failed with status 404' };
        expect(getErrorMessage(error)).toBe('The requested resource does not exist.');
    });

    it('returns status-based message for network error with generic message', () => {
        const error = { name: 'ApiError', status: 0, message: 'Request failed with status 0' };
        expect(getErrorMessage(error)).toBe('Failed to connect to the server. Check the network connection.');
    });

    it('returns API error message for network error with specific message', () => {
        const error = { name: 'ApiError', status: 0, message: 'Network error' };
        expect(getErrorMessage(error)).toBe('Network error');
    });

    it('returns Error message', () => {
        expect(getErrorMessage(new Error('Something broke'))).toBe('Something broke');
    });

    it('returns string error as-is', () => {
        expect(getErrorMessage('Custom error')).toBe('Custom error');
    });

    it('returns fallback for unknown error types', () => {
        expect(getErrorMessage(null)).toBe('The request failed.');
        expect(getErrorMessage(undefined)).toBe('The request failed.');
        expect(getErrorMessage({})).toBe('The request failed.');
    });

    it('uses custom fallback', () => {
        expect(getErrorMessage(null, 'Custom fallback')).toBe('Custom fallback');
    });

    it('returns status message for unknown status codes', () => {
        const error = { name: 'ApiError', status: 418, message: "I'm a teapot" };
        expect(getErrorMessage(error)).toBe("I'm a teapot");
    });
});

describe('getValidationErrors', () => {
    it('extracts errors from ProblemDetails format', () => {
        const error = {
            name: 'ApiError',
            status: 400,
            message: 'Validation failed',
            details: {
                errors: {
                    email: ['Email is required', 'Email is invalid'],
                    password: ['Password is too short'],
                },
            },
        };
        expect(getValidationErrors(error)).toEqual({
            email: ['Email is required', 'Email is invalid'],
            password: ['Password is too short'],
        });
    });

    it('handles single error strings', () => {
        const error = {
            name: 'ApiError',
            status: 400,
            message: 'Validation failed',
            details: {
                errors: {
                    name: 'Name is required',
                },
            },
        };
        expect(getValidationErrors(error)).toEqual({
            name: ['Name is required'],
        });
    });

    it('returns null for non-validation errors', () => {
        const error = { name: 'ApiError', status: 500, message: 'Server error' };
        expect(getValidationErrors(error)).toBeNull();
    });

    it('returns null when no errors object', () => {
        const error = { name: 'ApiError', status: 400, message: 'Bad request', details: {} };
        expect(getValidationErrors(error)).toBeNull();
    });

    it('returns null for non-ApiError', () => {
        expect(getValidationErrors(new Error('Error'))).toBeNull();
    });

    it('filters out non-string values', () => {
        const error = {
            name: 'ApiError',
            status: 400,
            message: 'Validation failed',
            details: {
                errors: {
                    field1: ['valid error'],
                    field2: [123, 'valid error'],
                },
            },
        };
        const result = getValidationErrors(error);
        expect(result).toEqual({
            field1: ['valid error'],
            field2: ['valid error'],
        });
    });
});

describe('getFieldError', () => {
    it('returns first error for exact field match', () => {
        const error = {
            name: 'ApiError',
            status: 400,
            message: 'Validation failed',
            details: {
                errors: {
                    email: ['Email is required', 'Email is invalid'],
                },
            },
        };
        expect(getFieldError(error, 'email')).toBe('Email is required');
    });

    it('returns null for non-existent field', () => {
        const error = {
            name: 'ApiError',
            status: 400,
            message: 'Validation failed',
            details: {
                errors: {
                    email: ['Email is required'],
                },
            },
        };
        expect(getFieldError(error, 'password')).toBeNull();
    });

    it('handles case-insensitive matching', () => {
        const error = {
            name: 'ApiError',
            status: 400,
            message: 'Validation failed',
            details: {
                errors: {
                    Email: ['Email is required'],
                },
            },
        };
        expect(getFieldError(error, 'email')).toBe('Email is required');
    });

    it('returns null for non-validation errors', () => {
        expect(getFieldError({ name: 'ApiError', status: 500, message: 'Error' }, 'field')).toBeNull();
    });
});

describe('getErrorDisplayInfo', () => {
    it('returns complete error information', () => {
        const error = {
            name: 'ApiError',
            status: 400,
            message: 'Validation failed',
            details: {
                errors: {
                    email: ['Email is required'],
                },
            },
        };
        const info = getErrorDisplayInfo(error);
        expect(info).toEqual({
            message: 'Validation failed',
            retryable: false,
            status: 400,
            validationErrors: { email: ['Email is required'] },
        });
    });

    it('marks network errors as retryable', () => {
        const error = { name: 'ApiError', status: 0, message: 'Network error' };
        const info = getErrorDisplayInfo(error);
        expect(info.retryable).toBe(true);
        expect(info.status).toBe(0);
    });

    it('marks server errors as retryable', () => {
        const error = { name: 'ApiError', status: 500, message: 'Server error' };
        const info = getErrorDisplayInfo(error);
        expect(info.retryable).toBe(true);
    });

    it('handles non-ApiError', () => {
        const info = getErrorDisplayInfo(new Error('Regular error'));
        expect(info).toEqual({
            message: 'Regular error',
            retryable: false,
            status: 0,
            validationErrors: null,
        });
    });
});

describe('createApiError', () => {
    it('creates ApiError with required fields', () => {
        const error = createApiError(404, 'Not found');
        expect(error).toBeInstanceOf(Error);
        expect(error.status).toBe(404);
        expect(error.message).toBe('Not found');
        expect(error.details).toBeUndefined();
    });

    it('creates ApiError with details', () => {
        const details = { field: 'value' };
        const error = createApiError(400, 'Bad request', details);
        expect(error).toBeInstanceOf(Error);
        expect(error.status).toBe(400);
        expect(error.message).toBe('Bad request');
        expect(error.details).toEqual({ field: 'value' });
    });
});

describe('Constants', () => {
    it('NETWORK_ERROR_STATUS triggers isNetworkError', () => {
        const error = createApiError(NETWORK_ERROR_STATUS, 'Network error');
        expect(isNetworkError(error)).toBe(true);
    });

    it('HTTP_STATUS.UNAUTHORIZED triggers isAuthError', () => {
        const error = createApiError(HTTP_STATUS.UNAUTHORIZED, 'Unauthorized');
        expect(isAuthError(error)).toBe(true);
    });

    it('HTTP_STATUS.NOT_FOUND triggers isNotFoundError', () => {
        const error = createApiError(HTTP_STATUS.NOT_FOUND, 'Not found');
        expect(isNotFoundError(error)).toBe(true);
    });

    it('HTTP_STATUS.BAD_REQUEST triggers isValidationError', () => {
        const error = createApiError(HTTP_STATUS.BAD_REQUEST, 'Bad request');
        expect(isValidationError(error)).toBe(true);
    });

    it('HTTP_STATUS.INTERNAL_SERVER_ERROR triggers isServerError', () => {
        const error = createApiError(HTTP_STATUS.INTERNAL_SERVER_ERROR, 'Server error');
        expect(isServerError(error)).toBe(true);
    });
});
