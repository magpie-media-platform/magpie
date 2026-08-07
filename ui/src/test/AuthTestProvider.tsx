import { QueryClient, QueryClientProvider } from '@tanstack/react-query';

import { AuthProvider, Role, type AuthProviderProps } from '@/auth';
import { testEffectivePermissions } from './permissions';

export function AuthTestProvider(props: AuthProviderProps) {
    const queryClient = new QueryClient({
        defaultOptions: {
            queries: {
                retry: false,
                gcTime: 0,
                staleTime: 0,
            },
        },
    });

    const initialPermissions =
        props.initialPermissions ?? testEffectivePermissions(props.initialUser?.role ?? Role.Admin);

    return (
        <QueryClientProvider client={queryClient}>
            <AuthProvider {...props} initialPermissions={initialPermissions} />
        </QueryClientProvider>
    );
}
