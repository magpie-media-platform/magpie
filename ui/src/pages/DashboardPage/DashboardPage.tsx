import { useCallback, useMemo } from 'react';
import {
    CalendarBlankIcon,
    CheckCircleIcon,
    CloudArrowDownIcon,
    FilmStripIcon,
    HardDrivesIcon,
    HouseIcon,
} from '@phosphor-icons/react';

import { useAuth, Role } from '@/auth';
import { PageHeader } from '@/components/ui/PageHeader';
import { PullToRefresh } from '@/components/ui/PullToRefresh';
import { ServiceHealthBar, type ServiceHealthEntry } from '@/components/ui/ServiceHealthBar';
import { DownloadQueueWidget } from '@/components/ui/DownloadQueueWidget';
import { ContentStatsWidget, type ContentStatsRow } from '@/components/ui/ContentStatsWidget';
import { UpcomingContentWidget } from '@/components/dashboard/UpcomingContentWidget';
import { Slot } from '@/components/ui/Slot';
import { OnboardingChecklist } from '@/components/dashboard/OnboardingChecklist';
import {
    useDownloadClientHealth,
    useMediaServerHealth,
    useActiveDownloads,
    useContentCounts,
    useRecentContent,
    useRecentlyFulfilled,
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
const EMPTY_CONTENT_COUNTS = { total: 0, missing: 0, upgradable: 0, fulfilled: 0 };
const CONTENT_ROW_STATUSES = ['fulfilled', 'upgradable', 'missing'] as const;
const RECENT_CONTENT_URL = `/library/all?${new URLSearchParams({ sort: 'dateAdded', sortDir: 'desc' }).toString()}`;
const FULFILLED_CONTENT_URL = `/library/all?${new URLSearchParams({
    fulfillment: 'fulfilled',
    levels: 'all',
    sort: 'fulfilledDate',
    sortDir: 'desc',
}).toString()}`;

export function DashboardPage() {
    useDocumentTitle('Dashboard');
    const { hasRole } = useAuth();
    const canManageIntegrations = hasRole(Role.Admin);

    const clientHealthQuery = useDownloadClientHealth();
    const serverHealthQuery = useMediaServerHealth();
    const activeDownloadsQuery = useActiveDownloads(5);
    const recentlyFulfilledQuery = useRecentlyFulfilled(7);
    const contentCountsQuery = useContentCounts();
    const recentContentQuery = useRecentContent(5);
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

    const getContentStatusUrl = useCallback(
        (status: FulfillmentStatus) =>
            `/library/all?${new URLSearchParams({ fulfillment: status, levels: 'all' }).toString()}`,
        [],
    );

    const getContentItemUrl = useCallback((item: { id: string }) => `/library/${item.id}`, []);

    const getUpcomingItemUrl = useCallback((item: { contentId: string }) => `/library/${item.contentId}`, []);

    const retryHealth = useCallback(() => {
        void clientHealthQuery.refetch();
        void serverHealthQuery.refetch();
    }, [clientHealthQuery, serverHealthQuery]);
    const retryActiveDownloads = useCallback(() => {
        void activeDownloadsQuery.refetch();
    }, [activeDownloadsQuery]);
    const retryRecentlyFulfilled = useCallback(() => {
        void recentlyFulfilledQuery.refetch();
    }, [recentlyFulfilledQuery]);
    const retryContent = useCallback(() => {
        void contentCountsQuery.refetch();
        void recentContentQuery.refetch();
    }, [contentCountsQuery, recentContentQuery]);
    const retryUpcoming = useCallback(() => {
        void upcomingContentQuery.refetch();
    }, [upcomingContentQuery]);

    const handleRefresh = useCallback(async () => {
        await Promise.all([
            clientHealthQuery.refetch(),
            serverHealthQuery.refetch(),
            activeDownloadsQuery.refetch(),
            recentlyFulfilledQuery.refetch(),
            contentCountsQuery.refetch(),
            recentContentQuery.refetch(),
            upcomingContentQuery.refetch(),
        ]);
    }, [
        clientHealthQuery,
        serverHealthQuery,
        activeDownloadsQuery,
        recentlyFulfilledQuery,
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

    const contentCounts = contentCountsQuery.data ?? EMPTY_CONTENT_COUNTS;
    const contentRows: ContentStatsRow[] = useMemo(
        () =>
            CONTENT_ROW_STATUSES.map((status) => ({
                status,
                count: contentCounts[status],
                url: getContentStatusUrl(status),
            })),
        [contentCounts, getContentStatusUrl],
    );

    const recentlyFulfilled = recentlyFulfilledQuery.data;
    const fulfilledRows: ContentStatsRow[] = useMemo(
        () => [
            { status: 'fulfilled', label: 'Today', count: recentlyFulfilled?.today ?? 0 },
            { status: 'fulfilled', label: 'This week', count: recentlyFulfilled?.thisWeek ?? 0 },
        ],
        [recentlyFulfilled],
    );

    const contentLoading = contentCountsQuery.isLoading || recentContentQuery.isLoading;
    const contentError = queryErrorOrNull(contentCountsQuery) ?? queryErrorOrNull(recentContentQuery);

    return (
        <div className="dashboard-page" data-page="dashboard">
            <PullToRefresh onRefresh={handleRefresh} className="dashboard-page__pull-to-refresh">
                <Slot slot="dashboard.main">
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
                            <ContentStatsWidget
                                heading="Fulfillment"
                                icon={<CheckCircleIcon weight="duotone" />}
                                rows={fulfilledRows}
                                recentHeading="Recent"
                                recentEmptyMessage="No fulfilled content"
                                recentItems={recentlyFulfilled?.items ?? []}
                                maxItems={7}
                                loading={recentlyFulfilledQuery.isLoading}
                                error={queryErrorOrNull(recentlyFulfilledQuery)}
                                onRetry={retryRecentlyFulfilled}
                                viewAllUrl={FULFILLED_CONTENT_URL}
                                viewAllLabel="View all fulfilled"
                                getItemUrl={getContentItemUrl}
                                showItemStatus={false}
                            />
                        </section>

                        <section className="dashboard-page__content-section" aria-label="Content overview">
                            <ContentStatsWidget
                                heading="Library"
                                icon={<FilmStripIcon weight="duotone" />}
                                total={contentCounts.total}
                                rows={contentRows}
                                recentItems={recentContentQuery.data ?? []}
                                maxItems={5}
                                loading={contentLoading}
                                error={contentError}
                                onRetry={retryContent}
                                viewAllUrl={RECENT_CONTENT_URL}
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
                            <Slot slot="dashboard.widgets" className="dashboard-page__extension-widgets" />
                        </div>
                    </div>
                </Slot>
            </PullToRefresh>
        </div>
    );
}

DashboardPage.displayName = 'DashboardPage';

export default DashboardPage;
