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
    [NETWORK_ERROR_STATUS]: 'Unable to connect to the server. Check your network connection.',
    [HTTP_STATUS.BAD_REQUEST]: 'Invalid request. Please check your input.',
    [HTTP_STATUS.UNAUTHORIZED]: 'Your session has expired. Please log in again.',
    [HTTP_STATUS.FORBIDDEN]: 'You do not have permission to perform this action.',
    [HTTP_STATUS.NOT_FOUND]: 'The requested resource was not found.',
    [HTTP_STATUS.CONFLICT]: 'A conflict occurred. The resource may have been modified.',
    [HTTP_STATUS.UNPROCESSABLE_ENTITY]: 'The request could not be processed. Please check your input.',
    [HTTP_STATUS.TOO_MANY_REQUESTS]: 'Too many requests. Please wait a moment and try again.',
    [HTTP_STATUS.INTERNAL_SERVER_ERROR]: 'An unexpected server error occurred. Please try again later.',
    [HTTP_STATUS.BAD_GATEWAY]: 'The server received an invalid response. Please try again later.',
    [HTTP_STATUS.SERVICE_UNAVAILABLE]: 'The service is temporarily unavailable. Please try again later.',
    [HTTP_STATUS.GATEWAY_TIMEOUT]: 'The server took too long to respond. Please try again later.',
};

export function isNetworkError(error: unknown): boolean {
    if (isApiError(error)) {
        return error.status === NETWORK_ERROR_STATUS;
    }
    if (error instanceof Error) {
        return (
            error.message.includes('NetworkError') ||
            error.message.includes('network') ||
            error.message.includes('Failed to fetch')
        );
    }
    return false;
}

export function isAuthError(error: unknown): boolean {
    return isApiError(error) && error.status === HTTP_STATUS.UNAUTHORIZED;
}

export function isPermissionError(error: unknown): boolean {
    return isApiError(error) && error.status === HTTP_STATUS.FORBIDDEN;
}

export function isNotFoundError(error: unknown): boolean {
    return isApiError(error) && error.status === HTTP_STATUS.NOT_FOUND;
}

export function isValidationError(error: unknown): boolean {
    if (!isApiError(error)) {
        return false;
    }
    return error.status === HTTP_STATUS.BAD_REQUEST || error.status === HTTP_STATUS.UNPROCESSABLE_ENTITY;
}

export function isServerError(error: unknown): boolean {
    return isApiError(error) && error.status >= 500 && error.status < 600;
}

export function isRetryableError(error: unknown): boolean {
    if (isNetworkError(error)) {
        return true;
    }
    if (isApiError(error)) {
        return error.status >= 500 || error.status === HTTP_STATUS.TOO_MANY_REQUESTS;
    }
    return false;
}

export function getErrorMessage(error: unknown, fallback = 'An unexpected error occurred.'): string {
    if (isApiError(error)) {
        const genericPrefixes = ['Request failed with status', 'HTTP error'];
        const isGenericMessage = genericPrefixes.some((prefix) => error.message.startsWith(prefix));

        if (!isGenericMessage && error.message) {
            return error.message;
        }

        return STATUS_MESSAGES[error.status] ?? `An error occurred (HTTP ${error.status}).`;
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

export interface ErrorDisplayInfo {
    message: string;
    retryable: boolean;
    status: number;
    validationErrors: Record<string, string[]> | null;
}

export function getErrorDisplayInfo(error: unknown): ErrorDisplayInfo {
    return {
        message: getErrorMessage(error),
        retryable: isRetryableError(error),
        status: isApiError(error) ? error.status : 0,
        validationErrors: getValidationErrors(error),
    };
}

export function createApiError(status: number, message: string, details?: unknown): ApiError {
    return new ApiError(status, message, details);
}
