import { describe, it, expect } from 'vitest';

import { ApiError } from './api';
import {
    isNotFoundError,
    isConflictError,
    isValidationError,
    getErrorMessage,
    candidateSearchErrorMessage,
    createApiError,
    getValidationErrors,
    getFieldError,
    HTTP_STATUS,
} from './errors';

describe('Error type checks', () => {
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

    describe('isConflictError', () => {
        it.each([
            { status: 409, expected: true },
            { status: 400, expected: false },
            { status: 404, expected: false },
        ])('returns $expected for status $status', ({ status, expected }) => {
            // Arrange
            const error = { name: 'ApiError', status, message: 'Conflict' };
            // Act
            const actual = isConflictError(error);
            // Assert
            expect(actual).toBe(expected);
        });
    });

    describe('download client failure messages', () => {
        it.each([
            {
                error: { name: 'ApiError', status: 400, message: 'No download client configured' },
                message: 'No download client configured',
            },
            { error: new Error('The download client timed out'), message: 'The download client timed out' },
            { error: { name: 'ApiError', status: 500, message: 'Server error' }, message: 'Server error' },
        ])('preserves the reported failure: $message', ({ error, message }) => {
            // Arrange
            // Act
            const actual = getErrorMessage(error);
            // Assert
            expect(actual).toBe(message);
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

describe('Constants', () => {
    it('HTTP_STATUS.NOT_FOUND triggers isNotFoundError', () => {
        expect(isNotFoundError(new ApiError(HTTP_STATUS.NOT_FOUND, 'Not found'))).toBe(true);
    });

    it('HTTP_STATUS.BAD_REQUEST triggers isValidationError', () => {
        expect(isValidationError(new ApiError(HTTP_STATUS.BAD_REQUEST, 'Bad request'))).toBe(true);
    });
});

describe('candidateSearchErrorMessage', () => {
    it.each([
        [createApiError(400, 'Bad query', { message: 'Expected a value.', position: 3 }), 'Expected a value.'],
        [createApiError(400, 'Unknown search field.'), 'Unknown search field.'],
        [createApiError(500, 'Internal details'), 'Search failed. Please try again.'],
        [
            createApiError(503, 'The provider is busy. Try again later.', {
                status: 503,
                detail: 'The provider is busy. Try again later.',
            }),
            'The provider is busy. Try again later.',
        ],
        [new Error('Connection lost'), 'Search failed. Please try again.'],
    ])('SearchError_ReturnsSafeMessage_WhenSearchFails %s', (error, expected) => {
        // Act
        const message = candidateSearchErrorMessage(error);

        // Assert
        expect(message).toBe(expected);
    });
});
