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
import type { SetupStepId } from './setupSteps';

interface CompletionStepProps {
    onAdvance: () => void;
    skippedSteps?: SetupStepId[];
}

interface SummaryRow {
    label: string;
    count: number | null;
    detail?: string;
}

const EMPTY_SKIPPED_STEPS: SetupStepId[] = [];

export function CompletionStep({ onAdvance, skippedSteps = EMPTY_SKIPPED_STEPS }: CompletionStepProps): ReactNode {
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
        const metadataGroups = settingsAvailable
            ? (settingsQuery.data ?? []).filter(hasConfiguredExtensionSettings)
            : null;
        const metadataExtensions = metadataGroups?.length ?? null;
        const skipped = (step: SetupStepId) => skippedSteps.includes(step);
        return [
            {
                label: 'Metadata-provider configs',
                count: metadataExtensions,
                detail: skipped('metadata')
                    ? 'Skipped'
                    : metadataGroups?.map((group) => group.name).join(', ') || undefined,
            },
            {
                label: 'Indexers',
                count: countConfiguredIndexerInstances(indexers.indexers),
                detail: skipped('indexer') ? 'Skipped' : undefined,
            },
            {
                label: 'Download clients',
                count: downloadClients.clients?.length ?? null,
                detail: skipped('download') ? 'Skipped' : undefined,
            },
            {
                label: 'Media servers',
                count: mediaServers.servers?.length ?? null,
                detail: skipped('server') ? 'Skipped' : undefined,
            },
            {
                label: 'Root folders',
                count: rootFolders.folders?.length ?? null,
                detail: skipped('root') ? 'Skipped' : undefined,
            },
        ];
    }, [
        settingsQuery.data,
        settingsQuery.error,
        indexers.indexers,
        downloadClients.clients,
        mediaServers.servers,
        rootFolders.folders,
        skippedSteps,
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

            <ul className="setup-page__summary-list" data-component="setup-summary" role="list">
                {rows.map((row) => (
                    <li key={row.label} className="setup-page__summary-row">
                        <span>{row.label}</span>
                        <span className="setup-page__summary-value">
                            <strong>{row.count === null ? '--' : row.count}</strong>
                            {row.detail && <span>{row.detail}</span>}
                        </span>
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
