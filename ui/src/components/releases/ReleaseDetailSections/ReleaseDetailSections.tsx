import { DetailsPanel, type DetailRow } from '@/components/ui/DetailsPanel';
import { Skeleton } from '@/components/ui/Skeleton';

import {
    downloadStatusLabels,
    downloadFailureReasonLabels,
    isFailedDownloadStatus,
    type ApiDownload,
    type DownloadFailureReasonValue,
} from '@/hooks/useDownloads';
import { formatDateTime } from '@/lib/dateFormatting';
import { formatFileSize } from '@/lib/formatSize';
import { safeHttpUrl } from '@/lib/safeUrl';

import './ReleaseDetailSections.css';

export interface ReleaseSummary {
    title: string;
    indexerName?: string | null;
    protocol?: string | null;
    size?: number | null;
    publishDate?: string | null;
    infoUrl?: string | null;
    externalId?: string | null;
    categories?: string[] | null;
    languages?: string[] | null;
    properties?: string | null;
}

export interface ReleaseDetailSectionsProps {
    release: ReleaseSummary;

    download?: ApiDownload | null;

    downloadLoading?: boolean;

    clientName?: string;
}

function hasValue(row: DetailRow): boolean {
    return row.value !== null && row.value !== undefined && row.value !== '';
}

function releaseRows(release: ReleaseSummary): DetailRow[] {
    return [
        { label: 'Title', value: release.title },
        { label: 'Indexer', value: release.indexerName },
        { label: 'Protocol', value: release.protocol },
        { label: 'Size', value: release.size && release.size > 0 ? formatFileSize(release.size) : null },
        { label: 'Published', value: release.publishDate ? formatDateTime(release.publishDate) : null },
        {
            label: 'Categories',
            value: release.categories && release.categories.length > 0 ? release.categories.join(', ') : null,
        },
        {
            label: 'Languages',
            value: release.languages && release.languages.length > 0 ? release.languages.join(', ') : null,
        },
        { label: 'External id', value: release.externalId, mono: true },
        { label: 'Release page', value: safeHttpUrl(release.infoUrl) },
    ].filter(hasValue);
}

function downloadRows(download: ApiDownload, clientName: string): DetailRow[] {
    return [
        { label: 'Client', value: clientName || download.downloadClientName },
        { label: 'Status', value: downloadStatusLabels[download.status] },
        { label: 'Client hold', value: download.heldByPolicy ? 'Held by client' : null },
        { label: 'File path', value: download.outputPath, mono: true },
        { label: 'Transfer id', value: download.externalId, mono: true },
        {
            label: 'Progress',
            value:
                download.progress === undefined || download.progress === null
                    ? null
                    : `${Number.isInteger(download.progress) ? download.progress : download.progress.toFixed(1)}%`,
        },
        { label: 'Status reason', value: download.statusReason, multiline: true },
        {
            label: 'Failure reason',
            value: download.failureReason
                ? downloadFailureReasonLabels[download.failureReason as DownloadFailureReasonValue]
                : null,
        },
        {
            label: 'Failed at',
            value:
                isFailedDownloadStatus(download.status) && download.failedAt ? formatDateTime(download.failedAt) : null,
        },
        { label: 'Added', value: formatDateTime(download.createdAt) },
        { label: 'Completed', value: download.dateCompleted ? formatDateTime(download.dateCompleted) : null },
        { label: 'Last status', value: download.modifiedAt ? formatDateTime(download.modifiedAt) : null },
        { label: 'Progress updated', value: download.lastProgressAt ? formatDateTime(download.lastProgressAt) : null },
    ].filter(hasValue);
}

function propertyRows(properties?: string | null): DetailRow[] {
    if (!properties) {
        return [];
    }

    const text: DetailRow[] = [{ label: 'Properties', value: properties, mono: true, multiline: true }];

    try {
        const parsed: unknown = JSON.parse(properties);
        if (!parsed || Array.isArray(parsed) || typeof parsed !== 'object') {
            return text;
        }

        return Object.entries(parsed)
            .sort(([left], [right]) => left.localeCompare(right))
            .map(([key, value]) => ({
                key,
                label: key,
                value: value === null ? 'null' : typeof value === 'object' ? JSON.stringify(value) : String(value),
                mono: true,
            }));
    } catch {
        return text;
    }
}

export function ReleaseDetailSections({
    release,
    download,
    downloadLoading = false,
    clientName = '',
}: ReleaseDetailSectionsProps) {
    const properties = propertyRows(release.properties);

    return (
        <>
            <DetailsPanel variant="dialog" rows={releaseRows(release)} data-component="release-summary" />
            {download ? (
                <DetailsPanel
                    variant="dialog"
                    title="Download"
                    rows={downloadRows(download, clientName)}
                    data-component="release-download"
                />
            ) : null}
            {!download && downloadLoading ? (
                <section
                    className="release-detail-sections__download-loading"
                    data-component="release-download-loading"
                >
                    <h2 className="details-panel__title">Download</h2>
                    <Skeleton variant="text" width="70%" />
                    <Skeleton variant="text" width="55%" />
                    <Skeleton variant="text" width="80%" />
                </section>
            ) : null}
            <DetailsPanel
                variant="dialog"
                title="Indexer properties"
                rows={properties}
                data-component={
                    properties[0]?.key === undefined ? 'release-indexer-properties-text' : 'release-indexer-properties'
                }
            />
        </>
    );
}

ReleaseDetailSections.displayName = 'ReleaseDetailSections';

export default ReleaseDetailSections;
