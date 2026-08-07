import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';

import { apiDelete, apiGet, apiPatch, apiPost } from '@/lib/api';
import type { ApiEventFilter } from '@/lib/eventContextCriteria';
import { useInvalidateQueriesOnEvents } from './useInvalidateQueriesOnEvents';
import { queryErrorOrNull } from '@/lib/utils';

import type { IntegrationSettingDefinition } from './useProviderSchema';

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

interface NotificationRuleUpdate extends Omit<NotificationRuleCreate, 'settings'> {
    id: string;

    settings?: Record<string, string>;
}

const notificationRuleQueryKeys = {
    all: ['notification-rules'] as const,
    list: () => ['notification-rules', 'list'] as const,
    detail: (id: string) => ['notification-rules', 'detail', id] as const,
    providers: () => ['notification-rules', 'providers'] as const,
    providerSchema: (providerType: string) => ['notification-rules', 'providers', providerType, 'schema'] as const,
};

const NOTIFICATION_PROVIDER_CHANGE_EVENTS = [
    'extension.installed',
    'extension.updated',
    'extension.enabled',
    'extension.disabled',
    'extension.uninstalled',
    'extension.upgraded',
] as const;

async function fetchRules(): Promise<NotificationRule[]> {
    return apiGet<NotificationRule[]>('/notification-rules');
}

async function fetchProviders(): Promise<string[]> {
    const providers =
        await apiGet<Array<{ kind: string; providerType: string; schema: IntegrationSettingDefinition[] }>>(
            '/providers',
        );

    return providers.filter((provider) => provider.kind === 'notification').map((provider) => provider.providerType);
}

async function fetchProviderSchema(providerType: string): Promise<IntegrationSettingDefinition[]> {
    const providers =
        await apiGet<Array<{ kind: string; providerType: string; schema: IntegrationSettingDefinition[] }>>(
            '/providers',
        );

    return (
        providers.find((provider) => provider.kind === 'notification' && provider.providerType === providerType)
            ?.schema ?? []
    );
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

async function deleteRule(id: string): Promise<void> {
    return apiDelete(`/notification-rules/${id}`);
}

async function testRule(id: string): Promise<void> {
    return apiPost(`/notification-rules/${id}/test`);
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

        deleteRule: deleteMutation.mutateAsync,
        isDeleting: deleteMutation.isPending,

        testRule: testMutation.mutateAsync,
        isTesting: testMutation.isPending,
    };
}

export function useNotificationProviders() {
    useInvalidateQueriesOnEvents(NOTIFICATION_PROVIDER_CHANGE_EVENTS, [notificationRuleQueryKeys.providers()], {
        exact: true,
        owner: 'notification-providers',
    });
    const providersQuery = useQuery({
        queryKey: notificationRuleQueryKeys.providers(),
        queryFn: fetchProviders,
        staleTime: 60_000,
    });

    return {
        providers: providersQuery.data ?? [],
        isLoading: providersQuery.isLoading,
        error: queryErrorOrNull(providersQuery),
        refetch: providersQuery.refetch,
    };
}

export function useNotificationProviderSchema(providerType: string | null) {
    useInvalidateQueriesOnEvents(
        providerType ? NOTIFICATION_PROVIDER_CHANGE_EVENTS : [],
        [notificationRuleQueryKeys.providerSchema(providerType ?? '')],
        { exact: true, owner: 'notification-provider-schema' },
    );
    return useQuery({
        queryKey: notificationRuleQueryKeys.providerSchema(providerType ?? ''),
        queryFn: () => fetchProviderSchema(providerType!),
        enabled: !!providerType,
        staleTime: 60_000,
    });
}
