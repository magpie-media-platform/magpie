import { useCallback } from 'react';

import { MonitoringToggle } from '@/components/ui/MonitoringToggle';
import { useToast } from '@/components/ui/Toast';
import { useUpdateContent } from '@/hooks/useContent';

import { DetailInfoRow } from '@/components/ui/DetailInfoGrid';

interface MonitoringControlProps {
    contentId: string | undefined;
    monitoring: boolean | null;
    effectiveMonitoring: boolean;
    canEdit?: boolean;
}

export function MonitoringControl({
    contentId,
    monitoring,
    effectiveMonitoring,
    canEdit = true,
}: MonitoringControlProps) {
    const updateMutation = useUpdateContent();
    const { toast } = useToast();

    const handleChange = useCallback(
        (next: boolean | null) => {
            if (!contentId || updateMutation.isPending || next === monitoring) {
                return;
            }
            updateMutation.mutate(
                { id: contentId, monitoring: next },
                {
                    onSuccess: () => {
                        toast({
                            title: 'Monitoring updated',
                            description:
                                next === null
                                    ? 'This item now inherits monitoring from its parent.'
                                    : `Monitoring set to ${next ? 'monitored' : 'not monitored'}.`,
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
        [contentId, monitoring, updateMutation, toast],
    );

    if (!contentId) {
        return null;
    }

    const effectiveValue = (
        <span data-monitored={effectiveMonitoring} data-testid="monitoring-control__effective">
            {effectiveMonitoring ? 'Monitored' : 'Not monitored'}
        </span>
    );

    return (
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
                    />
                ) : undefined
            }
        />
    );
}

MonitoringControl.displayName = 'MonitoringControl';

export default MonitoringControl;
