import { authScope, clearAuthStorage, sessionGeneration, storeAuthTokens, tokenStorage } from './tokenStorage';
import { withAuthSessionLock } from './authSessionLock';

const API_BASE = import.meta.env.VITE_API_URL || '/api';

const REFRESH_ENDPOINT = '/auth/refresh';

const refreshInFlight = new Map<string, Promise<string>>();

const isRefreshEndpoint = (path: string): boolean => {
    const cleaned = path.split('?')[0].split('#')[0].replace(/\/+$/, '');
    return cleaned.endsWith(REFRESH_ENDPOINT);
};

export interface AuthTokenResponse {
    accessToken: string;

    accessTokenExpiresAt: string;
}

export function renewAccessToken(baseUrl?: string): Promise<string> {
    const generation = sessionGeneration(baseUrl);
    const scope = authScope(baseUrl);
    const key = `${scope}:${generation}`;
    const existing = refreshInFlight.get(key);
    if (existing) return existing;

    const previousToken = tokenStorage.get(baseUrl);
    if (!previousToken) return Promise.reject(new ApiError(401, 'No stored session is available.'));
    const renewal = withAuthSessionLock(scope, async (signal) => {
        if (sessionGeneration(baseUrl) !== generation) {
            throw new ApiError(409, 'The session changed while renewal was pending.');
        }
        const currentToken = tokenStorage.get(baseUrl);
        if (currentToken && currentToken !== previousToken) return currentToken;
        let renewalToken = previousToken;
        try {
            for (let attempt = 0; attempt < 2; attempt++) {
                const response = await apiFetch<AuthTokenResponse>(REFRESH_ENDPOINT, {
                    baseUrl,
                    method: 'POST',
                    skipAuth: true,
                    signal,
                    // The previous bearer survives a reload and identifies retries of this rotation.
                    headers: { Authorization: `Bearer ${renewalToken}` },
                });
                signal.throwIfAborted();
                if (sessionGeneration(baseUrl) !== generation) {
                    throw new ApiError(409, 'The session changed while renewal was pending.');
                }
                storeAuthTokens(response.accessToken, response.accessTokenExpiresAt, baseUrl);
                renewalToken = response.accessToken;
                if (new Date(response.accessTokenExpiresAt).getTime() > Date.now()) {
                    window.dispatchEvent(new CustomEvent(AUTH_RENEWED_EVENT, { detail: { scope } }));
                    return response.accessToken;
                }
            }
            throw new ApiError(503, 'Session renewal has not returned a current access token.');
        } catch (error) {
            if (sessionGeneration(baseUrl) === generation) {
                const newerToken = tokenStorage.get(baseUrl);
                if (newerToken && newerToken !== renewalToken) return newerToken;
                if (isApiError(error) && (error.status === 401 || error.status === 403)) {
                    invalidateSession(generation, renewalToken, baseUrl);
                }
            }
            throw error;
        }
    }).finally(() => refreshInFlight.delete(key));
    refreshInFlight.set(key, renewal);
    return renewal;
}

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
export const AUTH_RENEWED_EVENT = 'magpie:auth:renewed';

function invalidateSession(generation: string, token: string | null, baseUrl?: string): void {
    if (sessionGeneration(baseUrl) !== generation || tokenStorage.get(baseUrl) !== token) return;
    clearAuthStorage(baseUrl);
    if (authScope(baseUrl) === authScope()) window.dispatchEvent(new CustomEvent(AUTH_FAILURE_EVENT));
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
        if (typeof pd.detail === 'string' && pd.detail) {
            message = pd.detail;
        } else if (typeof pd.message === 'string' && pd.message) {
            message = pd.message;
        } else if (typeof pd.title === 'string' && pd.title) {
            message = pd.title;
        }
    } else if (typeof details === 'string' && details) {
        message = details;
    }

    return new ApiError(response.status, message, details);
}

function isSameOrigin(url: string, baseUrl: string): boolean {
    try {
        return new URL(url, window.location.href).origin === new URL(baseUrl, window.location.href).origin;
    } catch {
        return false;
    }
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

    const url = path.startsWith('http') ? path : `${baseUrl}${path}`;
    const apiOrigin = isSameOrigin(url, baseUrl);

    let sentToken: string | null = null;
    if (!skipAuth && apiOrigin && !headers.has('Authorization')) {
        const token = tokenStorage.get(baseUrl);
        if (token) {
            headers.set('Authorization', `Bearer ${token}`);
            sentToken = token;
        }
    }

    const init: RequestInit = {
        credentials: apiOrigin ? 'include' : 'omit',
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
    const isAuthFailure = (response: Response) => response.status === 401;
    const callerHeaders = new Headers(options.headers);
    const callerProvidedAuth = callerHeaders.has('Authorization');
    const generation = sessionGeneration(options.baseUrl);

    let { url, init, skipAuth, sentToken } = buildRequest(path, options);

    let response: Response;
    try {
        response = await fetch(url, init);
    } catch (error) {
        throw new ApiError(0, error instanceof Error ? error.message : 'Network error', error);
    }

    if (isAuthFailure(response)) {
        const isRefresh = isRefreshEndpoint(path);
        const managedAuth = !skipAuth && !callerProvidedAuth && !isRefresh;

        if (sessionGeneration(options.baseUrl) !== generation) throw await errorFromResponse(response);
        const currentToken = tokenStorage.get(options.baseUrl);
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

        if (managedAuth && sentToken === null && isAuthFailure(response)) {
            throw await errorFromResponse(response);
        }

        const canRetry = managedAuth && isAuthFailure(response);

        if (canRetry) {
            await renewAccessToken(options.baseUrl);
            if (sessionGeneration(options.baseUrl) !== generation) throw await errorFromResponse(response);

            ({ url, init, skipAuth, sentToken } = buildRequest(path, options));
            try {
                response = await fetch(url, init);
            } catch (error) {
                throw new ApiError(0, error instanceof Error ? error.message : 'Network error', error);
            }

            if (isAuthFailure(response)) {
                const apiError = await errorFromResponse(response);
                invalidateSession(generation, sentToken, options.baseUrl);
                throw apiError;
            }
        } else if (isAuthFailure(response)) {
            throw await errorFromResponse(response);
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
