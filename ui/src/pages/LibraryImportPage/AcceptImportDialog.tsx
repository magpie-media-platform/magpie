import { useState } from 'react';

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
import type { LibraryImportMonitoringMode } from '@/hooks/useBulkAcceptLibraryImportCandidates';

import { ImportMonitoringSelect } from './ImportMonitoringSelect';

export type ImportAcceptAction = (monitoringMode: LibraryImportMonitoringMode) => void | Promise<void>;

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
    return (
        <Dialog open={action !== null} onOpenChange={(open) => !open && onClose()}>
            <DialogContent data-component="accept-import-dialog">
                <DialogHeader>
                    <DialogTitle>Accept import</DialogTitle>
                    <DialogDescription>Choose how Magpie monitors this import.</DialogDescription>
                </DialogHeader>
                <AcceptImportForm
                    initialMode={monitoringMode}
                    onCancel={onClose}
                    onAccept={(mode) => {
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
    initialMode: LibraryImportMonitoringMode | '';
    onCancel: () => void;
    onAccept: (mode: LibraryImportMonitoringMode) => void;
}

function AcceptImportForm({ initialMode, onCancel, onAccept }: AcceptImportFormProps) {
    const [selectedMode, setSelectedMode] = useState<LibraryImportMonitoringMode | ''>(initialMode);

    return (
        <>
            <DialogBody>
                <Label htmlFor="import-monitoring-select">Monitoring</Label>
                <ImportMonitoringSelect value={selectedMode} onChange={setSelectedMode} />
            </DialogBody>
            <DialogFooter>
                <Button variant="secondary" onClick={onCancel}>
                    Cancel
                </Button>
                <Button
                    variant="primary"
                    disabled={!selectedMode}
                    onClick={() => selectedMode && onAccept(selectedMode)}
                >
                    Accept
                </Button>
            </DialogFooter>
        </>
    );
}
