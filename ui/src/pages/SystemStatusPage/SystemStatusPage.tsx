import { useCallback } from 'react';
import { useNavigate } from 'react-router-dom';
import { ArrowClockwiseIcon, FolderIcon, GaugeIcon, HardDrivesIcon, StethoscopeIcon } from '@phosphor-icons/react';

import { Card } from '@/components/ui/Card';
import { DiskSpaceWidget, type DiskSpaceInfo } from '@/components/ui/DiskSpaceWidget';
import { PageHeader } from '@/components/ui/PageHeader';
import { Skeleton } from '@/components/ui/Skeleton';
import { SystemStatusSlot } from '@/components/ui/Slot/SystemSlots';
import { useDocumentTitle } from '@/hooks/useDocumentTitle';
import { useSystemStatus } from '@/hooks/useSystemStatus';
import { formatUptime } from '@/lib/formatTime';

import { DiagnosticsSection } from './DiagnosticsSection';
import { RestartHostPanel } from './RestartHostPanel';

import './SystemStatusPage.css';

interface SystemInfoItemProps {
    label: string;
    value: string;
    loading?: boolean;
    width?: 'medium' | 'wide';
}

function SystemInfoItem({ label, value, loading, width }: SystemInfoItemProps) {
    const className = `system-info__item${width ? ` system-info__item--${width}` : ''}`;

    if (loading) {
        return (
            <div className={className}>
                <span className="system-info__label">{label}</span>
                <Skeleton width={100} height={16} />
            </div>
        );
    }

    return (
        <div className={className}>
            <span className="system-info__label">{label}</span>
            <span className="system-info__value">{value}</span>
        </div>
    );
}

export function SystemStatusPage() {
    useDocumentTitle('System Status');
    const navigate = useNavigate();
    const statusData = useSystemStatus();

    const handleFolderClick = useCallback(
        (folder: DiskSpaceInfo) => {
            void navigate(`/admin/root-folders?edit=${encodeURIComponent(folder.id)}`);
        },
        [navigate],
    );

    const handleViewAllFolders = useCallback(() => {
        void navigate('/admin/root-folders');
    }, [navigate]);

    return (
        <div className="system-status-page" data-page="system-status">
            <SystemStatusSlot>
                <PageHeader
                    className="system-status-page__header"
                    title="System Status"
                    description="Host information, disk space, and diagnostics."
                    icon={<GaugeIcon weight="duotone" aria-hidden="true" data-testid="system-status-page__icon" />}
                />

                <div className="system-status-page__content">
                    <section className="system-status-page__section" aria-labelledby="sysinfo-heading">
                        <h2 id="sysinfo-heading" className="system-status-page__section-title">
                            <HardDrivesIcon weight="duotone" aria-hidden="true" />
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
                                    label="Architecture"
                                    value={statusData.systemInfo.architecture}
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
                                <SystemInfoItem
                                    label="Operating System"
                                    value={statusData.systemInfo.operatingSystem}
                                    loading={statusData.isLoading}
                                    width="medium"
                                />
                                <SystemInfoItem
                                    label="System Root"
                                    value={statusData.systemInfo.systemRoot}
                                    loading={statusData.isLoading}
                                    width="wide"
                                />
                            </div>
                        </Card>
                    </section>

                    <section className="system-status-page__section" aria-labelledby="restart-heading">
                        <h2 id="restart-heading" className="system-status-page__section-title">
                            <ArrowClockwiseIcon weight="duotone" aria-hidden="true" />
                            Service Control
                        </h2>
                        <RestartHostPanel />
                    </section>

                    <section className="system-status-page__section" aria-labelledby="disk-space-heading">
                        <h2 id="disk-space-heading" className="system-status-page__section-title">
                            <FolderIcon weight="duotone" aria-hidden="true" />
                            Disk Space
                        </h2>
                        <DiskSpaceWidget
                            heading="Root Folders"
                            icon={<HardDrivesIcon weight="duotone" />}
                            folders={statusData.diskSpace}
                            totals={statusData.diskSpaceTotals}
                            loading={statusData.isLoading}
                            onFolderClick={handleFolderClick}
                            onViewAllClick={handleViewAllFolders}
                            headingLevel={3}
                        />
                    </section>

                    <section className="system-status-page__section" aria-labelledby="diagnostics-heading">
                        <h2 id="diagnostics-heading" className="system-status-page__section-title">
                            <StethoscopeIcon weight="duotone" aria-hidden="true" />
                            Magpie Doctor
                        </h2>
                        <p className="system-status-page__section-note">
                            Issues found by the system scan, including unhealthy integrations.
                        </p>
                        <DiagnosticsSection />
                    </section>
                </div>
            </SystemStatusSlot>
        </div>
    );
}

SystemStatusPage.displayName = 'SystemStatusPage';

export default SystemStatusPage;
