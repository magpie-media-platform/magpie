import { useCallback } from 'react';

import { EyeIcon, EyeSlashIcon, TreeViewIcon } from '@phosphor-icons/react';

import { Tooltip } from '@/components/ui/Tooltip';
import { useToast } from '@/components/ui/Toast';
import { useUpdateContent } from '@/hooks/useContent';

import { ContentPolicyRow } from './ContentPolicySettings';

import './MonitoringToggle.css';

interface MonitoringControlProps {
    contentId: string | undefined;
    monitoring: boolean | null;
    effectiveMonitoring: boolean;
    canEdit?: boolean;
}

const OPTIONS: { value: boolean | null; key: string; label: string; icon: typeof EyeIcon; tooltip: string }[] = [
    {
        value: true,
        key: 'true',
        label: 'Monitor',
        icon: EyeIcon,
        tooltip: 'Monitor this item. The system searches and grabs for it',
    },
    {
        value: false,
        key: 'false',
        label: 'Do not monitor',
        icon: EyeSlashIcon,
        tooltip: 'Ignore this item. Background jobs will not pick it up',
    },
    {
        value: null,
        key: 'inherit',
        label: 'Inherit',
        icon: TreeViewIcon,
        tooltip: 'Inherit monitoring from a parent (no explicit decision)',
    },
];

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
        <ContentPolicyRow
            label="Monitoring"
            testId="monitoring-control"
            effectiveValue={effectiveValue}
            control={
                canEdit ? (
                    <div
                        className="monitoring-toggle__segments"
                        role="radiogroup"
                        aria-label="Monitoring override"
                        data-monitoring={monitoring === null ? 'inherit' : monitoring}
                        data-component="monitoring-toggle"
                        data-testid="monitoring-control__toggle"
                    >
                        {OPTIONS.map(({ value, key, label, icon: Icon, tooltip }) => {
                            const isActive = monitoring === value;
                            return (
                                <Tooltip key={key} content={tooltip}>
                                    <button
                                        type="button"
                                        role="radio"
                                        aria-checked={isActive}
                                        aria-label={`${label}: ${tooltip}`}
                                        className="monitoring-toggle__option"
                                        data-state={isActive ? 'active' : 'inactive'}
                                        data-value={key}
                                        data-testid={`monitoring-control__option-${key}`}
                                        onClick={() => handleChange(value)}
                                        disabled={updateMutation.isPending}
                                    >
                                        <Icon weight="bold" aria-hidden="true" />
                                        <span>{label}</span>
                                    </button>
                                </Tooltip>
                            );
                        })}
                    </div>
                ) : undefined
            }
        />
    );
}

MonitoringControl.displayName = 'MonitoringControl';

export default MonitoringControl;
