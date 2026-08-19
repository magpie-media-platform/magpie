import { useEffect, useMemo, useRef, useState } from 'react';
import * as Collapsible from '@radix-ui/react-collapsible';
import {
    CircleNotchIcon,
    CheckCircleIcon,
    XCircleIcon,
    ClockIcon,
    WarningIcon,
    HourglassIcon,
    StopIcon,
    CaretDownIcon,
} from '@phosphor-icons/react';

import { Badge, type BadgeVariant } from '@/components/ui/Badge';
import { ExecutionDetailPanel } from '@/components/jobs/ExecutionDetailPanel';
import type { ApiJobExecution } from '@/hooks/useJobs';
import {
    mapStatusToDisplay,
    statusLabels,
    formatJobDateTime,
    calculateDuration,
    humanizeJobType,
} from '@/lib/jobExecution';

import './ExecutionRow.css';

interface ExecutionRowProps {
    execution: ApiJobExecution;
    autoExpand?: boolean;
}

export function ExecutionRow({ execution, autoExpand = false }: ExecutionRowProps) {
    const [isExpanded, setIsExpanded] = useState(autoExpand);
    const rowRef = useRef<HTMLDivElement>(null);

    useEffect(() => {
        if (!autoExpand) {
            return;
        }

        setIsExpanded(true);
        rowRef.current?.scrollIntoView({ block: 'center' });
    }, [autoExpand]);

    const displayStatus = mapStatusToDisplay(execution.status, execution.notBeforeAt);
    const isActive = displayStatus === 'running' || displayStatus === 'pending' || displayStatus === 'queued';
    const statusLabel = statusLabels[displayStatus];

    const deferredUntil =
        displayStatus === 'pending' && execution.notBeforeAt ? formatJobDateTime(execution.notBeforeAt) : null;

    const StatusIcon = useMemo(() => {
        switch (displayStatus) {
            case 'running':
                return CircleNotchIcon;
            case 'pending':
                return HourglassIcon;
            case 'queued':
                return ClockIcon;
            case 'completed':
                return CheckCircleIcon;
            case 'failed':
                return XCircleIcon;
            case 'cancelled':
                return StopIcon;
            default:
                return ClockIcon;
        }
    }, [displayStatus]);

    const badgeVariant = useMemo((): BadgeVariant => {
        switch (displayStatus) {
            case 'running':
                return 'running';
            case 'pending':
                return 'pending';
            case 'queued':
                return 'queued';
            case 'completed':
                return 'completed';
            case 'failed':
                return 'failed';
            case 'cancelled':
                return 'cancelled';
            default:
                return 'default';
        }
    }, [displayStatus]);

    const duration = calculateDuration(execution.startedAt, execution.completedAt);
    const jobName = humanizeJobType(execution.jobType);
    const progress =
        typeof execution.progress === 'number' && Number.isFinite(execution.progress)
            ? Math.min(isActive ? 99 : 100, Math.max(0, Math.round(execution.progress)))
            : 0;

    const fillModifier =
        progress > 0
            ? `execution-row__progress-fill--${displayStatus}`
            : displayStatus === 'running'
              ? 'execution-row__progress-fill--indeterminate'
              : 'execution-row__progress-fill--idle';

    return (
        <Collapsible.Root
            ref={rowRef}
            open={isExpanded}
            onOpenChange={setIsExpanded}
            className={`execution-row execution-row--${displayStatus}`}
            role="listitem"
            data-execution-id={execution.id}
            data-status={displayStatus}
            data-expanded={isExpanded || undefined}
        >
            <Collapsible.Trigger asChild>
                <button
                    type="button"
                    className="execution-row__trigger"
                    aria-expanded={isExpanded}
                    aria-label={`${jobName} - ${statusLabel}. Click to ${isExpanded ? 'hide' : 'show'} details.`}
                    data-testid="execution-row__trigger"
                >
                    <div className="execution-row__main">
                        <div className="execution-row__status">
                            <StatusIcon
                                weight={displayStatus === 'running' ? 'bold' : 'fill'}
                                className={`execution-row__status-icon execution-row__status-icon--${displayStatus}`}
                                aria-hidden="true"
                            />
                        </div>
                        <div className="execution-row__info">
                            <div className="execution-row__header">
                                <span className="execution-row__name">{jobName}</span>
                                <Badge
                                    variant={badgeVariant}
                                    size="sm"
                                    data-testid="execution-row__status-badge"
                                    title={deferredUntil ? `Deferred until ${deferredUntil}` : undefined}
                                >
                                    {statusLabel}
                                </Badge>
                            </div>
                            <div className="execution-row__meta">
                                <span className="execution-row__time">
                                    <ClockIcon weight="regular" aria-hidden="true" />
                                    {formatJobDateTime(execution.createdAt)}
                                </span>
                                {execution.completedAt && <span className="execution-row__duration">{duration}</span>}
                            </div>
                            {execution.statusMessage && (
                                <div className={`execution-row__message execution-row__message--${displayStatus}`}>
                                    {displayStatus === 'failed' && <WarningIcon weight="fill" aria-hidden="true" />}
                                    <span>{execution.statusMessage}</span>
                                </div>
                            )}
                        </div>
                        <div className="execution-row__expand">
                            <CaretDownIcon weight="bold" className="execution-row__caret" aria-hidden="true" />
                        </div>
                    </div>
                    {isActive && (
                        <div className="execution-row__progress">
                            <div
                                className="execution-row__progress-bar"
                                data-testid="execution-row__progress-bar"
                                role="progressbar"
                                aria-valuenow={progress}
                                aria-valuemin={0}
                                aria-valuemax={100}
                                aria-label={`${jobName} progress`}
                            >
                                <div
                                    className={`execution-row__progress-fill ${fillModifier}`}
                                    data-testid="execution-row__progress-fill"
                                    style={progress > 0 ? { width: `${progress}%` } : undefined}
                                />
                            </div>
                            <span className="execution-row__progress-text">{progress}%</span>
                        </div>
                    )}
                </button>
            </Collapsible.Trigger>

            <Collapsible.Content className="execution-row__logs" data-testid="execution-row__logs">
                <ExecutionDetailPanel execution={execution} active={isExpanded} />
            </Collapsible.Content>
        </Collapsible.Root>
    );
}

ExecutionRow.displayName = 'ExecutionRow';

export default ExecutionRow;
