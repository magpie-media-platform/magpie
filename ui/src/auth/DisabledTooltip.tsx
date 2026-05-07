/* eslint-disable react-refresh/only-export-components */
import type { ReactElement } from 'react';

import { Tooltip, TooltipProvider } from '@/components/ui/Tooltip';
import { useAuth } from './useAuth';
import { getRequiredRole, RoleName, Role, type Permission } from './types';

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

export type DisabledTooltipProps = DisabledTooltipPermissionProps | DisabledTooltipRoleProps;

function isPermissionProps(props: DisabledTooltipProps): props is DisabledTooltipPermissionProps {
    return 'action' in props;
}

export function DisabledTooltip(props: DisabledTooltipProps) {
    const { hasPermission, hasRole } = useAuth();

    const { children, message } = props;

    let allowed: boolean;
    let requiredRoleName: string;

    if (isPermissionProps(props)) {
        allowed = hasPermission(props.action);
        requiredRoleName = RoleName[getRequiredRole(props.action)];
    } else {
        allowed = hasRole(props.minRole);
        requiredRoleName = RoleName[props.minRole];
    }

    const tooltipMessage = message ?? `Requires ${requiredRoleName} role`;

    if (allowed) {
        return <>{children}</>;
    }

    return (
        <TooltipProvider>
            <Tooltip content={tooltipMessage}>
                <span style={{ display: 'inline-block' }}>{children}</span>
            </Tooltip>
        </TooltipProvider>
    );
}

DisabledTooltip.displayName = 'DisabledTooltip';

export function useDisabledState(options: { action: Permission } | { minRole: Role }): {
    disabled: boolean;
    tooltipMessage: string;
} {
    const { hasPermission, hasRole } = useAuth();

    let allowed: boolean;
    let requiredRoleName: string;

    if ('action' in options) {
        allowed = hasPermission(options.action);
        requiredRoleName = RoleName[getRequiredRole(options.action)];
    } else {
        allowed = hasRole(options.minRole);
        requiredRoleName = RoleName[options.minRole];
    }

    return {
        disabled: !allowed,
        tooltipMessage: `Requires ${requiredRoleName} role`,
    };
}
