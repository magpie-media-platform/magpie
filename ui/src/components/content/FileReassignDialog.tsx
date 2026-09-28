import { useRef, useState, type RefObject } from 'react';
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
import { Select, SelectContent, SelectItem, SelectTrigger } from '@/components/ui/Select';
import { contentQueryKeys } from '@/hooks/useContent';
import { apiGet, apiPost, buildUrl } from '@/lib/api';
import { returnFocusOnClose } from '@/lib/returnFocus';
import type { MediaFile } from './ContentTabs/FilesTab';
import './FileReassignDialog.css';

interface Destination {
    id: string;
    name: string;
    ordinal?: number | null;
}

function compareDestinations(left: Destination, right: Destination): number {
    if (left.ordinal == null) {
        return right.ordinal == null ? left.id.localeCompare(right.id) : 1;
    }
    if (right.ordinal == null) {
        return -1;
    }
    return left.ordinal - right.ordinal || left.id.localeCompare(right.id);
}

function useDestinations(parentId: string | undefined) {
    return useQuery({
        queryKey: ['file-reassign-destinations', parentId],
        enabled: !!parentId,
        queryFn: async () => {
            const items: Destination[] = [];
            for (let pageNumber = 1; ; pageNumber++) {
                const page = await apiGet<{ items: Destination[]; totalItems: number }>(
                    buildUrl('/content', {
                        parentId,
                        pageNumber,
                        pageSize: 100,
                        sortBy: 'Ordinal',
                        sortDirection: 'Ascending',
                    }),
                );
                items.push(...page.items);
                if (items.length >= page.totalItems || page.items.length === 0) {
                    return items.sort(compareDestinations);
                }
            }
        },
    });
}

export function FileReassignDialog({
    contentId,
    showId,
    file,
    onClose,
    returnFocusRef,
    fallbackFocusRef,
}: {
    contentId: string;
    showId: string;
    file: MediaFile;
    onClose: () => void;
    returnFocusRef?: RefObject<HTMLElement | null>;
    fallbackFocusRef?: RefObject<HTMLElement | null>;
}) {
    const [seasonId, setSeasonId] = useState<string>();
    const [episodeId, setEpisodeId] = useState<string>();
    const seasons = useDestinations(showId);
    const episodes = useDestinations(seasonId);
    const client = useQueryClient();
    const correctedRef = useRef(false);
    const correction = useMutation({
        mutationFn: () =>
            apiPost(`/content/${contentId}/files/${file.id}/reassign`, {
                targetContentHierarchyItemId: episodeId,
                expectedFilePath: file.relativePath,
            }),
        onSuccess: () => {
            correctedRef.current = true;
            void client.invalidateQueries({ queryKey: contentQueryKeys.all });
            onClose();
        },
    });
    return (
        <Dialog open onOpenChange={(open) => !open && !correction.isPending && onClose()}>
            <DialogContent
                onCloseAutoFocus={(event) =>
                    returnFocusOnClose(
                        correctedRef.current ? fallbackFocusRef : returnFocusRef,
                        fallbackFocusRef,
                    )?.(event)
                }
            >
                <DialogHeader>
                    <DialogTitle>Correct episode association</DialogTitle>
                    <DialogDescription>
                        Select the library episode for this file. The file, media information, paths, and monitoring
                        choices are preserved.
                    </DialogDescription>
                </DialogHeader>
                <DialogBody>
                    <p className="file-reassign-dialog__file-name">{file.fileName}</p>
                    <Select
                        value={seasonId}
                        onValueChange={(value) => {
                            setSeasonId(value);
                            setEpisodeId(undefined);
                        }}
                        disabled={correction.isPending}
                    >
                        <SelectTrigger aria-label="Destination season" placeholder="Select season" />
                        <SelectContent>
                            {seasons.data?.map((season) => (
                                <SelectItem key={season.id} value={season.id}>
                                    {season.ordinal != null ? `${season.ordinal}: ` : ''}
                                    {season.name}
                                </SelectItem>
                            ))}
                        </SelectContent>
                    </Select>
                    <Select value={episodeId} onValueChange={setEpisodeId} disabled={!seasonId || correction.isPending}>
                        <SelectTrigger aria-label="Destination episode" placeholder="Select episode" />
                        <SelectContent>
                            {episodes.data?.map((episode) => (
                                <SelectItem key={episode.id} value={episode.id}>
                                    {episode.ordinal != null ? `${episode.ordinal}: ` : ''}
                                    {episode.name}
                                </SelectItem>
                            ))}
                        </SelectContent>
                    </Select>
                    {(seasons.isLoading || episodes.isLoading) && <p role="status">Loading destinations…</p>}
                    {seasons.data?.length === 0 && (
                        <p>No seasons are available. Add the missing library content before correction.</p>
                    )}
                    {seasonId && episodes.data?.length === 0 && <p>No episodes are available in this season.</p>}
                    {(correction.error || seasons.error || episodes.error) && (
                        <p role="alert">{(correction.error ?? seasons.error ?? episodes.error)?.message}</p>
                    )}
                </DialogBody>
                <DialogFooter>
                    <Button variant="secondary" onClick={onClose} disabled={correction.isPending}>
                        Cancel
                    </Button>
                    <Button
                        onClick={() => correction.mutate()}
                        disabled={!episodeId || correction.isPending}
                        loading={correction.isPending}
                    >
                        Correct association
                    </Button>
                </DialogFooter>
            </DialogContent>
        </Dialog>
    );
}
