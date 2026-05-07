import { forwardRef, type ComponentPropsWithoutRef, useMemo, useCallback } from 'react';
import {
    DownloadSimple,
    ArrowClockwise,
    MagnifyingGlass,
    CheckCircle,
    XCircle,
    Clock,
    Warning,
} from '@phosphor-icons/react';
import { type ColumnDef } from '@tanstack/react-table';

import { DataTable } from '../DataTable';
import { Badge, type BadgeVariant } from '../Badge';

import './ContentTabs.css';

export type HistoryEventType = 'grabbed' | 'downloaded' | 'imported' | 'failed' | 'searched';

export type HistoryEventStatus = 'completed' | 'failed' | 'pending';

export interface HistoryEvent {
    id: string;

    eventType: HistoryEventType;

    status: HistoryEventStatus;

    sourceName?: string;

    indexer?: string;

    downloadClient?: string;

    quality?: string;

    timestamp: string;

    message?: string;
}

export interface HistoryTabProps extends Omit<ComponentPropsWithoutRef<'div'>, 'children'> {
    events: HistoryEvent[];

    loading?: boolean;

    onEventClick?: (event: HistoryEvent) => void;

    emptyMessage?: string;
}

function getEventIcon(eventType: HistoryEventType) {
    const icons: Record<HistoryEventType, typeof DownloadSimple> = {
        grabbed: DownloadSimple,
        downloaded: CheckCircle,
        imported: ArrowClockwise,
        failed: XCircle,
        searched: MagnifyingGlass,
    };
    return icons[eventType] || Clock;
}

function getStatusVariant(status: HistoryEventStatus): BadgeVariant {
    const variants: Record<HistoryEventStatus, BadgeVariant> = {
        completed: 'fulfilled',
        failed: 'unhealthy',
        pending: 'degraded',
    };
    return variants[status];
}

function formatEventType(eventType: HistoryEventType): string {
    const labels: Record<HistoryEventType, string> = {
        grabbed: 'Grabbed',
        downloaded: 'Downloaded',
        imported: 'Imported',
        failed: 'Failed',
        searched: 'Searched',
    };
    return labels[eventType];
}

function formatTimestamp(dateStr: string): string {
    try {
        const date = new Date(dateStr);
        const now = new Date();
        const diffMs = now.getTime() - date.getTime();
        const diffHours = diffMs / (1000 * 60 * 60);
        const diffDays = diffHours / 24;

        if (diffHours < 1) {
            const diffMins = Math.floor(diffMs / (1000 * 60));
            return diffMins <= 1 ? 'Just now' : `${diffMins}m ago`;
        }
        if (diffHours < 24) {
            return `${Math.floor(diffHours)}h ago`;
        }
        if (diffDays < 7) {
            return `${Math.floor(diffDays)}d ago`;
        }

        return date.toLocaleDateString(undefined, {
            year: 'numeric',
            month: 'short',
            day: 'numeric',
            hour: '2-digit',
            minute: '2-digit',
        });
    } catch {
        return dateStr;
    }
}

function EventCell({ event }: { event: HistoryEvent }) {
    const Icon = getEventIcon(event.eventType);

    return (
        <div className={`history-tab__event-cell history-tab__event-cell--${event.eventType}`}>
            <Icon weight="bold" aria-hidden="true" />
            <span>{formatEventType(event.eventType)}</span>
        </div>
    );
}

function SourceCell({ event }: { event: HistoryEvent }) {
    return (
        <div className="history-tab__source-cell">
            {event.sourceName ? (
                <>
                    <span className="history-tab__source-name">{event.sourceName}</span>
                    {event.quality && (
                        <Badge variant="default" size="sm">
                            {event.quality}
                        </Badge>
                    )}
                </>
            ) : (
                <span className="history-tab__empty-cell">-</span>
            )}
        </div>
    );
}

function DetailsCell({ event }: { event: HistoryEvent }) {
    const details = [event.indexer, event.downloadClient].filter(Boolean);

    if (details.length === 0) {
        return <span className="history-tab__empty-cell">-</span>;
    }

    return (
        <div className="history-tab__details-cell">
            {details.map((detail, i) => (
                <span key={i} className="history-tab__detail-item">
                    {detail}
                </span>
            ))}
        </div>
    );
}

function StatusCell({ status, message }: { status: HistoryEventStatus; message?: string }) {
    const hasMessage = status === 'failed' && message;

    return (
        <div className="history-tab__status-cell">
            <Badge variant={getStatusVariant(status)} size="sm">
                {status === 'completed' ? 'Success' : status === 'failed' ? 'Failed' : 'Pending'}
            </Badge>
            {hasMessage && (
                <span title={message}>
                    <Warning weight="fill" className="history-tab__status-warning" aria-label="Has error message" />
                </span>
            )}
        </div>
    );
}

export const HistoryTab = forwardRef<HTMLDivElement, HistoryTabProps>(
    ({ events, loading = false, onEventClick, emptyMessage = 'No history found', className, ...props }, ref) => {
        const columns: ColumnDef<HistoryEvent>[] = useMemo(
            () => [
                {
                    id: 'eventType',
                    accessorKey: 'eventType',
                    header: 'Event',
                    size: 120,
                    cell: ({ row }) => <EventCell event={row.original} />,
                },
                {
                    id: 'source',
                    accessorKey: 'sourceName',
                    header: 'Source',
                    size: 300,
                    cell: ({ row }) => <SourceCell event={row.original} />,
                },
                {
                    id: 'details',
                    header: 'Details',
                    size: 150,
                    enableSorting: false,
                    cell: ({ row }) => <DetailsCell event={row.original} />,
                },
                {
                    id: 'status',
                    accessorKey: 'status',
                    header: 'Status',
                    size: 100,
                    cell: ({ row }) => <StatusCell status={row.original.status} message={row.original.message} />,
                },
                {
                    id: 'timestamp',
                    accessorKey: 'timestamp',
                    header: 'When',
                    size: 140,
                    cell: ({ row }) => (
                        <span
                            className="history-tab__timestamp"
                            title={new Date(row.original.timestamp).toLocaleString()}
                        >
                            {formatTimestamp(row.original.timestamp)}
                        </span>
                    ),
                },
            ],
            [],
        );

        const handleRowClick = useCallback(
            (row: HistoryEvent) => {
                if (onEventClick) {
                    onEventClick(row);
                }
            },
            [onEventClick],
        );

        const getRowId = useCallback((row: HistoryEvent) => row.id, []);

        const classes = ['history-tab', className].filter(Boolean).join(' ');

        return (
            <div ref={ref} className={classes} data-component="history-tab" {...props}>
                <div className="history-tab__table">
                    <DataTable<HistoryEvent>
                        columns={columns}
                        data={events}
                        onRowClick={onEventClick ? handleRowClick : undefined}
                        loading={loading}
                        enableSorting={true}
                        enablePagination={events.length > 25}
                        pageSize={25}
                        getRowId={getRowId}
                        emptyState={
                            <div className="history-tab__empty">
                                <Clock weight="thin" aria-hidden="true" />
                                <p>{emptyMessage}</p>
                            </div>
                        }
                    />
                </div>

                <div className="history-tab__mobile-list">
                    {events.length === 0 ? (
                        <div className="history-tab__empty">
                            <Clock weight="thin" aria-hidden="true" />
                            <p>{emptyMessage}</p>
                        </div>
                    ) : (
                        events.map((event) => (
                            <div
                                key={event.id}
                                className={`history-tab__mobile-card history-tab__mobile-card--${event.eventType}`}
                                onClick={onEventClick ? () => onEventClick(event) : undefined}
                                onKeyDown={
                                    onEventClick
                                        ? (e) => {
                                              if (e.key === 'Enter' || e.key === ' ') {
                                                  e.preventDefault();
                                                  onEventClick(event);
                                              }
                                          }
                                        : undefined
                                }
                                data-event-id={event.id}
                                role={onEventClick ? 'button' : undefined}
                                tabIndex={onEventClick ? 0 : undefined}
                            >
                                <div className="history-tab__mobile-header">
                                    <EventCell event={event} />
                                    <span className="history-tab__mobile-timestamp">
                                        {formatTimestamp(event.timestamp)}
                                    </span>
                                </div>
                                {event.sourceName && (
                                    <div className="history-tab__mobile-source">
                                        <span className="history-tab__source-name">{event.sourceName}</span>
                                        {event.quality && (
                                            <Badge variant="default" size="sm">
                                                {event.quality}
                                            </Badge>
                                        )}
                                    </div>
                                )}
                                <div className="history-tab__mobile-footer">
                                    <DetailsCell event={event} />
                                    <StatusCell status={event.status} message={event.message} />
                                </div>
                                {event.status === 'failed' && event.message && (
                                    <div className="history-tab__mobile-error">
                                        <Warning weight="fill" aria-hidden="true" />
                                        <span>{event.message}</span>
                                    </div>
                                )}
                            </div>
                        ))
                    )}
                </div>
            </div>
        );
    },
);

HistoryTab.displayName = 'HistoryTab';

export default HistoryTab;
