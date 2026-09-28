import type { ReactNode } from 'react';

import { useAuth } from './useAuth';
import type { Permission, Role } from './types';

export type RequireRoleProps = {
    children: ReactNode;

    fallback?: ReactNode;
} & ({ min: Role } | { permission: Permission });

export function RequireRole(props: RequireRoleProps) {
    const { children, fallback = null } = props;
    const { hasRole, hasPermission } = useAuth();
    const allowed = 'permission' in props ? hasPermission(props.permission) : hasRole(props.min);

    if (!allowed) {
        return <>{fallback}</>;
    }

    return <>{children}</>;
}

RequireRole.displayName = 'RequireRole';
