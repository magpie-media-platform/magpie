import { useCallback, useMemo, type ReactNode } from 'react';
import { useNavigate } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import { CheckCircleIcon, HouseIcon } from '@phosphor-icons/react';

import { Button } from '@/components/ui/Button';
import { useToast } from '@/components/ui/Toast';
import { useAuth } from '@/auth';
import { useDownloadClients } from '@/hooks/useDownloadClients';
import { countConfiguredIndexerInstances, useIndexers } from '@/hooks/useIndexers';
import { useMediaServers } from '@/hooks/useMediaServers';
import { useRootFolders } from '@/hooks/useRootFolders';
import { useCompleteSetup } from '@/hooks/useSetup';
import { apiGet, isApiError } from '@/lib/api';
import { hasConfiguredExtensionSettings, settingsQueryKeys, type ApiSettingsGroup } from '@/hooks/useSettings';

interface CompletionStepProps {
    onAdvance: () => void;
}

interface SummaryRow {
    label: string;
    count: number | null;
}

export function CompletionStep({ onAdvance }: CompletionStepProps): ReactNode {
    const { toast } = useToast();
    const { isAuthenticated } = useAuth();
    const navigate = useNavigate();
    const completeSetup = useCompleteSetup();

    const indexers = useIndexers();
    const downloadClients = useDownloadClients();
    const mediaServers = useMediaServers();
    const rootFolders = useRootFolders();
    const settingsQuery = useQuery({
        queryKey: settingsQueryKeys.all,
        queryFn: () => apiGet<ApiSettingsGroup[]>('/settings/groups'),
        retry: false,
    });

    const rows = useMemo<SummaryRow[]>(() => {
        const settingsAvailable =
            settingsQuery.data !== undefined || (settingsQuery.error !== null && !isApiError(settingsQuery.error));
        const metadataExtensions = settingsAvailable
            ? (settingsQuery.data ?? []).filter(hasConfiguredExtensionSettings).length
            : null;
        return [
            { label: 'Metadata-provider configs', count: metadataExtensions },
            { label: 'Indexers', count: countConfiguredIndexerInstances(indexers.indexers) },
            { label: 'Download clients', count: downloadClients.clients?.length ?? null },
            { label: 'Media servers', count: mediaServers.servers?.length ?? null },
            { label: 'Root folders', count: rootFolders.folders?.length ?? null },
        ];
    }, [
        settingsQuery.data,
        settingsQuery.error,
        indexers.indexers,
        downloadClients.clients,
        mediaServers.servers,
        rootFolders.folders,
    ]);

    const handleFinish = useCallback(async () => {
        try {
            await completeSetup.mutateAsync();
            toast({
                title: 'Setup complete',
                description: 'Magpie is ready to go.',
                variant: 'success',
            });
            void navigate(isAuthenticated ? '/' : '/login', { replace: true });
            onAdvance();
        } catch (error) {
            toast({
                title: 'Failed to complete setup',
                description: error instanceof Error ? error.message : 'Try again.',
                variant: 'error',
            });
        }
    }, [completeSetup, isAuthenticated, navigate, onAdvance, toast]);

    return (
        <div className="setup-page__form" data-step="done">
            <p className="setup-page__hint">
                Review the settings below. Then finish setup to start Magpie. The Admin pages contain these settings.
            </p>

            <ul className="setup-page__summary-list" data-component="setup-summary">
                {rows.map((row) => (
                    <li key={row.label} className="setup-page__summary-row">
                        <span>{row.label}</span>
                        <strong>{row.count === null ? '--' : row.count}</strong>
                    </li>
                ))}
            </ul>

            <Button
                type="button"
                variant="primary"
                size="lg"
                onClick={handleFinish}
                loading={completeSetup.isPending}
                className="setup-page__submit"
                data-action="finish-setup"
            >
                <CheckCircleIcon weight="bold" aria-hidden="true" />
                Finish setup
                <HouseIcon weight="bold" aria-hidden="true" />
            </Button>
        </div>
    );
}

CompletionStep.displayName = 'CompletionStep';
