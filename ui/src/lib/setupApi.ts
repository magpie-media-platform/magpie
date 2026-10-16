import { apiFetch, apiPost } from './api';

export interface SetupStatus {
    isRequired: boolean;

    isComplete: boolean;

    requiresAdminClaim: boolean;
}

export interface CreateAdminRequest {
    username: string;

    password: string;
}

interface CreatedUser {
    id: string;
    username: string;
    email: string | null;
    role: number;
}

export async function fetchSetupStatus(): Promise<SetupStatus> {
    return apiFetch<SetupStatus>('/system/setup/status', { skipAuth: true });
}

export async function createSetupAdmin(request: CreateAdminRequest): Promise<CreatedUser> {
    return apiFetch<CreatedUser>('/system/setup/admin', {
        method: 'POST',
        body: request,
        skipAuth: true,
    });
}

interface SetupDatabaseStatus {
    initialized: boolean;
    provider: string;
    jobTypes?: Record<string, string>;
}

export type NodeRole = 'Ui' | 'Api' | 'JobRunner';

interface ConfigureSetupDatabaseRequest {
    provider: string;
    connectionString: string | null;
    roles?: NodeRole[];
    jobTypes?: string[];
}

interface ConfigureSetupDatabaseResponse {
    provider: string;
    restartRequired: boolean;
}

export async function fetchSetupDatabaseStatus(): Promise<SetupDatabaseStatus> {
    return apiFetch<SetupDatabaseStatus>('/system/setup/db-status', { skipAuth: true });
}

export async function configureSetupDatabase(
    request: ConfigureSetupDatabaseRequest,
): Promise<ConfigureSetupDatabaseResponse> {
    return apiPost<ConfigureSetupDatabaseResponse>('/system/setup/db', request, { skipAuth: true });
}

export async function completeSetup(): Promise<SetupStatus> {
    return apiPost<SetupStatus>('/system/setup/complete', undefined, { skipAuth: true });
}

export const setupStatusQueryKey = ['system', 'setup', 'status'] as const;
export const setupDatabaseStatusQueryKey = ['system', 'setup', 'db-status'] as const;
