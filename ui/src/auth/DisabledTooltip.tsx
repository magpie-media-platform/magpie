import { useId, type ReactElement } from 'react';

import { Tooltip } from '@/components/ui/Tooltip';
import { useAuth } from './useAuth';
import { RoleName, Role, type Permission } from './types';

export interface DisabledTooltipPermissionProps {
    action: Permission;

    children: ReactElement;

    message?: string;
}

export interface DisabledTooltipRoleProps {
    minRole: Role;

    children: ReactElement;

    message?: string;
}

interface DisabledTooltipStateProps {
    disabled: boolean;

    children: ReactElement;

    message: string;
}

export type DisabledTooltipProps =
    | DisabledTooltipPermissionProps
    | DisabledTooltipRoleProps
    | DisabledTooltipStateProps;

function isPermissionProps(
    props: DisabledTooltipPermissionProps | DisabledTooltipRoleProps,
): props is DisabledTooltipPermissionProps {
    return 'action' in props;
}

export function useDisabledState(options: { action: Permission } | { minRole: Role }): {
    disabled: boolean;
    tooltipMessage: string;
} {
    const { hasPermission, hasRole, getRequiredRole } = useAuth();

    let allowed: boolean;
    let requiredRoleName: string | undefined;

    if ('action' in options) {
        allowed = hasPermission(options.action);
        const required = getRequiredRole(options.action);
        requiredRoleName = required !== undefined ? RoleName[required] : undefined;
    } else {
        allowed = hasRole(options.minRole);
        requiredRoleName = RoleName[options.minRole];
    }

    return {
        disabled: !allowed,
        tooltipMessage: requiredRoleName ? `Requires ${requiredRoleName} role` : 'Requires additional permissions',
    };
}

function AuthDisabledTooltip(props: DisabledTooltipPermissionProps | DisabledTooltipRoleProps) {
    const { disabled, tooltipMessage: requiredMessage } = useDisabledState(
        isPermissionProps(props) ? { action: props.action } : { minRole: props.minRole },
    );

    return (
        <DisabledTooltip disabled={disabled} message={props.message ?? requiredMessage}>
            {props.children}
        </DisabledTooltip>
    );
}

export function DisabledTooltip(props: DisabledTooltipProps) {
    const descriptionId = useId();

    if (!('disabled' in props)) {
        return <AuthDisabledTooltip {...props} />;
    }

    if (!props.disabled) {
        return <>{props.children}</>;
    }

    return (
        <>
            <Tooltip content={props.message}>
                {/* The wrapped control is disabled and cannot take focus, so the span carries the tab stop and the description. */}
                <span style={{ display: 'inline-block' }} tabIndex={0} aria-describedby={descriptionId}>
                    {props.children}
                </span>
            </Tooltip>
            <span id={descriptionId} className="sr-only">
                {props.message}
            </span>
        </>
    );
}

DisabledTooltip.displayName = 'DisabledTooltip';
