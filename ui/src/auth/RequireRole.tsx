import type { ReactNode } from 'react';

import { useAuth } from './useAuth';
import { Role, RoleName } from './types';

export interface RequireRoleProps {
    min: Role;

    children: ReactNode;

    fallback?: ReactNode;
}

export function RequireRole({ min, children, fallback = null }: RequireRoleProps) {
    const { hasRole } = useAuth();

    if (!hasRole(min)) {
        return <>{fallback}</>;
    }

    return <>{children}</>;
}

RequireRole.displayName = 'RequireRole';

export function useRequireRole(min: Role): { allowed: boolean; requiredRoleName: string } {
    const { hasRole } = useAuth();

    return {
        allowed: hasRole(min),
        requiredRoleName: RoleName[min],
    };
}
