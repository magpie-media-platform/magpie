import { Navigate, useLocation } from 'react-router-dom';

import { RouteLoadingState } from '@/router/RouteLoadingState';

import { useAuth } from './useAuth';
import { Role, RoleName } from './types';

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
