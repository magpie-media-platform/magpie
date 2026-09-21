import type { ReactNode } from 'react';

import { useAuth } from './useAuth';
import type { Role } from './types';

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
