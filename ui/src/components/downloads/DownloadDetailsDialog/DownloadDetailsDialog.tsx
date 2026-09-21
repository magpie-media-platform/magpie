import { MagnifyingGlassIcon } from '@phosphor-icons/react';

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

import './DownloadDetailsDialog.css';

export interface DownloadDetailsDialogProps {
    download: ApiDownload | null;

    clientName?: string;

    isLoading?: boolean;

    open: boolean;

    onOpenChange: (open: boolean) => void;
}

export function DownloadDetailsDialog({
    download,
    clientName = '',
    isLoading = false,
    open,
    onOpenChange,
}: DownloadDetailsDialogProps) {
    if (!download) {
        if (!open) {
            return null;
        }
        return (
            <Dialog open={open} onOpenChange={onOpenChange}>
                <DialogContent className="download-details-dialog" data-testid="download-details-dialog">
                    <DialogHeader>
                        <DialogTitle>Download details</DialogTitle>
                        <DialogDescription>
                            {isLoading
                                ? 'Loading the latest download for this release.'
                                : 'No download for this release.'}
                        </DialogDescription>
                    </DialogHeader>
                    <DialogBody>
                        {isLoading ? (
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
                                title="No active download found for this release"
                                description="Retention can remove download records. Grab the release again to start a new download."
                                data-testid="download-details-dialog__empty"
                            />
                        )}
                    </DialogBody>
                </DialogContent>
            </Dialog>
        );
    }
    return (
        <Dialog open={open} onOpenChange={onOpenChange}>
            <DialogContent className="download-details-dialog" data-testid="download-details-dialog">
                <DialogHeader>
                    <DialogTitle>
                        {download.contentHierarchyItemId ? (
                            <ContentLineageTitle
                                qualifiedName={{
                                    id: download.contentHierarchyItemId,
                                    parentId: download.contentHierarchyParentId,
                                    title: displayLineageLeafTitle(download),
                                }}
                            />
                        ) : (
                            displayLineageLeafTitle(download)
                        )}
                    </DialogTitle>
                    <DialogDescription>Download details</DialogDescription>
                </DialogHeader>
                <DialogBody className="download-details-dialog__body">
                    <ReleaseDetailSections
                        release={{ title: download.title, infoUrl: download.releaseInfoUrl }}
                        download={download}
                        clientName={clientName}
                    />
                </DialogBody>
            </DialogContent>
        </Dialog>
    );
}

DownloadDetailsDialog.displayName = 'DownloadDetailsDialog';

export default DownloadDetailsDialog;
