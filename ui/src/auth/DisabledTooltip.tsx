import { cloneElement, useId, type ReactElement } from 'react';

import { Tooltip, TooltipProvider } from '@/components/ui/Tooltip';
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

    const child = props.children as ReactElement<{ 'aria-describedby'?: string }>;
    const existingDescription = child.props['aria-describedby'];
    const describedBy = [existingDescription, descriptionId].filter(Boolean).join(' ');
    const describedChild = cloneElement(child, { 'aria-describedby': describedBy });

    return (
        <TooltipProvider>
            <Tooltip content={props.message}>
                <span style={{ display: 'inline-block' }}>
                    {describedChild}
                    <span id={descriptionId} className="sr-only">
                        {props.message}
                    </span>
                </span>
            </Tooltip>
        </TooltipProvider>
    );
}

DisabledTooltip.displayName = 'DisabledTooltip';
