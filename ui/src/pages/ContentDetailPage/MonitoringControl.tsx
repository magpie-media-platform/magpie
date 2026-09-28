import { useCallback, useState } from 'react';
import { CrosshairIcon } from '@phosphor-icons/react';

import { monitoringStateLabel } from '@/components/content/ContentCardPrimitives';
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
            {monitoringStateLabel(effectiveMonitoring, effectiveMonitoring)}
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
                title="Enable Shotgun mode?"
                description={
                    <>
                        <span className="monitoring-control__confirm-paragraph">
                            Shotgun mode grabs every acceptable release at the same time. This uses more bandwidth and
                            download slots.
                        </span>
                        <span className="monitoring-control__confirm-paragraph">
                            Magpie keeps the first successful import and cancels the rest, unless they&apos;re a higher
                            quality.
                        </span>
                        <span className="monitoring-control__confirm-paragraph">
                            Inheriting items will also use Shotgun mode.
                        </span>
                    </>
                }
                confirmVariant="warning"
                confirmLabel="Enable Shotgun"
                confirmIcon={<CrosshairIcon weight="bold" aria-hidden="true" />}
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
