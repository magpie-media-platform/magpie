import { ApiError, isApiError } from './api';

export const NETWORK_ERROR_STATUS = 0;

export const HTTP_STATUS = {
    BAD_REQUEST: 400,
    UNAUTHORIZED: 401,
    FORBIDDEN: 403,
    NOT_FOUND: 404,
    CONFLICT: 409,
    UNPROCESSABLE_ENTITY: 422,
    TOO_MANY_REQUESTS: 429,
    INTERNAL_SERVER_ERROR: 500,
    BAD_GATEWAY: 502,
    SERVICE_UNAVAILABLE: 503,
    GATEWAY_TIMEOUT: 504,
} as const;

const STATUS_MESSAGES: Record<number, string> = {
    [NETWORK_ERROR_STATUS]: 'Failed to connect to the server. Check the network connection.',
    [HTTP_STATUS.BAD_REQUEST]: 'The request is invalid. Check the input.',
    [HTTP_STATUS.UNAUTHORIZED]: 'The session expired. Sign in again.',
    [HTTP_STATUS.FORBIDDEN]: 'You do not have permission to perform this action.',
    [HTTP_STATUS.NOT_FOUND]: 'The requested resource does not exist.',
    [HTTP_STATUS.CONFLICT]: 'The request conflicts with the current resource.',
    [HTTP_STATUS.UNPROCESSABLE_ENTITY]: 'The request is not valid. Check the input.',
    [HTTP_STATUS.TOO_MANY_REQUESTS]: 'Too many requests. Wait, then try again.',
    [HTTP_STATUS.INTERNAL_SERVER_ERROR]: 'The server request failed. Try again later.',
    [HTTP_STATUS.BAD_GATEWAY]: 'The server received an invalid response. Try again later.',
    [HTTP_STATUS.SERVICE_UNAVAILABLE]: 'The service is temporarily unavailable. Try again later.',
    [HTTP_STATUS.GATEWAY_TIMEOUT]: 'The server took too long to respond. Try again later.',
};

export function isNotFoundError(error: unknown): boolean {
    return isApiError(error) && error.status === HTTP_STATUS.NOT_FOUND;
}

export function isConflictError(error: unknown): boolean {
    return isApiError(error) && error.status === HTTP_STATUS.CONFLICT;
}

export function isValidationError(error: unknown): boolean {
    if (!isApiError(error)) {
        return false;
    }
    return error.status === HTTP_STATUS.BAD_REQUEST || error.status === HTTP_STATUS.UNPROCESSABLE_ENTITY;
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

export function createApiError(status: number, message: string, details?: unknown): ApiError {
    return new ApiError(status, message, details);
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
