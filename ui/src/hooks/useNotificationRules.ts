import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';

import { apiDelete, apiGet, apiPatch, apiPost } from '@/lib/api';
import type { ApiEventFilter } from '@/lib/eventContextCriteria';
import { useInvalidateQueriesOnEvents } from './useInvalidateQueriesOnEvents';
import { queryErrorOrNull } from '@/lib/utils';

import { useProviderSchemaQuery } from './useProviderSchema';
import { selectProviderChoices, useProviderCatalog, type ProviderDescriptor } from './useProviderCatalog';

function selectNotificationProviders(providers: ProviderDescriptor[]) {
    return selectProviderChoices(providers, 'notification');
}
import type { ConnectionTestResult } from './useTestConnection';

export interface NotificationRule {
    id: string;

    name: string;

    ownerUserId?: string | null;

    eventFilter: ApiEventFilter;

    providerType: string;

    enabled: boolean;

    settings?: Record<string, string>;

    createdAt: string;

    createdByUserId?: string | null;

    modifiedAt?: string | null;

    modifiedByUserId?: string | null;
}

interface NotificationRuleCreate {
    name: string;

    ownerUserId?: string | null;

    eventFilter: ApiEventFilter;

    providerType: string;

    settings: Record<string, string>;

    enabled: boolean;
}

interface NotificationRuleUpdate extends Omit<NotificationRuleCreate, 'settings' | 'providerType' | 'ownerUserId'> {
    id: string;

    settings?: Record<string, string>;
}

const notificationRuleQueryKeys = {
    all: ['notification-rules'] as const,
    list: () => ['notification-rules', 'list'] as const,
    providers: () => ['notification-rules', 'providers'] as const,
    providerSchema: (providerType: string) => ['notification-rules', 'providers', providerType, 'schema'] as const,
    detail: (id: string) => ['notification-rules', 'detail', id] as const,
};

async function fetchRules(): Promise<NotificationRule[]> {
    return apiGet<NotificationRule[]>('/notification-rules');
}

async function createRule(request: NotificationRuleCreate): Promise<NotificationRule> {
    return apiPost<NotificationRule>('/notification-rules', request);
}

async function updateRule(request: NotificationRuleUpdate): Promise<void> {
    const { id, name, eventFilter, settings, enabled } = request;
    return apiPatch(`/notification-rules/${id}`, {
        name: { value: name },
        eventFilter: { value: eventFilter },
        ...(settings ? { settings: { add: settings } } : {}),
        enabled: { value: enabled },
    });
}

async function setRuleEnabled(id: string, enabled: boolean): Promise<void> {
    return apiPatch(`/notification-rules/${id}`, { enabled: { value: enabled } });
}

async function deleteRule(id: string): Promise<void> {
    return apiDelete(`/notification-rules/${id}`);
}

async function testRule(id: string): Promise<ConnectionTestResult> {
    return apiPost<ConnectionTestResult>(`/notification-rules/${id}/test`);
}

export function useNotificationRules() {
    useInvalidateQueriesOnEvents(
        ['notificationRule.created', 'notificationRule.updated', 'notificationRule.deleted'],
        [notificationRuleQueryKeys.list()],
        { exact: true, owner: 'notification-rules' },
    );
    const queryClient = useQueryClient();

    const rulesQuery = useQuery({
        queryKey: notificationRuleQueryKeys.list(),
        queryFn: fetchRules,
    });

    const createMutation = useMutation({
        mutationFn: createRule,
        onSuccess: () => {
            void queryClient.invalidateQueries({ queryKey: notificationRuleQueryKeys.all });
        },
    });

    const updateMutation = useMutation({
        mutationFn: updateRule,
        onSuccess: () => {
            void queryClient.invalidateQueries({ queryKey: notificationRuleQueryKeys.all });
        },
    });

    const toggleMutation = useMutation({
        mutationFn: ({ id, enabled }: { id: string; enabled: boolean }) => setRuleEnabled(id, enabled),
        onSuccess: () => {
            void queryClient.invalidateQueries({ queryKey: notificationRuleQueryKeys.all });
        },
    });

    const deleteMutation = useMutation({
        mutationFn: deleteRule,
        onSuccess: () => {
            void queryClient.invalidateQueries({ queryKey: notificationRuleQueryKeys.all });
        },
    });

    const testMutation = useMutation({
        mutationFn: testRule,
    });

    return {
        rules: rulesQuery.data ?? [],
        isLoading: rulesQuery.isLoading,
        error: queryErrorOrNull(rulesQuery),
        refetch: rulesQuery.refetch,

        createRule: createMutation.mutateAsync,
        isCreating: createMutation.isPending,

        updateRule: updateMutation.mutateAsync,
        isUpdating: updateMutation.isPending,

        toggleRuleEnabled: (id: string, enabled: boolean) => toggleMutation.mutateAsync({ id, enabled }),
        isToggling: toggleMutation.isPending,

        deleteRule: deleteMutation.mutateAsync,
        isDeleting: deleteMutation.isPending,

        testRule: testMutation.mutateAsync,
        isTesting: testMutation.isPending,
    };
}

export function useNotificationProviders() {
    const providersQuery = useProviderCatalog(selectNotificationProviders);

    return {
        providers: providersQuery.data?.types ?? [],
        providerLabels: providersQuery.data?.labels ?? {},
        isLoading: providersQuery.isLoading,
        error: queryErrorOrNull(providersQuery),
        refetch: providersQuery.refetch,
    };
}

export function useNotificationProviderSchema(providerType: string | null) {
    return useProviderSchemaQuery('notification', providerType);
}
