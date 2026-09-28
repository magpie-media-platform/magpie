import { LocalContentLink } from '@/components/content/LocalContentPreview';
import { type ColumnDef } from '@tanstack/react-table';
import {
    PauseIcon,
    PlayIcon,
    LightningIcon,
    TrashIcon,
    DotsThreeVerticalIcon,
    StopCircleIcon,
    InfoIcon,
    LinkBreakIcon,
} from '@phosphor-icons/react';

import { Badge } from '@/components/ui/Badge';
import { ContentPoster } from '@/components/content/ContentCardPrimitives';
import { ContentLineageTitle } from '@/components/content/ContentLineageTitle';
import { selectColumn } from '@/components/ui/DataTable/selectColumn';
import {
    DropdownMenu,
    DropdownMenuTrigger,
    DropdownMenuContent,
    DropdownMenuItem,
    DropdownMenuSeparator,
} from '@/components/ui/DropdownMenu';
import { ColumnHeaderFilter, type FacetOption } from '@/components/ui/ColumnHeaderFilter';
import {
    type ApiDownload,
    type DownloadStatusValue,
    downloadStatusLabels,
    downloadStatusVariants,
    isFailedDownloadStatus,
} from '@/hooks/useDownloads';
import { type BulkSelection } from '@/hooks/useBulkSelection';
import { type DownloadFilterState } from '@/types';
import { formatDateTime } from '@/lib/dateFormatting';
import { displayLineageLeafTitle } from '@/lib/downloadTitle';
import { formatRelativeTime } from '@/lib/formatTime';

export const stoppableStatuses = new Set<DownloadStatusValue>([
    'Grabbed',
    'Queued',
    'Downloading',
    'Stalled',
    'Paused',
]);

export interface RowActionHandlers {
    onPause: (id: string) => void;
    onResume: (id: string) => void;
    onForceStart: (download: ApiDownload) => void;
    onStop: (id: string) => void;
    onForget: (download: ApiDownload) => void;
    onRemove: (download: ApiDownload) => void;
    onViewDetails: (download: ApiDownload) => void;
}

interface DownloadColumnConfig {
    tab: 'queue' | 'history';
    filters: DownloadFilterState;
    onChange: (filters: DownloadFilterState) => void;
    clientOptions: FacetOption[];
    stateOptions: FacetOption[];
    selection: BulkSelection;
    canControlDownloads: boolean;
    canManageDownloads: boolean;
    getClientName: (download: ApiDownload) => string;
    actionsCell: NonNullable<ColumnDef<ApiDownload>['cell']>;
}

function displayTitle(download: ApiDownload): string {
    return download.contentHierarchyName?.trim() || download.title;
}

export function getEndedAt(download: ApiDownload): string {
    if (isFailedDownloadStatus(download.status)) {
        return download.failedAt ?? download.dateCompleted ?? download.modifiedAt ?? download.createdAt;
    }
    if (download.status === 'Cancelled') {
        return download.modifiedAt ?? download.dateCompleted ?? download.createdAt;
    }
    return download.dateCompleted ?? download.modifiedAt ?? download.createdAt;
}

function progressVariant(status: DownloadStatusValue): 'downloading' | 'stalled' | 'paused' | 'queued' {
    if (status === 'Downloading') {
        return 'downloading';
    }
    if (status === 'Stalled') {
        return 'stalled';
    }
    if (status === 'Paused') {
        return 'paused';
    }
    return 'queued';
}

export function StatusBadge({ status }: { status: DownloadStatusValue }) {
    return (
        <Badge variant={downloadStatusVariants[status]} size="sm" dot data-status={downloadStatusVariants[status]}>
            {downloadStatusLabels[status]}
        </Badge>
    );
}

export function DownloadProgress({ download }: { download: ApiDownload }) {
    if (download.progress === null || download.progress === undefined) {
        return <span className="downloads-page__empty-cell">&mdash;</span>;
    }
    const percent = Math.min(100, Math.max(0, download.progress));
    const displayPercent = Number.isInteger(percent) ? percent : Math.round(percent);
    return (
        <div
            className="downloads-page__progress"
            data-variant={progressVariant(download.status)}
            data-testid="downloads-page__progress"
        >
            <div
                className="downloads-page__progress-bar"
                role="progressbar"
                aria-valuenow={displayPercent}
                aria-valuemin={0}
                aria-valuemax={100}
            >
                <div className="downloads-page__progress-fill" style={{ width: `${percent}%` }} />
            </div>
            <span className="downloads-page__progress-percent">{displayPercent}%</span>
        </div>
    );
}

export function HoldAnnotation({ download }: { download: ApiDownload }) {
    if (download.clientRemovedAt) {
        return (
            <span
                className="downloads-page__policy-meta"
                data-testid="downloads-page__client-removed"
                title={`Removed from client ${formatDateTime(download.clientRemovedAt)}`}
            >
                Removed from client
            </span>
        );
    }

    if (download.status !== 'Imported' || !download.heldByPolicy) {
        return null;
    }

    const text = 'Held by client';
    return (
        <span className="downloads-page__policy-meta" data-testid="downloads-page__held-by-policy" title={text}>
            {text}
        </span>
    );
}

function PosterCell({ download }: { download: ApiDownload }) {
    return (
        <ContentPoster
            posterUrl={download.posterUrl}
            title={displayLineageLeafTitle(download)}
            className="downloads-page__poster"
            placeholderClassName="downloads-page__poster--placeholder"
        />
    );
}

export function TitleCell({ download }: { download: ApiDownload }) {
    const label = displayLineageLeafTitle(download);
    if (download.contentHierarchyItemId) {
        return (
            <LocalContentLink
                contentId={download.contentHierarchyItemId}
                className="downloads-page__title-link"
                data-content-id={download.contentHierarchyItemId}
                data-testid="downloads-page__item-title"
            >
                <ContentLineageTitle
                    qualifiedName={{
                        id: download.contentHierarchyItemId,
                        parentId: download.contentHierarchyParentId,
                        title: label,
                    }}
                />
            </LocalContentLink>
        );
    }
    return (
        <span className="downloads-page__title-plain" title={label} data-testid="downloads-page__item-title">
            {label}
        </span>
    );
}

export function RowActions({
    download,
    handlers,
    canControl,
    canManage,
}: {
    download: ApiDownload;
    handlers: RowActionHandlers;
    canControl: boolean;
    canManage: boolean;
}) {
    const label = displayLineageLeafTitle(download);
    const showPause = canControl && Boolean(download.canPause);
    const showResume = canControl && Boolean(download.canResume);
    const showForceStart = canManage && Boolean(download.canForceStart);
    const showStop = canManage && stoppableStatuses.has(download.status);

    return (
        <DropdownMenu>
            <DropdownMenuTrigger asChild>
                <button
                    type="button"
                    className="downloads-page__action-trigger"
                    data-testid="downloads-page__action-btn"
                    aria-label={`Actions for ${label}`}
                    data-action="row-menu"
                    data-target={download.id}
                    onClick={(event) => event.stopPropagation()}
                >
                    <DotsThreeVerticalIcon weight="bold" aria-hidden="true" />
                </button>
            </DropdownMenuTrigger>
            <DropdownMenuContent align="end" onClick={(event) => event.stopPropagation()}>
                <DropdownMenuItem
                    onSelect={() => handlers.onViewDetails(download)}
                    data-action="view-details"
                    data-target={download.id}
                >
                    <InfoIcon weight="bold" aria-hidden="true" />
                    View details
                </DropdownMenuItem>
                {showPause && (
                    <DropdownMenuItem
                        onSelect={() => handlers.onPause(download.id)}
                        data-action="pause"
                        data-target={download.id}
                    >
                        <PauseIcon weight="bold" aria-hidden="true" />
                        Pause
                    </DropdownMenuItem>
                )}
                {showResume && (
                    <DropdownMenuItem
                        onSelect={() => handlers.onResume(download.id)}
                        data-action="resume"
                        data-target={download.id}
                    >
                        <PlayIcon weight="bold" aria-hidden="true" />
                        Resume
                    </DropdownMenuItem>
                )}
                {showForceStart && (
                    <DropdownMenuItem
                        onSelect={() => handlers.onForceStart(download)}
                        data-action="force-start"
                        data-target={download.id}
                    >
                        <LightningIcon weight="bold" aria-hidden="true" />
                        Force start
                    </DropdownMenuItem>
                )}
                {showStop && (
                    <DropdownMenuItem
                        onSelect={() => handlers.onStop(download.id)}
                        data-action="stop"
                        data-target={download.id}
                    >
                        <StopCircleIcon weight="bold" aria-hidden="true" />
                        Stop
                    </DropdownMenuItem>
                )}
                {canManage && (
                    <>
                        <DropdownMenuSeparator />
                        <DropdownMenuItem
                            onSelect={() => handlers.onForget(download)}
                            data-action="forget"
                            data-target={download.id}
                        >
                            <LinkBreakIcon weight="bold" aria-hidden="true" />
                            Forget
                        </DropdownMenuItem>
                        <DropdownMenuItem
                            className="downloads-page__action-danger"
                            onSelect={() => handlers.onRemove(download)}
                            data-action="remove"
                            data-target={download.id}
                        >
                            <TrashIcon weight="bold" aria-hidden="true" />
                            Remove
                        </DropdownMenuItem>
                    </>
                )}
            </DropdownMenuContent>
        </DropdownMenu>
    );
}

export function buildDownloadColumns(config: DownloadColumnConfig): ColumnDef<ApiDownload>[] {
    const {
        tab,
        filters,
        onChange,
        clientOptions,
        stateOptions,
        selection,
        canControlDownloads,
        canManageDownloads,
        getClientName,
        actionsCell,
    } = config;
    const setClient = (client: string) => onChange({ ...filters, client });
    const setState = (state: string) => onChange({ ...filters, state });
    const canSelectRows = tab === 'queue' ? canControlDownloads : canManageDownloads;
    return [
        ...(canSelectRows
            ? [
                  selectColumn<ApiDownload>({
                      selection,
                      getId: (item) => item.id,
                      headerLabel: 'Select all',
                      rowLabel: (item) => displayLineageLeafTitle(item),
                  }),
              ]
            : []),
        {
            id: 'poster',
            header: '',
            enableSorting: false,
            size: 56,
            cell: ({ row }) => <PosterCell download={row.original} />,
        },
        {
            id: 'title',
            header: 'Title',
            accessorFn: (row) => displayTitle(row),
            cell: ({ row }) => <TitleCell download={row.original} />,
        },
        {
            id: 'client',
            header: () => (
                <ColumnHeaderFilter
                    type="facet"
                    label="Client"
                    active={filters.client.length > 0}
                    options={clientOptions}
                    value={filters.client}
                    onChange={setClient}
                />
            ),
            meta: { headerLabel: 'Client' },
            size: 132,
            accessorFn: (row) => getClientName(row),
            cell: ({ row }) => {
                const name = getClientName(row.original);
                return name ? (
                    <span className="downloads-page__client">{name}</span>
                ) : (
                    <span className="downloads-page__empty-cell">&mdash;</span>
                );
            },
        },
        {
            id: 'status',
            header: () => (
                <ColumnHeaderFilter
                    type="facet"
                    label="Status"
                    active={filters.state.length > 0}
                    options={stateOptions}
                    value={filters.state}
                    onChange={setState}
                />
            ),
            meta: { headerLabel: 'Status' },
            size: 156,
            accessorFn: (row) => row.status,
            cell: ({ row }) => (
                <div className="downloads-page__status-cell">
                    <StatusBadge status={row.original.status} />
                    <HoldAnnotation download={row.original} />
                </div>
            ),
        },
        ...(tab === 'queue'
            ? [
                  {
                      id: 'progress',
                      header: 'Progress',
                      size: 140,
                      accessorFn: (row) => row.progress ?? -1,
                      cell: ({ row }) => <DownloadProgress download={row.original} />,
                  } satisfies ColumnDef<ApiDownload>,
              ]
            : []),
        tab === 'queue'
            ? {
                  id: 'added',
                  header: 'Added',
                  size: 116,
                  accessorFn: (row) => row.createdAt,
                  cell: ({ row }) => (
                      <span className="downloads-page__date" title={formatDateTime(row.original.createdAt)}>
                          {formatRelativeTime(row.original.createdAt, '--')}
                      </span>
                  ),
              }
            : {
                  id: 'completed',
                  header: 'Ended',
                  size: 116,
                  accessorFn: (row) => getEndedAt(row),
                  cell: ({ row }) => {
                      const value = getEndedAt(row.original);
                      return (
                          <span className="downloads-page__date" title={formatDateTime(value)}>
                              {formatRelativeTime(value, '--')}
                          </span>
                      );
                  },
              },
        {
            id: 'lastStatus',
            header: 'Last status',
            size: 124,
            accessorFn: (row) => row.modifiedAt ?? '',
            cell: ({ row }) => (
                <span
                    className="downloads-page__date"
                    title={row.original.modifiedAt ? formatDateTime(row.original.modifiedAt) : undefined}
                >
                    {row.original.modifiedAt ? formatRelativeTime(row.original.modifiedAt, '--') : '--'}
                </span>
            ),
        },
        {
            id: 'actions',
            header: '',
            enableSorting: false,
            size: 48,
            cell: actionsCell,
        },
    ];
}
