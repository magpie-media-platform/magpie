import { isApiError } from './api';

const STATUS_MESSAGES: Record<number, string> = {
    0: 'Failed to connect to the server. Check the network connection.',
    400: 'The request is invalid. Check the input.',
    401: 'The session expired. Sign in again.',
    403: 'You do not have permission to perform this action.',
    404: 'The requested resource does not exist.',
    409: 'The request conflicts with the current resource.',
    422: 'The request is not valid. Check the input.',
    429: 'Too many requests. Wait, then try again.',
    500: 'The server request failed. Try again later.',
    502: 'The server received an invalid response. Try again later.',
    503: 'The service is temporarily unavailable. Try again later.',
    504: 'The server took too long to respond. Try again later.',
};

export function isNotFoundError(error: unknown): boolean {
    return isApiError(error) && error.status === 404;
}

export function isConflictError(error: unknown): boolean {
    return isApiError(error) && error.status === 409;
}

export function isValidationError(error: unknown): boolean {
    if (!isApiError(error)) {
        return false;
    }
    return error.status === 400 || error.status === 422;
}

export function getErrorMessage(error: unknown, fallback = 'The request failed.'): string {
    if (isApiError(error)) {
        const genericPrefixes = ['Request failed with status', 'HTTP error'];
        const isGenericMessage = genericPrefixes.some((prefix) => error.message.startsWith(prefix));

        if (!isGenericMessage && error.message) {
            return error.message;
        }

        return STATUS_MESSAGES[error.status] ?? `The request failed (HTTP ${error.status}).`;
    }

    if (error instanceof Error) {
        return error.message || fallback;
    }

    if (typeof error === 'string') {
        return error;
    }

    return fallback;
}

export function getValidationErrors(error: unknown): Record<string, string[]> | null {
    if (!isApiError(error) || !isValidationError(error)) {
        return null;
    }

    const details = error.details;
    if (!details || typeof details !== 'object') {
        return null;
    }

    const pd = details as Record<string, unknown>;
    if (pd.errors && typeof pd.errors === 'object') {
        const errors = pd.errors as Record<string, unknown>;
        const result: Record<string, string[]> = {};

        for (const [key, value] of Object.entries(errors)) {
            if (Array.isArray(value)) {
                result[key] = value.filter((v): v is string => typeof v === 'string');
            } else if (typeof value === 'string') {
                result[key] = [value];
            }
        }

        return Object.keys(result).length > 0 ? result : null;
    }

    return null;
}

export function getFieldError(error: unknown, fieldName: string): string | null {
    const errors = getValidationErrors(error);
    if (!errors) {
        return null;
    }

    if (errors[fieldName]?.[0]) {
        return errors[fieldName][0];
    }

    const lowerFieldName = fieldName.toLowerCase();
    for (const [key, messages] of Object.entries(errors)) {
        if (key.toLowerCase() === lowerFieldName && messages[0]) {
            return messages[0];
        }
    }

    return null;
}

export function candidateSearchErrorMessage(error: unknown): string {
    if (isApiError(error) && typeof error.details === 'object' && error.details !== null) {
        const details = error.details as { message?: unknown };
        if (typeof details.message === 'string') {
            return details.message;
        }
    }
    if (isApiError(error) && (error.status === 400 || error.status === 503)) {
        return getErrorMessage(error, 'Search failed. Please try again.');
    }
    return 'Search failed. Please try again.';
}
