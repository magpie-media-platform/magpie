import { useCallback, useMemo } from 'react';
import {
    CalendarBlankIcon,
    CloudArrowDownIcon,
    FilmStripIcon,
    HardDrivesIcon,
    HouseIcon,
    TimerIcon,
} from '@phosphor-icons/react';

import { useAuth, Role } from '@/auth';
import { PageHeader } from '@/components/ui/PageHeader';
import { PullToRefresh } from '@/components/ui/PullToRefresh';
import { ServiceHealthBar, type ServiceHealthEntry } from '@/components/ui/ServiceHealthBar';
import { DownloadQueueWidget } from '@/components/ui/DownloadQueueWidget';
import { RecentJobsWidget } from '@/components/ui/RecentJobsWidget';
import { ContentStatsWidget } from '@/components/ui/ContentStatsWidget';
import { UpcomingContentWidget } from '@/components/dashboard/UpcomingContentWidget';
import { DashboardMainSlot, DashboardWidgetsSlot } from '@/components/ui/Slot/DashboardSlots';
import { OnboardingChecklist } from '@/components/dashboard/OnboardingChecklist';
import {
    useDownloadClientHealth,
    useMediaServerHealth,
    useActiveDownloads,
    useRecentJobs,
    useContentCounts,
    useRecentContent,
    useUpcomingContent,
    type HealthCounts,
} from '@/hooks/useDashboard';
import { useDocumentTitle } from '@/hooks/useDocumentTitle';
import { useOnboardingStatus } from '@/hooks/useOnboardingStatus';
import { usePersistentState } from '@/hooks/usePersistentState';
import { getDownloadStateUrl } from '@/lib/downloadDeepLink';
import { queryErrorOrNull } from '@/lib/utils';
import { type FulfillmentStatus } from '@/types';
import type { HealthStatus } from '@/components/ui/HealthCard';

import './DashboardPage.css';

const EMPTY_HEALTH: HealthCounts = { healthy: 0, unhealthy: 0, degraded: 0, stabilizing: 0, unknown: 0, stale: 0 };
const EMPTY_DOWNLOAD_COUNTS = { downloading: 0, queued: 0, paused: 0, stalled: 0 };
const EMPTY_CONTENT_COUNTS = { total: 0, missing: 0, upgradable: 0, fulfilled: 0, fulfilledExternally: 0, partial: 0 };

export function DashboardPage() {
    useDocumentTitle('Dashboard');
    const { hasRole } = useAuth();
    const canManageIntegrations = hasRole(Role.Admin);
    const jobsBasePath = hasRole(Role.Admin) ? '/admin/jobs' : '/my-jobs';

    const clientHealthQuery = useDownloadClientHealth();
    const serverHealthQuery = useMediaServerHealth();
    const activeDownloadsQuery = useActiveDownloads(5);
    const recentJobsQuery = useRecentJobs(5);
    const contentCountsQuery = useContentCounts();
    const recentContentQuery = useRecentContent(3);
    const upcomingContentQuery = useUpcomingContent();

    const onboarding = useOnboardingStatus();
    const [onboardingDismissed, setOnboardingDismissed] = usePersistentState('magpie.onboarding.dismissed', false);
    const dismissOnboarding = useCallback(() => setOnboardingDismissed(true), [setOnboardingDismissed]);
    const completedSteps = onboarding.steps.filter((step) => step.done).length;
    const showOnboarding =
        canManageIntegrations &&
        !onboardingDismissed &&
        !onboarding.isLoading &&
        !onboarding.error &&
        !onboarding.isComplete;

    const getClientStatusUrl = useCallback((status: HealthStatus) => `/admin/download-clients?status=${status}`, []);

    const getServerStatusUrl = useCallback((status: HealthStatus) => `/admin/media-servers?status=${status}`, []);

    const getJobUrl = useCallback((job: { id: string }) => `${jobsBasePath}?job=${job.id}`, [jobsBasePath]);

    const getContentStatusUrl = useCallback((status: FulfillmentStatus) => `/library/all?fulfillment=${status}`, []);

    const getContentItemUrl = useCallback((item: { id: string }) => `/library/${item.id}`, []);

    const getUpcomingItemUrl = useCallback((item: { contentId: string }) => `/library/${item.contentId}`, []);

    const retryHealth = useCallback(() => {
        void clientHealthQuery.refetch();
        void serverHealthQuery.refetch();
    }, [clientHealthQuery, serverHealthQuery]);
    const retryActiveDownloads = useCallback(() => {
        void activeDownloadsQuery.refetch();
    }, [activeDownloadsQuery]);
    const retryRecentJobs = useCallback(() => {
        void recentJobsQuery.refetch();
    }, [recentJobsQuery]);
    const retryContent = useCallback(() => {
        void contentCountsQuery.refetch();
        void recentContentQuery.refetch();
        void upcomingContentQuery.refetch();
    }, [contentCountsQuery, recentContentQuery, upcomingContentQuery]);
    const retryUpcoming = useCallback(() => {
        void upcomingContentQuery.refetch();
    }, [upcomingContentQuery]);

    const handleRefresh = useCallback(async () => {
        await Promise.all([
            clientHealthQuery.refetch(),
            serverHealthQuery.refetch(),
            activeDownloadsQuery.refetch(),
            recentJobsQuery.refetch(),
            contentCountsQuery.refetch(),
            recentContentQuery.refetch(),
            upcomingContentQuery.refetch(),
        ]);
    }, [
        clientHealthQuery,
        serverHealthQuery,
        activeDownloadsQuery,
        recentJobsQuery,
        contentCountsQuery,
        recentContentQuery,
        upcomingContentQuery,
    ]);

    const clientHealth = clientHealthQuery.data ?? EMPTY_HEALTH;
    const serverHealth = serverHealthQuery.data ?? EMPTY_HEALTH;

    const healthServices: ServiceHealthEntry[] = useMemo(
        () => [
            {
                name: 'Download Clients',
                icon: <CloudArrowDownIcon weight="duotone" />,
                counts: clientHealth,
                getStatusUrl: canManageIntegrations ? getClientStatusUrl : undefined,
                addUrl: canManageIntegrations ? '/admin/download-clients' : undefined,
            },
            {
                name: 'Media Servers',
                icon: <HardDrivesIcon weight="duotone" />,
                counts: serverHealth,
                getStatusUrl: canManageIntegrations ? getServerStatusUrl : undefined,
                addUrl: canManageIntegrations ? '/admin/media-servers' : undefined,
            },
        ],
        [clientHealth, serverHealth, canManageIntegrations, getClientStatusUrl, getServerStatusUrl],
    );

    const healthLoading = clientHealthQuery.isLoading || serverHealthQuery.isLoading;
    const healthError = queryErrorOrNull(clientHealthQuery) ?? queryErrorOrNull(serverHealthQuery);

    const contentLoading =
        contentCountsQuery.isLoading || recentContentQuery.isLoading || upcomingContentQuery.isLoading;
    const contentError =
        queryErrorOrNull(contentCountsQuery) ??
        queryErrorOrNull(recentContentQuery) ??
        queryErrorOrNull(upcomingContentQuery);

    return (
        <div className="dashboard-page" data-page="dashboard">
            <PullToRefresh onRefresh={handleRefresh} className="dashboard-page__pull-to-refresh">
                <DashboardMainSlot>
                    <PageHeader
                        className="dashboard-page__header"
                        title="Dashboard"
                        icon={<HouseIcon weight="duotone" aria-hidden="true" data-testid="dashboard-page__icon" />}
                    />

                    <div className="dashboard-page__content">
                        {showOnboarding && (
                            <OnboardingChecklist
                                steps={onboarding.steps}
                                completedCount={completedSteps}
                                onDismiss={dismissOnboarding}
                            />
                        )}

                        <ServiceHealthBar
                            services={healthServices}
                            loading={healthLoading}
                            error={healthError}
                            onRetry={retryHealth}
                        />

                        <section className="dashboard-page__widgets" aria-label="Dashboard widgets">
                            <DownloadQueueWidget
                                heading="Downloads"
                                icon={<CloudArrowDownIcon weight="duotone" />}
                                counts={activeDownloadsQuery.data?.counts ?? EMPTY_DOWNLOAD_COUNTS}
                                activeDownloads={activeDownloadsQuery.data?.items ?? []}
                                maxItems={3}
                                loading={activeDownloadsQuery.isLoading}
                                error={queryErrorOrNull(activeDownloadsQuery)}
                                onRetry={retryActiveDownloads}
                                getStateUrl={getDownloadStateUrl}
                                viewAllUrl="/downloads"
                            />
                            <RecentJobsWidget
                                heading="Recent Jobs"
                                icon={<TimerIcon weight="duotone" />}
                                jobs={recentJobsQuery.data ?? []}
                                maxItems={5}
                                loading={recentJobsQuery.isLoading}
                                error={queryErrorOrNull(recentJobsQuery)}
                                onRetry={retryRecentJobs}
                                getJobUrl={getJobUrl}
                                viewAllUrl={jobsBasePath}
                            />
                        </section>

                        <section className="dashboard-page__content-section" aria-label="Content overview">
                            <ContentStatsWidget
                                heading="Content"
                                icon={<FilmStripIcon weight="duotone" />}
                                counts={contentCountsQuery.data ?? EMPTY_CONTENT_COUNTS}
                                recentItems={recentContentQuery.data ?? []}
                                maxItems={3}
                                loading={contentLoading}
                                error={contentError}
                                onRetry={retryContent}
                                getStatusUrl={getContentStatusUrl}
                                viewAllUrl="/library"
                                getItemUrl={getContentItemUrl}
                            />
                            <UpcomingContentWidget
                                heading="Upcoming"
                                icon={<CalendarBlankIcon weight="duotone" />}
                                items={upcomingContentQuery.data ?? []}
                                maxItems={5}
                                loading={upcomingContentQuery.isLoading}
                                error={queryErrorOrNull(upcomingContentQuery)}
                                onRetry={retryUpcoming}
                                getItemUrl={getUpcomingItemUrl}
                                viewCalendarUrl="/calendar"
                            />
                        </section>

                        <div data-testid="dashboard-page__extension-widgets">
                            <DashboardWidgetsSlot className="dashboard-page__extension-widgets" />
                        </div>
                    </div>
                </DashboardMainSlot>
            </PullToRefresh>
        </div>
    );
}

DashboardPage.displayName = 'DashboardPage';

export default DashboardPage;
