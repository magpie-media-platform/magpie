import { clearAuthStorage, refreshAccessToken } from './authApi';

const API_BASE = import.meta.env.VITE_API_URL || '/api';

const TOKEN_KEY = 'magpie_auth_token';

const REFRESH_ENDPOINT = '/auth/refresh';

let refreshInFlight: Promise<void> | null = null;

const isRefreshEndpoint = (path: string): boolean => {
    const cleaned = path.split('?')[0].split('#')[0].replace(/\/+$/, '');
    return cleaned.endsWith(REFRESH_ENDPOINT);
};

const AUTH_FAILURE_DISPATCHED = Symbol('magpie.api.authFailureDispatched');

type AuthFailureMarked = { [AUTH_FAILURE_DISPATCHED]?: true };

function markAuthFailureDispatched(error: object): void {
    Object.defineProperty(error, AUTH_FAILURE_DISPATCHED, {
        value: true,
        enumerable: false,
        configurable: false,
        writable: false,
    });
}

function isAuthFailureDispatched(error: unknown): boolean {
    return (
        typeof error === 'object' && error !== null && (error as AuthFailureMarked)[AUTH_FAILURE_DISPATCHED] === true
    );
}

async function ensureRefresh(): Promise<void> {
    if (refreshInFlight) {
        return refreshInFlight;
    }

    refreshInFlight = (async () => {
        try {
            await refreshAccessToken();
        } finally {
            refreshInFlight = null;
        }
    })();

    return refreshInFlight;
}

export const tokenStorage = {
    get(): string | null {
        try {
            return localStorage.getItem(TOKEN_KEY);
        } catch {
            return null;
        }
    },

    set(token: string): void {
        try {
            localStorage.setItem(TOKEN_KEY, token);
        } catch {
            void 0;
        }
    },

    remove(): void {
        try {
            localStorage.removeItem(TOKEN_KEY);
        } catch {
            void 0;
        }
    },

    exists(): boolean {
        return this.get() !== null;
    },
};

export class ApiError extends Error {
    readonly status: number;

    readonly details?: unknown;

    constructor(status: number, message: string, details?: unknown) {
        super(message);
        this.name = 'ApiError';
        this.status = status;
        this.details = details;
    }
}

export function isApiError(value: unknown): value is ApiError {
    if (value instanceof ApiError) {
        return true;
    }
    return (
        typeof value === 'object' &&
        value !== null &&
        'name' in value &&
        (value as ApiError).name === 'ApiError' &&
        'status' in value &&
        'message' in value &&
        typeof (value as ApiError).status === 'number' &&
        typeof (value as ApiError).message === 'string'
    );
}

function hasAbortName(value: unknown): boolean {
    return (
        typeof value === 'object' &&
        value !== null &&
        'name' in value &&
        (value as { name?: unknown }).name === 'AbortError'
    );
}

export function isAbortError(error: unknown): boolean {
    if (hasAbortName(error)) {
        return true;
    }
    return isApiError(error) && hasAbortName(error.details);
}

export const AUTH_FAILURE_EVENT = 'magpie:auth:failure';

function dispatchAuthFailure(): void {
    window.dispatchEvent(new CustomEvent(AUTH_FAILURE_EVENT));
}

interface ApiRequestOptions extends Omit<RequestInit, 'body'> {
    body?: unknown;

    skipAuth?: boolean;

    baseUrl?: string;
}

async function parseErrorBody(response: Response): Promise<unknown> {
    const contentType = response.headers.get('content-type');
    if (contentType?.includes('json')) {
        try {
            return await response.json();
        } catch {
            return null;
        }
    }
    try {
        const text = await response.text();
        return text || null;
    } catch {
        return null;
    }
}

async function errorFromResponse(response: Response): Promise<ApiError> {
    const details = await parseErrorBody(response);

    let message = `Request failed with status ${response.status}`;
    if (details && typeof details === 'object') {
        const pd = details as Record<string, unknown>;
        if (typeof pd.title === 'string') {
            message = pd.title;
        } else if (typeof pd.message === 'string') {
            message = pd.message;
        } else if (typeof pd.detail === 'string') {
            message = pd.detail;
        }
    } else if (typeof details === 'string' && details) {
        message = details;
    }

    return new ApiError(response.status, message, details);
}

function buildRequest(
    path: string,
    options: ApiRequestOptions,
): {
    url: string;
    init: RequestInit;
    skipAuth: boolean;
    sentToken: string | null;
} {
    const { body, skipAuth = false, baseUrl = API_BASE, ...fetchOptions } = options;

    const headers = new Headers(fetchOptions.headers);
    const isFormData = typeof FormData !== 'undefined' && body instanceof FormData;

    if (body !== undefined && !isFormData && !headers.has('Content-Type')) {
        headers.set('Content-Type', 'application/json');
    }

    let sentToken: string | null = null;
    if (!skipAuth && !headers.has('Authorization')) {
        const token = tokenStorage.get();
        if (token) {
            headers.set('Authorization', `Bearer ${token}`);
            sentToken = token;
        }
    }

    const url = path.startsWith('http') ? path : `${baseUrl}${path}`;
    const init: RequestInit = {
        credentials: 'include',
        ...fetchOptions,
        headers,
    };

    if (body !== undefined) {
        if (isFormData || typeof body === 'string') {
            init.body = body as BodyInit;
        } else {
            init.body = JSON.stringify(body);
        }
    }

    return { url, init, skipAuth, sentToken };
}

export async function apiFetchResponse(path: string, options?: ApiRequestOptions): Promise<Response>;
export async function apiFetchResponse(path: string, options: ApiRequestOptions = {}): Promise<Response> {
    const callerHeaders = new Headers(options.headers);
    const callerProvidedAuth = callerHeaders.has('Authorization');

    let { url, init, skipAuth, sentToken } = buildRequest(path, options);

    let response: Response;
    try {
        response = await fetch(url, init);
    } catch (error) {
        throw new ApiError(0, error instanceof Error ? error.message : 'Network error', error);
    }

    if (response.status === 401) {
        const isRefresh = isRefreshEndpoint(path);
        const managedAuth = !skipAuth && !callerProvidedAuth && !isRefresh;

        const currentToken = tokenStorage.get();
        if (managedAuth && currentToken && currentToken !== sentToken) {
            ({ url, init, skipAuth, sentToken } = buildRequest(path, options));
            try {
                response = await fetch(url, init);
            } catch (error) {
                throw new ApiError(0, error instanceof Error ? error.message : 'Network error', error);
            }
            if (response.ok) {
                return response;
            }
        }

        if (managedAuth && sentToken === null && response.status === 401) {
            throw await errorFromResponse(response);
        }

        const canRetry = managedAuth && response.status === 401;

        if (canRetry) {
            try {
                await ensureRefresh();
            } catch (error) {
                if (isAuthFailureDispatched(error)) {
                    throw error;
                }
                clearAuthStorage();
                dispatchAuthFailure();
                throw await errorFromResponse(response);
            }

            ({ url, init, skipAuth } = buildRequest(path, options));
            try {
                response = await fetch(url, init);
            } catch (error) {
                throw new ApiError(0, error instanceof Error ? error.message : 'Network error', error);
            }

            if (response.status === 401) {
                clearAuthStorage();
                dispatchAuthFailure();
                throw await errorFromResponse(response);
            }
        } else if (response.status === 401) {
            clearAuthStorage();
            dispatchAuthFailure();
            const apiError = await errorFromResponse(response);
            if (isRefresh) {
                markAuthFailureDispatched(apiError);
            }
            throw apiError;
        }
    }

    if (!response.ok) {
        throw await errorFromResponse(response);
    }

    return response;
}

export async function apiFetch<T = void>(path: string, options?: ApiRequestOptions): Promise<T>;
export async function apiFetch<T = void>(path: string, options: ApiRequestOptions = {}): Promise<T | undefined> {
    const response = await apiFetchResponse(path, options);

    if (response.status === 204) {
        return undefined;
    }

    const contentType = response.headers.get('content-type');
    if (contentType?.includes('application/json')) {
        return response.json();
    }

    const text = await response.text();
    if (!text) {
        return undefined;
    }
    throw new ApiError(
        response.status,
        `Expected JSON response but received ${contentType ?? 'unknown content type'}`,
        text,
    );
}

export function apiGet<T>(path: string, options?: Omit<ApiRequestOptions, 'method' | 'body'>): Promise<T> {
    return apiFetch<T>(path, { ...options, method: 'GET' });
}

export function apiPost<T = void>(
    path: string,
    body?: unknown,
    options?: Omit<ApiRequestOptions, 'method' | 'body'>,
): Promise<T> {
    return apiFetch<T>(path, { ...options, method: 'POST', body });
}

export function apiPut<T = void>(
    path: string,
    body?: unknown,
    options?: Omit<ApiRequestOptions, 'method' | 'body'>,
): Promise<T> {
    return apiFetch<T>(path, { ...options, method: 'PUT', body });
}

export function apiPatch<T = void>(
    path: string,
    body?: unknown,
    options?: Omit<ApiRequestOptions, 'method' | 'body'>,
): Promise<T> {
    return apiFetch<T>(path, { ...options, method: 'PATCH', body });
}

export function apiDelete<T = void>(path: string, options?: Omit<ApiRequestOptions, 'method'>): Promise<T> {
    return apiFetch<T>(path, { ...options, method: 'DELETE' });
}

export function buildUrl(path: string, params?: object): string {
    if (!params) {
        return path;
    }

    const searchParams = new URLSearchParams();
    for (const [key, value] of Object.entries(params)) {
        if (value === undefined || value === null) {
            continue;
        }
        if (Array.isArray(value)) {
            for (const element of value) {
                if (element !== undefined && element !== null) {
                    searchParams.append(key, String(element));
                }
            }
        } else {
            searchParams.set(key, String(value));
        }
    }

    const queryString = searchParams.toString();
    if (!queryString) {
        return path;
    }

    return `${path}${path.includes('?') ? '&' : '?'}${queryString}`;
}
