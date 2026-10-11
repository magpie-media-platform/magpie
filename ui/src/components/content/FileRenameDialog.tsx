import { useRef, useState, type RefObject } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import type { ColumnDef } from '@tanstack/react-table';

import { Button } from '@/components/ui/Button';
import { Checkbox } from '@/components/ui/Checkbox';
import { DataTable } from '@/components/ui/DataTable';
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
import { Select, SelectContent, SelectItem, SelectTrigger } from '@/components/ui/Select';
import { apiPost } from '@/lib/api';
import { returnFocusOnClose } from '@/lib/returnFocus';
import { contentQueryKeys } from '@/hooks/useContent';
import { RenameTokenReference } from './RenameTokenReference';
import './FileRenameDialog.css';

export interface FileRenameEntry {
    candidateId?: string | null;
    mediaFileId?: string | null;
    contentHierarchyItemId: string;
    originalPath: string;
    destinationPath?: string | null;
    error?: string | null;
    isSidecar: boolean;
    completed: boolean;
}

export interface FileRenamePreview {
    hasMask: boolean;
    files: FileRenameEntry[];
}

const columns: ColumnDef<FileRenameEntry>[] = [
    { accessorKey: 'originalPath', header: 'Original path' },
    {
        accessorKey: 'destinationPath',
        header: 'Destination path',
        cell: ({ row }) => row.original.destinationPath ?? 'Unavailable',
    },
    {
        id: 'outcome',
        header: 'Outcome',
        cell: ({ row }) => row.original.error ?? (row.original.completed ? 'Completed' : 'Ready'),
    },
];

export function RenamePreviewTable({ preview }: { preview: FileRenamePreview }) {
    return (
        <div className="file-rename-preview">
            <DataTable columns={columns} data={preview.files} enablePagination={false} manualPagination />
        </div>
    );
}

interface FileRenameDialogProps {
    contentId: string;
    filePath?: string;
    mediaFileIds?: string[];
    onAssociate?: (filePath: string) => Promise<{ mediaFile: { id: string } }>;
    onClose: () => void;

    /** Where focus goes when the dialog closes. Without it, the dialog keeps its default focus behaviour. */
    returnFocusRef?: RefObject<HTMLElement | null>;

    /** Where focus goes when the control in returnFocusRef is gone. */
    fallbackFocusRef?: RefObject<HTMLElement | null>;
}

export function FileRenameDialog({
    contentId,
    filePath,
    mediaFileIds,
    onAssociate,
    onClose,
    returnFocusRef,
    fallbackFocusRef,
}: FileRenameDialogProps) {
    const [applyMask, setApplyMask] = useState(false);
    const [outcome, setOutcome] = useState<FileRenamePreview>();
    const [tokenFileId, setTokenFileId] = useState<string>();
    const associationId = useRef<string | undefined>(undefined);
    const client = useQueryClient();
    const preview = useQuery({
        queryKey: ['file-rename-preview', contentId, filePath, mediaFileIds],
        queryFn: () =>
            apiPost<FileRenamePreview>(`/content/${contentId}/files/rename-preview`, { filePath, mediaFileIds }),
        staleTime: 0,
        refetchOnWindowFocus: false,
    });
    const execute = useMutation({
        mutationFn: async () => {
            if (!preview.data) throw new Error('The preview is not available.');
            let plan = preview.data;
            if (filePath && onAssociate) {
                if (!associationId.current) associationId.current = (await onAssociate(filePath)).mediaFile.id;
                if (!applyMask) return undefined;
                plan = {
                    ...plan,
                    files: plan.files.map((file) => ({
                        ...file,
                        mediaFileId: file.mediaFileId ?? associationId.current,
                    })),
                };
            }
            return apiPost<FileRenamePreview>(`/content/${contentId}/files/rename`, plan);
        },
        onSuccess: (result) => {
            void client.invalidateQueries({ queryKey: contentQueryKeys.all });
            if (result) setOutcome(result);
            else onClose();
        },
    });

    const tokenFiles = (preview.data?.files ?? []).filter((file) => file.mediaFileId && !file.isSidecar);
    const tokenFile = tokenFiles.find((file) => file.mediaFileId === tokenFileId) ?? tokenFiles[0];

    return (
        <Dialog open onOpenChange={(open) => !open && !execute.isPending && onClose()}>
            <DialogContent
                className="file-rename-dialog"
                onCloseAutoFocus={returnFocusOnClose(returnFocusRef, fallbackFocusRef)}
            >
                <DialogHeader>
                    <DialogTitle>{filePath ? 'Associate file' : 'Apply rename mask'}</DialogTitle>
                    <DialogDescription>
                        {filePath
                            ? 'Association links the existing file. Renaming is a separate optional operation.'
                            : 'Content folders are not renamed. Valid files can proceed when other files fail.'}
                    </DialogDescription>
                </DialogHeader>
                <DialogBody>
                    {filePath && <p>{filePath}</p>}
                    {filePath && preview.data?.hasMask && !outcome && (
                        <label>
                            <Checkbox
                                checked={applyMask}
                                onCheckedChange={(checked) => setApplyMask(checked === true)}
                                disabled={execute.isPending}
                            />{' '}
                            Apply rename mask
                        </label>
                    )}
                    {preview.isPending && <p>Loading rename preview…</p>}
                    {preview.error && <p role="alert">{preview.error.message}</p>}
                    {execute.error && <p role="alert">{execute.error.message}</p>}
                    {(outcome || ((!filePath || applyMask) && preview.data)) && (
                        <RenamePreviewTable preview={outcome ?? preview.data!} />
                    )}
                    {!outcome && tokenFile?.mediaFileId && (
                        <div className="file-rename-dialog__tokens">
                            <Label htmlFor="file-rename-token-file">Token values for</Label>
                            <Select value={tokenFile.mediaFileId} onValueChange={setTokenFileId}>
                                <SelectTrigger id="file-rename-token-file" data-field="token-file">
                                    {tokenFile.originalPath}
                                </SelectTrigger>
                                <SelectContent>
                                    {tokenFiles.map((file) => (
                                        <SelectItem key={file.mediaFileId} value={file.mediaFileId!}>
                                            {file.originalPath}
                                        </SelectItem>
                                    ))}
                                </SelectContent>
                            </Select>
                            <RenameTokenReference
                                request={{
                                    contentHierarchyItemId: tokenFile.contentHierarchyItemId,
                                    mediaFileId: tokenFile.mediaFileId,
                                }}
                            />
                        </div>
                    )}
                    {outcome && (
                        <p role="status">
                            {outcome.files.filter((file) => file.completed).length} completed;{' '}
                            {outcome.files.filter((file) => file.error).length} failed.
                        </p>
                    )}
                </DialogBody>
                <DialogFooter>
                    <Button variant="secondary" onClick={onClose} disabled={execute.isPending}>
                        {outcome ? 'Close' : 'Cancel'}
                    </Button>
                    {!outcome && (
                        <Button
                            loading={execute.isPending}
                            disabled={
                                !preview.data ||
                                execute.isPending ||
                                (!filePath && !preview.data.files.some((file) => !file.error))
                            }
                            onClick={() => execute.mutate()}
                        >
                            {filePath
                                ? applyMask
                                    ? 'Associate and apply valid renames'
                                    : 'Associate file'
                                : 'Apply valid renames'}
                        </Button>
                    )}
                </DialogFooter>
            </DialogContent>
        </Dialog>
    );
}
