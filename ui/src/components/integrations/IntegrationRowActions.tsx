import { CircleNotchIcon, FlaskIcon, PencilSimpleIcon, PowerIcon, TrashIcon } from '@phosphor-icons/react';

import { Switch } from '@/components/ui/Switch';
import { Tooltip } from '@/components/ui/Tooltip';

interface IntegrationRowActionsProps {
    prefix: string;

    name: string;

    id: string;

    enabled: boolean;

    isTesting: boolean;

    onTest: () => void;

    onToggleEnabled: (enabled: boolean) => void;

    onEdit: () => void;

    onDelete: () => void;
}

export function IntegrationRowActions({
    prefix,
    name,
    id,
    enabled,
    isTesting,
    onTest,
    onToggleEnabled,
    onEdit,
    onDelete,
}: IntegrationRowActionsProps) {
    const btnClass = `${prefix}__action-btn card-actions__btn`;

    return (
        <div className={`${prefix}__item-actions card-actions`}>
            <Tooltip content="Test">
                <button
                    type="button"
                    className={btnClass}
                    data-testid={`${prefix}__action-btn--test`}
                    onClick={onTest}
                    disabled={isTesting}
                    aria-label={`Test ${name}`}
                    data-action="test"
                    data-target={id}
                >
                    {isTesting ? (
                        <CircleNotchIcon weight="bold" className="card-actions__spinner" />
                    ) : (
                        <FlaskIcon weight="bold" />
                    )}
                </button>
            </Tooltip>
            <div className={`${prefix}__item-toggle`} title={enabled ? 'Disable' : 'Enable'}>
                <PowerIcon weight="bold" className={`${prefix}__toggle-icon`} aria-hidden="true" />
                <Switch
                    checked={enabled}
                    onCheckedChange={onToggleEnabled}
                    aria-label={`${enabled ? 'Disable' : 'Enable'} ${name}`}
                    data-action="toggle-enabled"
                    data-target={id}
                />
            </div>
            <Tooltip content="Edit">
                <button
                    type="button"
                    className={btnClass}
                    data-testid={`${prefix}__action-btn--edit`}
                    onClick={onEdit}
                    aria-label={`Edit ${name}`}
                    data-action="edit"
                    data-target={id}
                >
                    <PencilSimpleIcon weight="bold" />
                </button>
            </Tooltip>
            <Tooltip content="Delete">
                <button
                    type="button"
                    className={`${prefix}__action-btn ${prefix}__action-btn--danger card-actions__btn card-actions__btn--danger`}
                    data-testid={`${prefix}__action-btn--delete`}
                    onClick={onDelete}
                    aria-label={`Delete ${name}`}
                    data-action="delete"
                    data-target={id}
                >
                    <TrashIcon weight="bold" />
                </button>
            </Tooltip>
        </div>
    );
}

IntegrationRowActions.displayName = 'IntegrationRowActions';
