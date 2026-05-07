import { useState, useMemo } from 'react';
import * as Collapsible from '@radix-ui/react-collapsible';
import {
    CircleNotch,
    CheckCircle,
    XCircle,
    ListBullets,
    Clock,
    Warning,
    Hourglass,
    Stop,
    CaretDown,
    Info,
    Bug,
} from '@phosphor-icons/react';

import { Badge, type BadgeVariant } from '@/components/ui/Badge';
import type { ApiJobExecution } from '@/hooks/useJobs';
import { jobExecutionStatusLabels } from '@/hooks/useJobs';
import {
    mapStatusToDisplay,
    statusLabels,
    formatJobDateTime,
    calculateDuration,
    formatLogTime,
} from '@/lib/jobExecution';

import './ExecutionRow.css';

export interface ExecutionRowProps {
    execution: ApiJobExecution;
}

export function ExecutionRow({ execution }: ExecutionRowProps) {
    const [isExpanded, setIsExpanded] = useState(false);
    const displayStatus = mapStatusToDisplay(execution.status);
    const isActive = displayStatus === 'running' || displayStatus === 'pending';
    const statusLabel = statusLabels[displayStatus];
    const hasDetails = Boolean(execution.statusMessage);

    const StatusIcon = useMemo(() => {
        switch (displayStatus) {
            case 'running':
                return CircleNotch;
            case 'pending':
                return Hourglass;
            case 'completed':
                return CheckCircle;
            case 'failed':
                return XCircle;
            case 'cancelled':
                return Stop;
            default:
                return Clock;
        }
    }, [displayStatus]);

    const badgeVariant = useMemo((): BadgeVariant => {
        switch (displayStatus) {
            case 'running':
                return 'running';
            case 'pending':
                return 'pending';
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
    const progress = execution.progress ?? 0;

    return (
        <Collapsible.Root
            open={isExpanded}
            onOpenChange={setIsExpanded}
            className={`execution-row execution-row--${displayStatus}`}
            data-execution-id={execution.id}
            data-status={displayStatus}
            data-expanded={isExpanded || undefined}
        >
            <Collapsible.Trigger asChild>
                <button
                    type="button"
                    className="execution-row__trigger"
                    role="listitem"
                    aria-expanded={isExpanded}
                    aria-label={`${execution.jobType} - ${statusLabel}. Click to ${isExpanded ? 'hide' : 'show'} details.`}
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
                                <span className="execution-row__name">{execution.jobType}</span>
                                <Badge variant={badgeVariant} size="sm">
                                    {jobExecutionStatusLabels[execution.status]}
                                </Badge>
                            </div>
                            <div className="execution-row__meta">
                                <span className="execution-row__time">
                                    <Clock weight="regular" aria-hidden="true" />
                                    {formatJobDateTime(execution.createdAt)}
                                </span>
                                {execution.completedAt && <span className="execution-row__duration">{duration}</span>}
                            </div>
                            {execution.statusMessage && (
                                <div className={`execution-row__message execution-row__message--${displayStatus}`}>
                                    {displayStatus === 'failed' && <Warning weight="fill" aria-hidden="true" />}
                                    <span>{execution.statusMessage}</span>
                                </div>
                            )}
                        </div>
                        {hasDetails && (
                            <div className="execution-row__expand">
                                <CaretDown weight="bold" className="execution-row__caret" aria-hidden="true" />
                            </div>
                        )}
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
                                aria-label={`${execution.jobType} progress`}
                            >
                                <div
                                    className={`execution-row__progress-fill ${progress === 0 ? 'execution-row__progress-fill--indeterminate' : `execution-row__progress-fill--${displayStatus}`}`}
                                    style={progress > 0 ? { width: `${progress}%` } : undefined}
                                />
                            </div>
                            <span className="execution-row__progress-text">{progress}%</span>
                        </div>
                    )}
                </button>
            </Collapsible.Trigger>

            {hasDetails && (
                <Collapsible.Content className="execution-row__logs" data-testid="execution-row__logs">
                    <div className="execution-row__logs-header">
                        <ListBullets weight="bold" aria-hidden="true" />
                        <span>Execution Details</span>
                    </div>
                    <div
                        className="execution-row__logs-list"
                        role="log"
                        aria-label={`${execution.jobType} execution details`}
                    >
                        {execution.startedAt && (
                            <div className="execution-row__log-entry execution-row__log-entry--info">
                                <span className="execution-row__log-time">{formatLogTime(execution.startedAt)}</span>
                                <span className="execution-row__log-level">
                                    <Info weight="bold" aria-hidden="true" />
                                </span>
                                <span className="execution-row__log-message">Started</span>
                            </div>
                        )}
                        {execution.statusMessage && (
                            <div
                                className={`execution-row__log-entry execution-row__log-entry--${displayStatus === 'failed' ? 'error' : 'info'}`}
                            >
                                <span className="execution-row__log-time">
                                    {formatLogTime(execution.completedAt ?? execution.createdAt)}
                                </span>
                                <span className="execution-row__log-level">
                                    {displayStatus === 'failed' ? (
                                        <XCircle weight="bold" aria-hidden="true" />
                                    ) : displayStatus === 'running' ? (
                                        <Bug weight="bold" aria-hidden="true" />
                                    ) : (
                                        <Info weight="bold" aria-hidden="true" />
                                    )}
                                </span>
                                <span className="execution-row__log-message">{execution.statusMessage}</span>
                            </div>
                        )}
                        {execution.completedAt && (
                            <div
                                className={`execution-row__log-entry execution-row__log-entry--${displayStatus === 'completed' ? 'info' : displayStatus === 'failed' ? 'error' : 'warn'}`}
                            >
                                <span className="execution-row__log-time">{formatLogTime(execution.completedAt)}</span>
                                <span className="execution-row__log-level">
                                    {displayStatus === 'completed' ? (
                                        <CheckCircle weight="bold" aria-hidden="true" />
                                    ) : displayStatus === 'failed' ? (
                                        <XCircle weight="bold" aria-hidden="true" />
                                    ) : (
                                        <Warning weight="bold" aria-hidden="true" />
                                    )}
                                </span>
                                <span className="execution-row__log-message">
                                    {displayStatus === 'completed'
                                        ? 'Completed successfully'
                                        : displayStatus === 'failed'
                                          ? 'Failed'
                                          : displayStatus === 'cancelled'
                                            ? 'Cancelled'
                                            : 'Finished'}
                                </span>
                            </div>
                        )}
                    </div>
                </Collapsible.Content>
            )}
        </Collapsible.Root>
    );
}

ExecutionRow.displayName = 'ExecutionRow';
