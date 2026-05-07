import { apiFetch } from './api';

export interface SetupStatus {
    isRequired: boolean;

    isComplete: boolean;
}

export interface CreateAdminRequest {
    username: string;

    password: string;
}

export interface CreatedUser {
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

export const setupStatusQueryKey = ['system', 'setup', 'status'] as const;
