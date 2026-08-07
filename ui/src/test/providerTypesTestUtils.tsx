import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import type { ReactNode } from 'react';

import { createTestQueryClient, type SeededProviderTypes } from './providerTypesTestQueryClient';

interface ProviderTypesWrapperProps {
    children: ReactNode;
    seed?: SeededProviderTypes;
    client?: QueryClient;
}

export function ProviderTypesWrapper({ children, seed, client }: ProviderTypesWrapperProps) {
    const queryClient = client ?? createTestQueryClient(seed);
    return <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>;
}
