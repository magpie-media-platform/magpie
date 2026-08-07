import { useCallback, useMemo, useState, type ReactNode } from 'react';
import { Navigate, useLocation, useNavigate } from 'react-router-dom';
import { useQuery, useQueryClient } from '@tanstack/react-query';

import { Logo } from '@/components/ui/Logo';
import { Button } from '@/components/ui/Button';
import { useAuth } from '@/auth';
import { useDocumentTitle } from '@/hooks/useDocumentTitle';
import { useSetupStatus } from '@/hooks/useSetup';
import { settingsQueryKeys, type ApiSettingsGroup } from '@/hooks/useSettings';
import { apiGet } from '@/lib/api';
import { RouteLoadingState } from '@/router/RouteLoadingState';

import { AdminClaimStep } from './AdminClaimStep';
import { CompletionStep } from './CompletionStep';
import { DbProviderStep } from './DbProviderStep';
import { FirstDownloadClientStep } from './FirstDownloadClientStep';
import { FirstIndexerStep } from './FirstIndexerStep';
import { FirstMediaServerStep } from './FirstMediaServerStep';
import { FirstRootFolderStep } from './FirstRootFolderStep';
import { MetadataProviderStep } from './MetadataProviderStep';
import {
    SETUP_STEPS,
    type SetupStepAvailability,
    type SetupStepId,
    nextStepAfter,
    pickInitialStep,
} from './setupSteps';

import './SetupPage.css';

export function SetupPage(): ReactNode {
    useDocumentTitle('Set Up Magpie');

    const { isAuthenticated, isLoading: isAuthLoading } = useAuth();
    const location = useLocation();
    const navigate = useNavigate();
    const queryClient = useQueryClient();

    const statusQuery = useSetupStatus();
    const status = statusQuery.data ?? null;

    const groupsQuery = useQuery({
        queryKey: settingsQueryKeys.all,
        queryFn: () => apiGet<ApiSettingsGroup[]>('/settings/groups'),
        enabled: isAuthenticated,
        retry: false,
    });
    const stepAvailability = useMemo<SetupStepAvailability>(
        () => ({
            includeMetadata: (groupsQuery.data ?? []).some((group) => group.extensionId && group.settings.length > 0),
        }),
        [groupsQuery.data],
    );

    const [stepId, setStepId] = useState<SetupStepId | null>(null);

    const resolvedStepId = useMemo<SetupStepId | null>(() => {
        if (stepId !== null) {
            return stepId;
        }
        if (!status) {
            return null;
        }
        return pickInitialStep(status);
    }, [stepId, status]);

    const handleAdvance = useCallback(async () => {
        if (!resolvedStepId) {
            return;
        }

        const availability =
            resolvedStepId === 'admin'
                ? {
                      includeMetadata: (
                          await queryClient.fetchQuery({
                              queryKey: settingsQueryKeys.all,
                              queryFn: () => apiGet<ApiSettingsGroup[]>('/settings/groups'),
                              retry: false,
                          })
                      ).some((group) => group.extensionId && group.settings.length > 0),
                  }
                : stepAvailability;
        const next = nextStepAfter(resolvedStepId, availability);
        if (next === null) {
            void navigate(isAuthenticated ? '/' : '/login', { replace: true });
            return;
        }
        setStepId(next);
    }, [resolvedStepId, queryClient, stepAvailability, navigate, isAuthenticated]);

    if ((statusQuery.isLoading && resolvedStepId === null) || isAuthLoading) {
        return <RouteLoadingState />;
    }

    if (status?.isRequired && !status.requiresAdminClaim && !isAuthenticated) {
        return <Navigate to="/login" state={{ from: location }} replace />;
    }

    if (status && !status.isRequired && !status.requiresAdminClaim) {
        return <Navigate to={isAuthenticated ? '/' : '/login'} replace />;
    }

    const currentStep = resolvedStepId ? SETUP_STEPS[resolvedStepId] : null;
    const currentState = statusQuery.isError ? 'error' : (resolvedStepId ?? 'loading');

    return (
        <div className="setup-page" data-page="setup" data-current-step={currentState}>
            <div className="setup-page__card">
                <div className="setup-page__brand">
                    <Logo size={48} variant="solid" />
                    <h1 className="setup-page__title">Welcome to Magpie</h1>
                    <p className="setup-page__subtitle">
                        {currentStep?.subtitle ?? 'Configure Magpie for your environment.'}
                    </p>
                </div>

                {statusQuery.isError ? (
                    <div className="setup-page__status-error" role="alert" data-testid="setup-status-error">
                        <p className="setup-page__error">
                            Failed to load browser setup status. Complete setup with the local Magpie CLI. Then retry
                            this check.
                        </p>
                        <Button
                            type="button"
                            variant="primary"
                            onClick={() => void statusQuery.refetch()}
                            loading={statusQuery.isFetching}
                            className="setup-page__submit"
                        >
                            Retry
                        </Button>
                    </div>
                ) : resolvedStepId === 'db' ? (
                    <DbProviderStep onAdvance={handleAdvance} />
                ) : resolvedStepId === 'admin' ? (
                    <AdminClaimStep onAdvance={handleAdvance} />
                ) : resolvedStepId === 'metadata' ? (
                    <MetadataProviderStep onAdvance={handleAdvance} />
                ) : resolvedStepId === 'indexer' ? (
                    <FirstIndexerStep onAdvance={handleAdvance} />
                ) : resolvedStepId === 'download' ? (
                    <FirstDownloadClientStep onAdvance={handleAdvance} />
                ) : resolvedStepId === 'root' ? (
                    <FirstRootFolderStep onAdvance={handleAdvance} />
                ) : resolvedStepId === 'server' ? (
                    <FirstMediaServerStep onAdvance={handleAdvance} />
                ) : resolvedStepId === 'done' ? (
                    <CompletionStep onAdvance={handleAdvance} />
                ) : null}
            </div>
        </div>
    );
}

SetupPage.displayName = 'SetupPage';

export default SetupPage;
