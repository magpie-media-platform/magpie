import { Navigate, useLocation, type RouteObject } from 'react-router-dom';

import { RouteLoadingState } from '@/router/RouteLoadingState';

import { useAuth } from './useAuth';
import { Role, RoleName, type Permission } from './types';

export interface RequireAuthProps {
    children: React.ReactNode;

    loginPath?: string;
}

export function RequireAuth({ children, loginPath = '/login' }: RequireAuthProps) {
    const { isAuthenticated, isLoading } = useAuth();
    const location = useLocation();

    if (isLoading) {
        return <RouteLoadingState />;
    }

    if (!isAuthenticated) {
        return <Navigate to={loginPath} state={{ from: location }} replace />;
    }

    return <>{children}</>;
}

RequireAuth.displayName = 'RequireAuth';

export interface RoleGuardProps {
    children: React.ReactNode;

    minRole: Role;

    forbiddenPath?: string;
}

export function RoleGuard({ children, minRole, forbiddenPath = '/forbidden' }: RoleGuardProps) {
    const { hasRole, isLoading } = useAuth();
    const location = useLocation();

    // Even nested under RequireAuth, a fresh-mount RoleGuard can briefly observe user=null while the AuthProvider initializes; without this gate, real admins flash to /forbidden.
    if (isLoading) {
        return <RouteLoadingState />;
    }

    if (!hasRole(minRole)) {
        return (
            <Navigate
                to={forbiddenPath}
                state={{
                    from: location,
                    requiredRole: RoleName[minRole],
                }}
                replace
            />
        );
    }

    return <>{children}</>;
}

RoleGuard.displayName = 'RoleGuard';

export interface PermissionGuardProps {
    children: React.ReactNode;

    permission: Permission;

    forbiddenPath?: string;
}

export function PermissionGuard({ children, permission, forbiddenPath = '/forbidden' }: PermissionGuardProps) {
    const { hasPermission } = useAuth();
    const location = useLocation();

    if (!hasPermission(permission)) {
        return (
            <Navigate
                to={forbiddenPath}
                state={{
                    from: location,
                    requiredPermission: permission,
                }}
                replace
            />
        );
    }

    return <>{children}</>;
}

PermissionGuard.displayName = 'PermissionGuard';

export interface ProtectedRouteConfig extends Omit<RouteObject, 'children'> {
    minRole?: Role;

    permission?: Permission;

    children?: ProtectedRouteConfig[];
}
