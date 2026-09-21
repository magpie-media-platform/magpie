import type { Role } from '@/auth';
import type { BadgeVariant } from '@/types/badge';

export interface UserItem {
    id: string;
    username: string;
    email: string | null;
    extensionId: string | null;
    providerType: string;
    externalId: string | null;
    settings: Record<string, string> | null;
    enabled: boolean;
    role: Role;
    failedLoginAttempts: number;
    lockedUntil: string | null;
    lastLoginAt: string | null;
    createdAt: string;
    createdByUserId: string | null;
    modifiedAt: string | null;
    modifiedByUserId: string | null;
}

export interface UserCreate {
    username: string;
    password: string;
    role: Role;
    enabled: boolean;
}

export interface UserUpdate {
    role?: Role;
    enabled?: boolean;
}

export interface ApiKeyItem {
    id: string;
    userId: string;
    name: string;
    keyPrefix: string;
    enabled: boolean;
    expiresAt: string | null;
    lastUsedAt: string | null;
    createdAt: string;
    createdByUserId: string | null;
    modifiedAt: string | null;
    modifiedByUserId: string | null;
}

export interface ApiKeyCreatedItem {
    key: ApiKeyItem;
    plaintextKey: string;
}

export interface ApiKeyCreate {
    name: string;
    expiresAt?: string | null;
}

export const roleVariants: Record<Role, BadgeVariant> = {
    0: 'default',
    1: 'info',
    2: 'warning',
    3: 'primary',
};
