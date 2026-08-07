import {
    Dialog,
    DialogBody,
    DialogContent,
    DialogDescription,
    DialogHeader,
    DialogTitle,
} from '@/components/ui/Dialog';
import { ReleaseDetailSections } from '@/components/releases/ReleaseDetailSections';
import type { ApiDownload } from '@/hooks/useDownloads';

import './ReleaseDetailsDialog.css';

export interface ReleaseDetails {
    title: string;
    indexerName?: string | null;
    protocol: string;
    size?: number | null;
    publishDate: string;
    infoUrl?: string | null;
    externalId?: string | null;
    categories?: string[] | null;
    languages?: string[] | null;
    properties?: string | null;
}

export interface ReleaseDetailsDialogProps {
    release: ReleaseDetails | null;
    download?: ApiDownload | null;
    downloadLoading?: boolean;
    clientName?: string;
    open: boolean;
    onOpenChange: (open: boolean) => void;
}

export function ReleaseDetailsDialog({
    release,
    download,
    downloadLoading,
    clientName,
    open,
    onOpenChange,
}: ReleaseDetailsDialogProps) {
    if (!release) {
        return null;
    }

    return (
        <Dialog open={open} onOpenChange={onOpenChange}>
            <DialogContent className="release-details-dialog" data-testid="release-details-dialog">
                <DialogHeader>
                    <DialogTitle>{release.title}</DialogTitle>
                    <DialogDescription>Release details</DialogDescription>
                </DialogHeader>
                <DialogBody className="release-details-dialog__body">
                    <ReleaseDetailSections
                        release={release}
                        download={download}
                        downloadLoading={downloadLoading}
                        clientName={clientName}
                    />
                </DialogBody>
            </DialogContent>
        </Dialog>
    );
}

ReleaseDetailsDialog.displayName = 'ReleaseDetailsDialog';

export default ReleaseDetailsDialog;
