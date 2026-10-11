import { useLayoutEffect, useRef, useState, type RefObject } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';

import { Button } from '@/components/ui/Button';
import {
    Dialog,
    DialogBody,
    DialogContent,
    DialogDescription,
    DialogFooter,
    DialogHeader,
    DialogTitle,
} from '@/components/ui/Dialog';
import { Label } from '@/components/ui/Label';
import { Checkbox } from '@/components/ui/Checkbox';
import { RenamePreviewTable, type FileRenamePreview } from '@/components/content/FileRenameDialog';
import { apiPost } from '@/lib/api';
import { returnFocusOnClose } from '@/lib/returnFocus';
import type { LibraryImportSubmissionResult } from '@/hooks/useBulkAcceptLibraryImportCandidates';
import type { LibraryImportMonitoringMode } from '@/hooks/useBulkAcceptLibraryImportCandidates';
import { libraryImportCandidateQueryKeys } from '@/hooks/useLibraryImportCandidates';
import { libraryImportCandidateGroupQueryKeys } from '@/hooks/useLibraryImportCandidateGroups';
import { useInstanceName } from '@/hooks/useInstanceName';

import { ImportMonitoringSelect } from './ImportMonitoringSelect';

export interface ImportRenameSelection {
    ids?: string[];
    filters?: { rootFolderId?: string; groupKey?: string; excludeGroupKeys?: string[]; excludeIds?: string[] }[];
}

export type ImportAcceptAction = ((monitoringMode: LibraryImportMonitoringMode) => void | Promise<void>) & {
    renameSelection?: ImportRenameSelection;

    /** Where focus goes after Accept, and when the control that opened the dialog is gone. */
    fallbackFocusRef?: RefObject<HTMLElement | null>;
};

interface AcceptImportDialogProps {
    action: ImportAcceptAction | null;
    monitoringMode: LibraryImportMonitoringMode | '';
    onMonitoringModeChange: (mode: LibraryImportMonitoringMode) => void;
    onClose: () => void;
}

export function AcceptImportDialog({
    action,
    monitoringMode,
    onMonitoringModeChange,
    onClose,
}: AcceptImportDialogProps) {
    const instanceName = useInstanceName();
    const openerRef = useRef<HTMLElement | null>(null);
    const fallbackFocusRef = useRef<HTMLElement | null>(null);
    useLayoutEffect(() => {
        if (!action) return;
        openerRef.current = document.activeElement instanceof HTMLElement ? document.activeElement : null;
        fallbackFocusRef.current =
            openerRef.current?.closest<HTMLElement>('[role="dialog"]') ?? action.fallbackFocusRef?.current ?? null;
    }, [action]);
    return (
        <Dialog open={action !== null} onOpenChange={(open) => !open && onClose()}>
            <DialogContent
                data-component="accept-import-dialog"
                className="file-rename-dialog"
                onCloseAutoFocus={returnFocusOnClose(openerRef, fallbackFocusRef)}
            >
                <DialogHeader>
                    <DialogTitle>Accept import</DialogTitle>
                    <DialogDescription>Choose how {instanceName} monitors this import.</DialogDescription>
                </DialogHeader>
                <AcceptImportForm
                    renameSelection={action?.renameSelection}
                    initialMode={monitoringMode}
                    onCancel={onClose}
                    onMonitoringModeChange={onMonitoringModeChange}
                    onAccept={(mode) => {
                        if (action?.fallbackFocusRef) {
                            openerRef.current = null;
                        }
                        onMonitoringModeChange(mode);
                        onClose();
                        void action?.(mode);
                    }}
                />
            </DialogContent>
        </Dialog>
    );
}

AcceptImportDialog.displayName = 'AcceptImportDialog';

interface AcceptImportFormProps {
    renameSelection?: ImportRenameSelection;
    initialMode: LibraryImportMonitoringMode | '';
    onCancel: () => void;
    onAccept: (mode: LibraryImportMonitoringMode) => void;
    onMonitoringModeChange: (mode: LibraryImportMonitoringMode) => void;
}

function AcceptImportForm({
    initialMode,
    onCancel,
    onAccept,
    renameSelection,
    onMonitoringModeChange,
}: AcceptImportFormProps) {
    const [selectedMode, setSelectedMode] = useState<LibraryImportMonitoringMode | ''>(initialMode);
    const [applyMask, setApplyMask] = useState(false);
    const [submitted, setSubmitted] = useState<LibraryImportSubmissionResult>();
    const queryClient = useQueryClient();
    const preview = useQuery({
        queryKey: ['import-rename-preview', renameSelection],
        queryFn: () => apiPost<FileRenamePreview>('/import/rename-preview', renameSelection),
        enabled: renameSelection !== undefined,
        refetchOnWindowFocus: false,
        staleTime: 0,
    });
    const submitRenames = useMutation({
        mutationFn: () =>
            apiPost<LibraryImportSubmissionResult>('/import/candidates/bulk-accept', {
                ids: [...new Set(preview.data?.files.map((file) => file.candidateId).filter(Boolean))],
                monitoringMode: selectedMode,
                renamePreview: preview.data,
            }),
        onSuccess: (result) => {
            setSubmitted(result);
            if (selectedMode) onMonitoringModeChange(selectedMode);
            void queryClient.invalidateQueries({ queryKey: libraryImportCandidateQueryKeys.all });
            void queryClient.invalidateQueries({ queryKey: libraryImportCandidateGroupQueryKeys.all });
        },
    });

    return (
        <>
            <DialogBody>
                <Label htmlFor="import-monitoring-select">Monitoring</Label>
                <ImportMonitoringSelect value={selectedMode} onChange={setSelectedMode} />
                <p>
                    Association keeps existing file paths. Optional renaming runs separately and reports failures in the
                    import job.
                </p>
                {preview.data?.hasMask && !submitted && (
                    <label>
                        <Checkbox
                            checked={applyMask}
                            onCheckedChange={(checked) => setApplyMask(checked === true)}
                            disabled={submitRenames.isPending}
                        />{' '}
                        Apply rename mask
                    </label>
                )}
                {preview.error && <p role="alert">Rename preview unavailable: {preview.error.message}</p>}
                {preview.isFetching && <p>Loading rename preview…</p>}
                {applyMask && preview.data && <RenamePreviewTable preview={preview.data} />}
                {submitRenames.error && <p role="alert">{submitRenames.error.message}</p>}
                {submitted && (
                    <p role="status">
                        {submitted.queued.length} imports queued; {submitted.skipped.length} skipped. Check import jobs
                        for association and rename outcomes.
                    </p>
                )}
            </DialogBody>
            <DialogFooter>
                <Button variant="secondary" onClick={onCancel} disabled={submitRenames.isPending}>
                    {submitted ? 'Close' : 'Cancel'}
                </Button>
                {!submitted && (
                    <Button
                        variant="primary"
                        disabled={!selectedMode || submitRenames.isPending || (applyMask && !preview.data)}
                        onClick={() => selectedMode && (applyMask ? submitRenames.mutate() : onAccept(selectedMode))}
                    >
                        Accept
                    </Button>
                )}
            </DialogFooter>
        </>
    );
}
