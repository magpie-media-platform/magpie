import { useEffect, useState, type ReactNode } from 'react';
import { LocalContentLink, LocalContentItemReference } from '@/components/content/LocalContentPreview';

import { Badge } from '@/components/ui/Badge';
import {
    Dialog,
    DialogBody,
    DialogContent,
    DialogDescription,
    DialogHeader,
    DialogTitle,
} from '@/components/ui/Dialog';
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/Tabs';
import { MagpieActivityEntryKind, type MagpieActivityEntry } from '@/hooks/useMagpieActivity';
import { formatDateTime } from '@/lib/dateFormatting';
import { getMagpieActivityStatusBadge } from '@/lib/magpieActivityStatusBadge';

import { RelatedEventsChain } from './RelatedEventsChain';
import { entryLabel } from './entryLabel';

import './RelatedEventsModal.css';

export interface RelatedEventsModalProps {
    executionId: string | null;

    correlationId: string | null;

    selfEntry: MagpieActivityEntry | null;

    seedEntityId?: string | null;

    open: boolean;
    onOpenChange: (open: boolean) => void;
}

export function RelatedEventsModal({
    executionId,
    correlationId,
    selfEntry,
    seedEntityId = null,
    open,
    onOpenChange,
}: RelatedEventsModalProps): ReactNode {
    const [view, setView] = useState('details');

    useEffect(() => {
        setView('details');
    }, [open, selfEntry?.kind, selfEntry?.id]);

    const isJob = selfEntry?.kind === MagpieActivityEntryKind.JobExecution;
    const { variant: statusVariant, label: statusLabel } = getMagpieActivityStatusBadge(selfEntry?.status);

    return (
        <Dialog open={open} onOpenChange={onOpenChange}>
            <DialogContent className="related-events" data-component="related-events-modal">
                <DialogHeader>
                    <DialogTitle>Activity detail</DialogTitle>
                    <DialogDescription>Review this activity or the activity related to it.</DialogDescription>
                </DialogHeader>

                <Tabs value={view} onValueChange={setView} className="related-events__tabs">
                    <TabsList className="related-events__tabs-list">
                        <TabsTrigger value="details">Details</TabsTrigger>
                        <TabsTrigger value="trace">Related activity</TabsTrigger>
                    </TabsList>

                    <TabsContent value="details" asChild>
                        <DialogBody className="related-events__tab-content">
                            {selfEntry && (
                                <div className="related-events__self" data-testid="related-events__self">
                                    <h3 className="related-events__self-title">{entryLabel(selfEntry)}</h3>
                                    {selfEntry.entityId && selfEntry.entityType === 'ContentHierarchyItem' && (
                                        <LocalContentLink contentId={selfEntry.entityId}>
                                            Open content preview
                                        </LocalContentLink>
                                    )}
                                    {selfEntry.entityId && selfEntry.entityType === 'ContentItem' && (
                                        <LocalContentItemReference contentItemId={selfEntry.entityId}>
                                            Open content preview
                                        </LocalContentItemReference>
                                    )}
                                    <div className="related-events__self-meta">
                                        {isJob && selfEntry.status ? (
                                            <Badge variant={statusVariant} size="sm">
                                                {statusLabel}
                                            </Badge>
                                        ) : (
                                            <Badge variant="default" size="sm">
                                                Recorded
                                            </Badge>
                                        )}
                                        <Badge variant="info" size="sm" badgeStyle="outline">
                                            {selfEntry.category}
                                        </Badge>
                                        <span className="related-events__self-time">
                                            {formatDateTime(selfEntry.timestamp)}
                                        </span>
                                    </div>
                                    {selfEntry.detail ? (
                                        <p className="related-events__self-detail">{selfEntry.detail}</p>
                                    ) : (
                                        <p className="related-events__self-detail related-events__self-detail--empty">
                                            No additional detail was recorded.
                                        </p>
                                    )}
                                </div>
                            )}
                        </DialogBody>
                    </TabsContent>

                    <TabsContent value="trace" asChild>
                        <DialogBody className="related-events__tab-content">
                            <RelatedEventsChain
                                executionId={executionId}
                                correlationId={correlationId}
                                active={open && view === 'trace'}
                                selfEntry={selfEntry}
                                seedEntityId={seedEntityId}
                            />
                        </DialogBody>
                    </TabsContent>
                </Tabs>
            </DialogContent>
        </Dialog>
    );
}

RelatedEventsModal.displayName = 'RelatedEventsModal';

export default RelatedEventsModal;
