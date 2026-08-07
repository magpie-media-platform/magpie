import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';

import {
    fetchSetupStatus,
    createSetupAdmin,
    setupStatusQueryKey,
    fetchSetupDatabaseStatus,
    configureSetupDatabase,
    setupDatabaseStatusQueryKey,
    completeSetup,
    type SetupStatus,
} from '@/lib/setupApi';

export function useSetupStatus() {
    return useQuery({
        queryKey: setupStatusQueryKey,
        queryFn: fetchSetupStatus,
        staleTime: 60_000,
        gcTime: 5 * 60_000,
        retry: false,
        refetchOnWindowFocus: false,
        refetchOnMount: false,
        refetchOnReconnect: false,
    });
}

export function useCreateSetupAdmin() {
    const queryClient = useQueryClient();

    return useMutation({
        mutationFn: createSetupAdmin,
        onSuccess: () => {
            queryClient.setQueryData<SetupStatus>(setupStatusQueryKey, (prev) => ({
                isRequired: prev?.isRequired ?? true,
                isComplete: prev?.isComplete ?? false,
                requiresAdminClaim: false,
            }));
            void queryClient.invalidateQueries({ queryKey: setupStatusQueryKey });
        },
    });
}

export function useSetupDatabaseStatus() {
    return useQuery({
        queryKey: setupDatabaseStatusQueryKey,
        queryFn: fetchSetupDatabaseStatus,
        staleTime: 0,
        retry: 1,
        retryDelay: 500,
    });
}

export function useConfigureSetupDatabase() {
    const queryClient = useQueryClient();

    return useMutation({
        mutationFn: configureSetupDatabase,
        onSuccess: () => {
            void queryClient.invalidateQueries({ queryKey: setupDatabaseStatusQueryKey });
        },
    });
}

export function useCompleteSetup() {
    const queryClient = useQueryClient();

    return useMutation({
        mutationFn: completeSetup,
        onSuccess: (status) => {
            queryClient.setQueryData<SetupStatus>(setupStatusQueryKey, status);
            void queryClient.invalidateQueries({ queryKey: setupStatusQueryKey });
        },
    });
}
