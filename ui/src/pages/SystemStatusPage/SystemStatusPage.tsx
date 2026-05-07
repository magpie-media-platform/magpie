import { useCallback } from 'react';
import { useNavigate } from 'react-router-dom';
import {
    CheckCircle,
    Clock,
    CloudArrowDown,
    Database,
    Folder,
    Gauge,
    HardDrives,
    Heartbeat,
    Plugs,
    Queue,
    Television,
    XCircle,
    Warning,
} from '@phosphor-icons/react';

import { Badge } from '@/components/ui/Badge';
import { Card } from '@/components/ui/Card';
import { DiskSpaceWidget, type DiskSpaceInfo } from '@/components/ui/DiskSpaceWidget';
import { DownloadQueueWidget } from '@/components/ui/DownloadQueueWidget';
import { HealthCard } from '@/components/ui/HealthCard';
import { LogViewer } from '@/components/ui/LogViewer';
import { Skeleton } from '@/components/ui/Skeleton';
import { SystemStatusSlot } from '@/components/ui/Slot/SystemSlots';
import { useDocumentTitle } from '@/hooks/useDocumentTitle';
import { useSystemStatus, type ServiceStatus, type ServiceHealth } from '@/hooks/useSystemStatus';
import { formatUptime } from '@/lib/formatTime';

import './SystemStatusPage.css';

const statusVariantMap: Record<ServiceStatus, string> = {
    healthy: 'healthy',
    unhealthy: 'unhealthy',
    degraded: 'degraded',
    unknown: 'unknown',
};

function StatusIcon({ status }: { status: ServiceStatus }) {
    switch (status) {
        case 'healthy':
            return (
                <CheckCircle weight="fill" className="service-card__status-icon service-card__status-icon--healthy" />
            );
        case 'unhealthy':
            return <XCircle weight="fill" className="service-card__status-icon service-card__status-icon--unhealthy" />;
        case 'degraded':
            return <Warning weight="fill" className="service-card__status-icon service-card__status-icon--degraded" />;
        case 'unknown':
        default:
            return <Clock weight="fill" className="service-card__status-icon service-card__status-icon--unknown" />;
    }
}

interface ServiceCardProps {
    service: ServiceHealth;
    icon: React.ReactNode;
    loading?: boolean;
}

function ServiceCard({ service, icon, loading = false }: ServiceCardProps) {
    if (loading) {
        return (
            <Card className="service-card service-card--loading">
                <div className="service-card__icon">
                    <Skeleton width={32} height={32} />
                </div>
                <div className="service-card__content">
                    <Skeleton width={80} height={18} />
                    <Skeleton width={120} height={14} />
                </div>
                <div className="service-card__status">
                    <Skeleton width={60} height={24} />
                </div>
            </Card>
        );
    }

    const variant = statusVariantMap[service.status];

    return (
        <Card
            className={`service-card service-card--${variant}`}
            data-component="service-card"
            data-status={service.status}
        >
            <div className="service-card__icon" aria-hidden="true">
                {icon}
            </div>
            <div className="service-card__content">
                <h3 className="service-card__name">{service.name}</h3>
                {service.message && <p className="service-card__message">{service.message}</p>}
                {service.responseTime !== undefined && (
                    <span className="service-card__response-time">{service.responseTime}ms</span>
                )}
            </div>
            <div className="service-card__status">
                <StatusIcon status={service.status} />
                <Badge variant={variant as 'healthy' | 'unhealthy' | 'degraded' | 'unknown'} dot>
                    {service.status.charAt(0).toUpperCase() + service.status.slice(1)}
                </Badge>
            </div>
        </Card>
    );
}

interface SystemInfoItemProps {
    label: string;
    value: string;
    loading?: boolean;
}

function SystemInfoItem({ label, value, loading }: SystemInfoItemProps) {
    if (loading) {
        return (
            <div className="system-info__item">
                <span className="system-info__label">{label}</span>
                <Skeleton width={100} height={16} />
            </div>
        );
    }

    return (
        <div className="system-info__item">
            <span className="system-info__label">{label}</span>
            <span className="system-info__value">{value}</span>
        </div>
    );
}

export function SystemStatusPage() {
    useDocumentTitle('System Status');
    const navigate = useNavigate();
    const statusData = useSystemStatus();

    const getIndexerStatusUrl = useCallback((status: string) => `/indexers?status=${status}`, []);

    const getClientStatusUrl = useCallback((status: string) => `/download-clients?status=${status}`, []);

    const getServerStatusUrl = useCallback((status: string) => `/admin/media-servers?status=${status}`, []);

    const handleFolderClick = useCallback(
        (folder: DiskSpaceInfo) => {
            navigate(`/admin/root-folders?id=${folder.id}`);
        },
        [navigate],
    );

    const handleViewAllFolders = useCallback(() => {
        navigate('/admin/root-folders');
    }, [navigate]);

    const getDownloadStateUrl = useCallback((state: string) => `/downloads?state=${state}`, []);

    const getDownloadUrl = useCallback((download: { id: string }) => `/downloads/${download.id}`, []);

    return (
        <div className="system-status-page" data-page="system-status">
            <SystemStatusSlot>
                <header className="system-status-page__header">
                    <Gauge
                        weight="duotone"
                        className="system-status-page__icon"
                        aria-hidden="true"
                        data-testid="system-status-page__icon"
                    />
                    <h1 className="system-status-page__title">System Status</h1>
                </header>

                <div className="system-status-page__content">
                    <section className="system-status-page__section" aria-labelledby="services-heading">
                        <h2 id="services-heading" className="system-status-page__section-title">
                            <Plugs weight="duotone" aria-hidden="true" />
                            Core Services
                        </h2>
                        <div className="system-status-page__services">
                            <ServiceCard
                                service={statusData.services.api}
                                icon={<Heartbeat weight="duotone" />}
                                loading={statusData.isLoading}
                            />
                            <ServiceCard
                                service={statusData.services.database}
                                icon={<Database weight="duotone" />}
                                loading={statusData.isLoading}
                            />
                            <ServiceCard
                                service={statusData.services.scheduler}
                                icon={<Clock weight="duotone" />}
                                loading={statusData.isLoading}
                            />
                        </div>
                    </section>

                    <section className="system-status-page__section" aria-labelledby="integrations-heading">
                        <h2 id="integrations-heading" className="system-status-page__section-title">
                            <Plugs weight="duotone" aria-hidden="true" />
                            Integrations
                        </h2>
                        <div className="system-status-page__integrations">
                            <HealthCard
                                heading="Indexers"
                                icon={<Database weight="duotone" />}
                                counts={statusData.integrations.indexers}
                                loading={statusData.isLoading}
                                getStatusUrl={getIndexerStatusUrl}
                                headingLevel={3}
                            />
                            <HealthCard
                                heading="Download Clients"
                                icon={<CloudArrowDown weight="duotone" />}
                                counts={statusData.integrations.downloadClients}
                                loading={statusData.isLoading}
                                getStatusUrl={getClientStatusUrl}
                                headingLevel={3}
                            />
                            <HealthCard
                                heading="Media Servers"
                                icon={<Television weight="duotone" />}
                                counts={statusData.integrations.mediaServers}
                                loading={statusData.isLoading}
                                getStatusUrl={getServerStatusUrl}
                                headingLevel={3}
                            />
                        </div>
                    </section>

                    <section className="system-status-page__section" aria-labelledby="disk-space-heading">
                        <h2 id="disk-space-heading" className="system-status-page__section-title">
                            <Folder weight="duotone" aria-hidden="true" />
                            Disk Space
                        </h2>
                        <DiskSpaceWidget
                            heading="Root Folders"
                            icon={<HardDrives weight="duotone" />}
                            folders={statusData.diskSpace}
                            loading={statusData.isLoading}
                            onFolderClick={handleFolderClick}
                            onViewAllClick={handleViewAllFolders}
                            headingLevel={3}
                        />
                    </section>

                    <section className="system-status-page__section" aria-labelledby="queue-heading">
                        <h2 id="queue-heading" className="system-status-page__section-title">
                            <Queue weight="duotone" aria-hidden="true" />
                            Download Queue
                        </h2>
                        <DownloadQueueWidget
                            heading="Active Downloads"
                            icon={<CloudArrowDown weight="duotone" />}
                            counts={statusData.downloadQueue.counts}
                            activeDownloads={statusData.downloadQueue.activeDownloads}
                            maxItems={3}
                            loading={statusData.isLoading}
                            getStateUrl={getDownloadStateUrl}
                            viewAllUrl="/downloads"
                            getDownloadUrl={getDownloadUrl}
                            headingLevel={3}
                        />
                    </section>

                    <section className="system-status-page__section" aria-labelledby="sysinfo-heading">
                        <h2 id="sysinfo-heading" className="system-status-page__section-title">
                            <HardDrives weight="duotone" aria-hidden="true" />
                            System Information
                        </h2>
                        <Card className="system-info">
                            <div className="system-info__grid">
                                <SystemInfoItem
                                    label="Version"
                                    value={statusData.systemInfo.version}
                                    loading={statusData.isLoading}
                                />
                                <SystemInfoItem
                                    label="Runtime"
                                    value={statusData.systemInfo.runtime}
                                    loading={statusData.isLoading}
                                />
                                <SystemInfoItem
                                    label="Operating System"
                                    value={statusData.systemInfo.operatingSystem}
                                    loading={statusData.isLoading}
                                />
                                <SystemInfoItem
                                    label="Architecture"
                                    value={statusData.systemInfo.architecture}
                                    loading={statusData.isLoading}
                                />
                                <SystemInfoItem
                                    label="System Root"
                                    value={statusData.systemInfo.systemRoot}
                                    loading={statusData.isLoading}
                                />
                                <SystemInfoItem
                                    label="Node ID"
                                    value={statusData.systemInfo.nodeId}
                                    loading={statusData.isLoading}
                                />
                                <SystemInfoItem
                                    label="Uptime"
                                    value={formatUptime(statusData.systemInfo.startedAt)}
                                    loading={statusData.isLoading}
                                />
                            </div>
                        </Card>
                    </section>

                    <section className="system-status-page__section" aria-labelledby="logs-heading">
                        <LogViewer loading={statusData.isLoading} persistFilters />
                    </section>
                </div>
            </SystemStatusSlot>
        </div>
    );
}

SystemStatusPage.displayName = 'SystemStatusPage';

export default SystemStatusPage;
