import { integrationEnvironment } from './environment';

const DEFAULT_BASE_URL = integrationEnvironment.baseUrl;

export const ADMIN_USERNAME = integrationEnvironment.adminUsername;
export const ADMIN_PASSWORD = integrationEnvironment.adminPassword;

export const UI_TOKEN_STORAGE_KEY = 'magpie_auth_token';

export class ApiError extends Error {
    public readonly status: number;
    public readonly body: string;

    constructor(
        status: number,
        body: string,
        message: string,
    ) {
        super(message);
        this.name = 'ApiError';
        this.status = status;
        this.body = body;
    }
}

interface ApiClientOptions {
    baseURL?: string;
    accessToken?: string;
    apiKey?: string;
}

export class ApiClient {
    private readonly baseURL: string;
    private readonly apiKey: string | undefined;
    private accessToken: string | undefined;

    constructor(options: ApiClientOptions = {}) {
        this.baseURL = options.baseURL ?? DEFAULT_BASE_URL;
        this.apiKey = options.apiKey;
        this.accessToken = options.accessToken;
    }

    setAccessToken(token: string | undefined): void {
        this.accessToken = token;
    }

    async request<T>(method: string, path: string, body?: unknown): Promise<T> {
        const headers: Record<string, string> = { Accept: 'application/json' };
        if (body !== undefined) {
            headers['Content-Type'] = 'application/json';
        }
        if (this.accessToken) {
            headers['Authorization'] = `Bearer ${this.accessToken}`;
        }
        if (this.apiKey) {
            headers['X-Api-Key'] = this.apiKey;
        }

        const response = await fetch(`${this.baseURL}${path}`, {
            method,
            headers,
            body: body === undefined ? undefined : JSON.stringify(body),
        });

        if (!response.ok) {
            const text = await response.text();
            throw new ApiError(
                response.status,
                text,
                `${method} ${path} failed with ${response.status}: ${text.slice(0, 300)}`,
            );
        }

        if (response.status === 204) {
            return undefined as T;
        }

        const contentType = response.headers.get('content-type') ?? '';
        return contentType.includes('application/json')
            ? ((await response.json()) as T)
            : ((await response.text()) as unknown as T);
    }

    get<T>(path: string): Promise<T> {
        return this.request<T>('GET', path);
    }

    post<T>(path: string, body?: unknown): Promise<T> {
        return this.request<T>('POST', path, body);
    }

    put<T>(path: string, body?: unknown): Promise<T> {
        return this.request<T>('PUT', path, body);
    }

    patch<T>(path: string, body?: unknown): Promise<T> {
        return this.request<T>('PATCH', path, body);
    }

    delete<T>(path: string): Promise<T> {
        return this.request<T>('DELETE', path);
    }
}

interface LoginResponse {
    accessToken: string;
    accessTokenExpiresAt: string;
}

export async function loginAdmin(
    username: string,
    password: string,
    baseURL?: string,
): Promise<{ api: ApiClient; accessToken: string }> {
    const anon = new ApiClient(baseURL ? { baseURL } : {});
    const login = await anon.post<LoginResponse>('/api/auth/login', { username, password });
    return {
        api: new ApiClient({ baseURL, accessToken: login.accessToken }),
        accessToken: login.accessToken,
    };
}
