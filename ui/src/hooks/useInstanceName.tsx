import { createContext, useContext, useEffect, type ReactNode } from 'react';
import { useQuery } from '@tanstack/react-query';

import { useInvalidateQueriesOnEvents } from '@/hooks/useInvalidateQueriesOnEvents';
import { settingsQueryKeys } from '@/hooks/useSettings';
import { apiFetch } from '@/lib/api';

export const DEFAULT_INSTANCE_NAME = 'Magpie';

const INSTANCE_NAME_STORAGE_KEY = 'magpie.instanceName';

const instanceNameQueryKey = [...settingsQueryKeys.groups, 'instance-name'] as const;

const InstanceNameContext = createContext(DEFAULT_INSTANCE_NAME);

function readStoredInstanceName(): string | undefined {
    try {
        return localStorage.getItem(INSTANCE_NAME_STORAGE_KEY) ?? undefined;
    } catch {
        return undefined;
    }
}

async function fetchInstanceName(): Promise<string> {
    const name = await apiFetch<string>('/system/instance-name', { skipAuth: true, cache: 'no-cache' });
    return name?.trim() || DEFAULT_INSTANCE_NAME;
}

export function InstanceNameProvider({ children }: { children: ReactNode }) {
    useInvalidateQueriesOnEvents(['settingsGroup.updated'], [instanceNameQueryKey], { exact: true });
    const { data } = useQuery({
        queryKey: instanceNameQueryKey,
        queryFn: fetchInstanceName,
        staleTime: Infinity,
    });
    const instanceName = data ?? readStoredInstanceName() ?? DEFAULT_INSTANCE_NAME;

    useEffect(() => {
        if (data === undefined) {
            return;
        }
        document.querySelector('meta[name="apple-mobile-web-app-title"]')?.setAttribute('content', data);
        try {
            localStorage.setItem(INSTANCE_NAME_STORAGE_KEY, data);
        } catch {
            void 0;
        }
    }, [data]);

    return <InstanceNameContext value={instanceName}>{children}</InstanceNameContext>;
}

export function useInstanceName(): string {
    return useContext(InstanceNameContext);
}
