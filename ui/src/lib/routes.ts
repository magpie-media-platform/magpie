import { useCallback } from 'react';
import { useLocation } from 'react-router-dom';

export function isActivePath(pathname: string, path: string): boolean {
    if (path === '/' || path === '/admin') {
        return pathname === path;
    }
    return pathname === path || pathname.startsWith(`${path}/`);
}

export function isAdminRoute(pathname: string): boolean {
    return pathname === '/admin' || pathname.startsWith('/admin/');
}

export function useIsActive(): (path: string) => boolean {
    const { pathname } = useLocation();
    return useCallback((path: string) => isActivePath(pathname, path), [pathname]);
}
