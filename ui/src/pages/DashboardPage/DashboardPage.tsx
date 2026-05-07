import { useCallback, useMemo } from 'react';
import { CalendarBlank, CloudArrowDown, Database, FilmStrip, HardDrives, House, Timer } from '@phosphor-icons/react';

import { useAuth, Role } from '@/auth';
import { PullToRefresh } from '@/components/ui/PullToRefresh';
import { ServiceHealthBar, type ServiceHealthEntry } from '@/components/ui/ServiceHealthBar';
import { DownloadQueueWidget } from '@/components/ui/DownloadQueueWidget';
import { RecentJobsWidget } from '@/components/ui/RecentJobsWidget';
import { ContentStatsWidget } from '@/components/ui/ContentStatsWidget';
import { UpcomingContentWidget } from '@/components/ui/UpcomingContentWidget';
import { DashboardMainSlot, DashboardWidgetsSlot } from '@/components/ui/Slot/DashboardSlots';
import {
    useIndexerHealth,
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
import { queryErrorOrNull } from '@/lib/utils';
import type { HealthStatus } from '@/components/ui/HealthCard';

import './DashboardPage.css';

const EMPTY_HEALTH: HealthCounts = { healthy: 0, unhealthy: 0, unknown: 0, stale: 0 };
const EMPTY_DOWNLOAD_COUNTS = { downloading: 0, queued: 0, stalled: 0 };
const EMPTY_CONTENT_COUNTS = { total: 0, missing: 0, upgradable: 0, fulfilled: 0, fulfilledExternally: 0 };

export function DashboardPage() {
    useDocumentTitle('Dashboard');
    const { hasRole } = useAuth();
    const jobsBasePath = hasRole(Role.Admin) ? '/admin/jobs' : '/my-jobs';

    const indexerHealthQuery = useIndexerHealth();
    const clientHealthQuery = useDownloadClientHealth();
    const serverHealthQuery = useMediaServerHealth();
    const activeDownloadsQuery = useActiveDownloads(5);
    const recentJobsQuery = useRecentJobs(5);
    const contentCountsQuery = useContentCounts();
    const recentContentQuery = useRecentContent(3);
    const upcomingContentQuery = useUpcomingContent();

    const getIndexerStatusUrl = useCallback((status: HealthStatus) => `/admin/indexers?status=${status}`, []);

    const getClientStatusUrl = useCallback((status: HealthStatus) => `/admin/download-clients?status=${status}`, []);

    const getServerStatusUrl = useCallback((status: HealthStatus) => `/admin/media-servers?status=${status}`, []);

    const getDownloadStateUrl = useCallback((state: string) => `/downloads?state=${state}`, []);

    const getDownloadUrl = useCallback((download: { id: string }) => `/downloads/${download.id}`, []);

    const getJobUrl = useCallback((job: { id: string }) => `${jobsBasePath}?job=${job.id}`, [jobsBasePath]);

    const getContentStatusUrl = useCallback((status: string) => `/library?fulfillment=${status}`, []);

    const getContentItemUrl = useCallback((item: { id: string }) => `/library/${item.id}`, []);

    const getUpcomingItemUrl = useCallback((item: { contentId: string }) => `/library/${item.contentId}`, []);

    const retryHealth = useCallback(() => {
        void indexerHealthQuery.refetch();
        void clientHealthQuery.refetch();
        void serverHealthQuery.refetch();
    }, [indexerHealthQuery, clientHealthQuery, serverHealthQuery]);
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
            indexerHealthQuery.refetch(),
            clientHealthQuery.refetch(),
            serverHealthQuery.refetch(),
            activeDownloadsQuery.refetch(),
            recentJobsQuery.refetch(),
            contentCountsQuery.refetch(),
            recentContentQuery.refetch(),
            upcomingContentQuery.refetch(),
        ]);
    }, [
        indexerHealthQuery,
        clientHealthQuery,
        serverHealthQuery,
        activeDownloadsQuery,
        recentJobsQuery,
        contentCountsQuery,
        recentContentQuery,
        upcomingContentQuery,
    ]);

    const indexerHealth = indexerHealthQuery.data ?? EMPTY_HEALTH;
    const clientHealth = clientHealthQuery.data ?? EMPTY_HEALTH;
    const serverHealth = serverHealthQuery.data ?? EMPTY_HEALTH;

    const healthServices: ServiceHealthEntry[] = useMemo(
        () => [
            {
                name: 'Indexers',
                icon: <Database weight="duotone" />,
                counts: indexerHealth,
                getStatusUrl: getIndexerStatusUrl,
                addUrl: '/admin/indexers',
            },
            {
                name: 'Download Clients',
                icon: <CloudArrowDown weight="duotone" />,
                counts: clientHealth,
                getStatusUrl: getClientStatusUrl,
                addUrl: '/admin/download-clients',
            },
            {
                name: 'Media Servers',
                icon: <HardDrives weight="duotone" />,
                counts: serverHealth,
                getStatusUrl: getServerStatusUrl,
                addUrl: '/admin/media-servers',
            },
        ],
        [indexerHealth, clientHealth, serverHealth, getIndexerStatusUrl, getClientStatusUrl, getServerStatusUrl],
    );

    const healthLoading = indexerHealthQuery.isLoading || clientHealthQuery.isLoading || serverHealthQuery.isLoading;
    const healthError =
        queryErrorOrNull(indexerHealthQuery) ??
        queryErrorOrNull(clientHealthQuery) ??
        queryErrorOrNull(serverHealthQuery);

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
                    <header className="dashboard-page__header">
                        <House
                            weight="duotone"
                            className="dashboard-page__icon"
                            aria-hidden="true"
                            data-testid="dashboard-page__icon"
                        />
                        <h1 className="dashboard-page__title">Dashboard</h1>
                    </header>

                    <div className="dashboard-page__content">
                        <ServiceHealthBar
                            services={healthServices}
                            loading={healthLoading}
                            error={healthError}
                            onRetry={retryHealth}
                        />

                        <section className="dashboard-page__widgets" aria-label="Dashboard widgets">
                            <DownloadQueueWidget
                                heading="Downloads"
                                icon={<CloudArrowDown weight="duotone" />}
                                counts={activeDownloadsQuery.data?.counts ?? EMPTY_DOWNLOAD_COUNTS}
                                activeDownloads={activeDownloadsQuery.data?.items ?? []}
                                maxItems={3}
                                loading={activeDownloadsQuery.isLoading}
                                error={queryErrorOrNull(activeDownloadsQuery)}
                                onRetry={retryActiveDownloads}
                                getStateUrl={getDownloadStateUrl}
                                viewAllUrl="/downloads"
                                getDownloadUrl={getDownloadUrl}
                            />
                            <RecentJobsWidget
                                heading="Recent Jobs"
                                icon={<Timer weight="duotone" />}
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
                                icon={<FilmStrip weight="duotone" />}
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
                                icon={<CalendarBlank weight="duotone" />}
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
