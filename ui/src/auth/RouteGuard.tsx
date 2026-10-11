import { Navigate, useLocation } from 'react-router-dom';

import { RouteErrorState } from '@/router/RouteErrorState';
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

export type RoleGuardProps = {
    children: React.ReactNode;

    forbiddenPath?: string;
} & ({ minRole: Role } | { permission: Permission });

export function RoleGuard(props: RoleGuardProps) {
    const { children, forbiddenPath = '/forbidden' } = props;
    const { hasRole, getRequiredRole, isLoading, permissionsFailed, retryPermissions } = useAuth();
    const location = useLocation();
    const minRole = 'permission' in props ? getRequiredRole(props.permission) : props.minRole;

    if (minRole === undefined && permissionsFailed) {
        return <RouteErrorState onRetry={retryPermissions} />;
    }

    if (isLoading || minRole === undefined) {
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
