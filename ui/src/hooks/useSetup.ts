import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';

import { fetchSetupStatus, createSetupAdmin, setupStatusQueryKey, type SetupStatus } from '../lib/setupApi';

export function useSetupStatus() {
    return useQuery({
        queryKey: setupStatusQueryKey,
        queryFn: fetchSetupStatus,
        staleTime: 60_000,
        gcTime: 5 * 60_000,
        retry: 1,
        retryDelay: 500,
    });
}

export function useCreateSetupAdmin() {
    const queryClient = useQueryClient();

    return useMutation({
        mutationFn: createSetupAdmin,
        onSuccess: () => {
            // Write fresh status synchronously so SetupRedirectGate sees it before the navigation; invalidate too so a later fetch reconciles with the server.
            queryClient.setQueryData<SetupStatus>(setupStatusQueryKey, {
                isRequired: false,
                isComplete: true,
            });
            queryClient.invalidateQueries({ queryKey: setupStatusQueryKey });
        },
    });
}
