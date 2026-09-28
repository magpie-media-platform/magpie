import { MagnifyingGlassIcon } from '@phosphor-icons/react';
import { LocalContentLink } from '@/components/content/LocalContentPreview';

import {
    Dialog,
    DialogContent,
    DialogHeader,
    DialogBody,
    DialogTitle,
    DialogDescription,
} from '@/components/ui/Dialog';
import { EmptyState } from '@/components/ui/EmptyState';
import { Skeleton } from '@/components/ui/Skeleton';
import { ContentLineageTitle } from '@/components/content/ContentLineageTitle';
import { ReleaseDetailSections } from '@/components/releases/ReleaseDetailSections';

import { type ApiDownload } from '@/hooks/useDownloads';
import { displayLineageLeafTitle } from '@/lib/downloadTitle';
import { getErrorMessage, isNotFoundError } from '@/lib/errors';

import './DownloadDetailsDialog.css';

export interface DownloadDetailsDialogProps {
    download: ApiDownload | null;

    selectedDownload?: ApiDownload | null;

    clientName?: string;

    isLoading?: boolean;

    error?: Error | null;

    open: boolean;

    onOpenChange: (open: boolean) => void;
}

export function DownloadDetailsDialog({
    download,
    selectedDownload = null,
    clientName = '',
    isLoading = false,
    error = null,
    open,
    onOpenChange,
}: DownloadDetailsDialogProps) {
    const identity = download ?? selectedDownload;
    const notFound = isNotFoundError(error);
    const emptyTitle = error
        ? notFound
            ? 'Download no longer exists'
            : 'Download details unavailable'
        : 'No active download found for this release';
    const emptyDescription = error
        ? notFound
            ? 'The selected download was removed.'
            : getErrorMessage(error, 'Failed to load the selected download.')
        : 'Retention can remove download records. Grab the release again to start a new download.';

    return (
        <Dialog open={open} onOpenChange={onOpenChange}>
            <DialogContent className="download-details-dialog" data-testid="download-details-dialog">
                <DialogHeader>
                    <DialogTitle>
                        {identity?.contentHierarchyItemId ? (
                            <LocalContentLink contentId={identity.contentHierarchyItemId}>
                                <ContentLineageTitle
                                    qualifiedName={{
                                        id: identity.contentHierarchyItemId,
                                        parentId: identity.contentHierarchyParentId,
                                        title: displayLineageLeafTitle(identity),
                                    }}
                                />
                            </LocalContentLink>
                        ) : identity ? (
                            displayLineageLeafTitle(identity)
                        ) : (
                            'Download details'
                        )}
                    </DialogTitle>
                    <DialogDescription>
                        {isLoading ? 'Loading the selected download.' : 'Download details'}
                    </DialogDescription>
                </DialogHeader>
                <DialogBody className="download-details-dialog__body">
                    {download ? (
                        <ReleaseDetailSections
                            release={{ title: download.title, infoUrl: download.releaseInfoUrl }}
                            download={download}
                            clientName={clientName}
                        />
                    ) : isLoading ? (
                        <div
                            className="download-details-dialog__loading"
                            data-testid="download-details-dialog__loading"
                        >
                            <Skeleton variant="text" width="60%" />
                            <Skeleton variant="text" width="90%" />
                            <Skeleton variant="text" width="80%" />
                            <Skeleton variant="text" width="70%" />
                        </div>
                    ) : (
                        <EmptyState
                            size="sm"
                            icon={<MagnifyingGlassIcon weight="thin" aria-hidden="true" />}
                            title={emptyTitle}
                            description={emptyDescription}
                            data-testid={error ? 'download-details-dialog__error' : 'download-details-dialog__empty'}
                        />
                    )}
                </DialogBody>
            </DialogContent>
        </Dialog>
    );
}

DownloadDetailsDialog.displayName = 'DownloadDetailsDialog';

export default DownloadDetailsDialog;
