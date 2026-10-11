import { useCallback, useState } from 'react';
import { FlagCheckeredIcon } from '@phosphor-icons/react';

import { DeleteConfirmDialog } from '@/components/ui/DeleteConfirmDialog';
import { MonitoringToggle } from '@/components/ui/MonitoringToggle';
import { useToast } from '@/components/ui/Toast';
import { useUpdateContent } from '@/hooks/useContent';
import type { MonitoringMode } from '@/types';

import { DetailInfoRow } from '@/components/ui/DetailInfoGrid';

interface MonitoringControlProps {
    contentId: string | undefined;
    monitoring: MonitoringMode | null;
    effectiveMonitoring: MonitoringMode;
    canEdit?: boolean;
    hasChildren?: boolean;
}

const UPDATED_DESCRIPTIONS: Record<string, string> = {
    inherit: 'This item now inherits monitoring from its parent.',
    Monitored: 'Monitoring set to monitored.',
    Unmonitored: 'Monitoring set to unmonitored.',
    Shotgun: 'Monitoring set to Shotgun.',
};

export function MonitoringControl({
    contentId,
    monitoring,
    effectiveMonitoring,
    canEdit = true,
    hasChildren = false,
}: MonitoringControlProps) {
    const updateMutation = useUpdateContent();
    const { toast } = useToast();
    const [confirmShotgun, setConfirmShotgun] = useState(false);

    const update = useCallback(
        (next: MonitoringMode | null) => {
            if (!contentId) {
                return;
            }
            updateMutation.mutate(
                { id: contentId, monitoring: next },
                {
                    onSuccess: () => {
                        toast({
                            title: 'Monitoring updated',
                            description: UPDATED_DESCRIPTIONS[next ?? 'inherit'],
                            variant: 'success',
                        });
                    },
                    onError: () => {
                        toast({
                            title: 'Update failed',
                            description: 'Failed to update monitoring. Please try again.',
                            variant: 'error',
                        });
                    },
                },
            );
        },
        [contentId, updateMutation, toast],
    );

    const handleChange = useCallback(
        (next: MonitoringMode | null) => {
            if (!contentId || updateMutation.isPending || next === monitoring) {
                return;
            }
            if (next === 'Shotgun') {
                setConfirmShotgun(true);
                return;
            }
            update(next);
        },
        [contentId, updateMutation.isPending, monitoring, update],
    );

    if (!contentId) {
        return null;
    }

    const effectiveValue = (
        <span
            data-monitored={effectiveMonitoring !== 'Unmonitored'}
            data-shotgun={effectiveMonitoring === 'Shotgun'}
            data-testid="monitoring-control__effective"
        >
            {effectiveMonitoring}
        </span>
    );

    return (
        <>
            <DetailInfoRow
                label="Monitoring"
                data-testid="monitoring-control"
                value={effectiveValue}
                control={
                    canEdit ? (
                        <MonitoringToggle
                            value={monitoring}
                            onChange={handleChange}
                            disabled={updateMutation.isPending}
                            testIdPrefix="monitoring-control"
                            shotgun
                        />
                    ) : undefined
                }
            />
            <DeleteConfirmDialog
                open={confirmShotgun}
                itemName=""
                itemType="Shotgun mode"
                title="Use Shotgun?"
                description={
                    <>
                        <span className="monitoring-control__confirm-paragraph">
                            Downloads every good release at once and keeps the first one that finishes. Faster, but uses
                            more bandwidth.
                        </span>
                        {hasChildren && (
                            <span className="monitoring-control__confirm-paragraph">
                                Also applies to everything inside it.
                            </span>
                        )}
                    </>
                }
                confirmVariant="warning"
                confirmLabel="Use Shotgun"
                confirmIcon={<FlagCheckeredIcon weight="bold" aria-hidden="true" />}
                onConfirm={() => {
                    setConfirmShotgun(false);
                    update('Shotgun');
                }}
                onCancel={() => setConfirmShotgun(false)}
            />
        </>
    );
}

MonitoringControl.displayName = 'MonitoringControl';

export default MonitoringControl;
