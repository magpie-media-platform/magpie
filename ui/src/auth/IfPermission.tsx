/* eslint-disable react-refresh/only-export-components */
import type { ReactNode } from 'react';

import { useAuth } from './useAuth';
import { getRequiredRole, RoleName, type Permission } from './types';

export interface IfPermissionProps {
    action: Permission;

    children: ReactNode;

    fallback?: ReactNode;
}

export function IfPermission({ action, children, fallback = null }: IfPermissionProps) {
    const { hasPermission } = useAuth();

    if (!hasPermission(action)) {
        return <>{fallback}</>;
    }

    return <>{children}</>;
}

IfPermission.displayName = 'IfPermission';

export function usePermission(action: Permission): {
    allowed: boolean;
    requiredRoleName: string;
} {
    const { hasPermission } = useAuth();
    const requiredRole = getRequiredRole(action);

    return {
        allowed: hasPermission(action),
        requiredRoleName: RoleName[requiredRole],
    };
}
