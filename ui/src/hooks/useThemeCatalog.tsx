import { useEffect, type ReactNode } from 'react';
import { useQuery } from '@tanstack/react-query';

import { settingsQueryKeys } from '@/hooks/useSettings';
import { apiGet } from '@/lib/api';
import { EXTENSION_CATALOG_EVENTS } from '@/lib/extensionCatalogEvents';
import { setThemeCatalog, type ThemeCatalog } from '@/lib/theme';
import { useInvalidateQueriesOnEvents } from './useInvalidateQueriesOnEvents';

export const themeCatalogQueryKey = [...settingsQueryKeys.groups, 'themes'] as const;

export function useThemeCatalog() {
    return useQuery({
        queryKey: themeCatalogQueryKey,
        queryFn: () => apiGet<ThemeCatalog>('/ui/themes', { skipAuth: true }),
        staleTime: Infinity,
    });
}

export function ThemeProvider({ children }: { children: ReactNode }) {
    useInvalidateQueriesOnEvents(['settingsGroup.updated', ...EXTENSION_CATALOG_EVENTS], [themeCatalogQueryKey], {
        exact: true,
    });
    const { data } = useThemeCatalog();
    useEffect(() => {
        if (data) setThemeCatalog(data);
    }, [data]);
    return children;
}
