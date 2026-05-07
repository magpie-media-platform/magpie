import { createElement, type ReactNode, useCallback, useMemo } from 'react';

import { useExtensions, useSlotExtensions } from '@/extensions/useExtensions';
import type { UIExtension } from '@/extensions/types';
import { ErrorBoundary } from '@/components/ui/ErrorBoundary';
import { buildExtensionAttributes } from './buildExtensionAttributes';

import './Slot.css';

export interface SlotProps {
    slot: string;

    children?: ReactNode;

    className?: string;

    entityId?: string;

    entityType?: string;
}

interface ExtensionComponentProps {
    extension: UIExtension;
    apiBaseUrl: string;
    userId: string | null;
    username: string | null;
    role: string | null;
    entityId?: string;
    entityType?: string;
}

function ExtensionComponent({
    extension,
    apiBaseUrl,
    userId,
    username,
    role,
    entityId,
    entityType,
}: ExtensionComponentProps) {
    const attributes = buildExtensionAttributes({
        extension,
        apiBaseUrl,
        userId,
        username,
        role,
        entityId,
        entityType,
    });

    const mountId = [extension.id, entityId, entityType].filter(Boolean).join(':');

    return createElement(extension.tagName, {
        'data-slot-extension': mountId,
        ...attributes,
    });
}

ExtensionComponent.displayName = 'ExtensionComponent';

function groupByModifier(extensions: UIExtension[]): {
    before: UIExtension[];
    after: UIExtension[];
    replace: UIExtension | null;
} {
    const before: UIExtension[] = [];
    const after: UIExtension[] = [];
    let replace: UIExtension | null = null;

    for (const ext of extensions) {
        switch (ext.modifier) {
            case 'Before':
                before.push(ext);
                break;
            case 'After':
                after.push(ext);
                break;
            case 'Replace':
                if (!replace) {
                    replace = ext;
                } else {
                    console.warn(
                        `[Slot] Multiple Replace modifiers for slot "${ext.slot}": ` +
                            `"${replace.name}" (kept) and "${ext.name}" (ignored). ` +
                            `Only one Replace per slot is allowed.`,
                    );
                }
                break;
        }
    }

    return { before, after, replace };
}

export function Slot({ slot, children, className, entityId, entityType }: SlotProps) {
    const { apiBaseUrl, userId, username, role } = useExtensions();
    const { extensions, isReady, isLoading } = useSlotExtensions(slot);

    const { before, after, replace } = useMemo(() => groupByModifier(extensions), [extensions]);

    const renderExtension = useCallback(
        (ext: UIExtension, i: number) => (
            <ErrorBoundary key={`${[ext.id, entityId, entityType].filter(Boolean).join(':')}:${i}`}>
                <ExtensionComponent
                    extension={ext}
                    apiBaseUrl={apiBaseUrl}
                    userId={userId}
                    username={username}
                    role={role}
                    entityId={entityId}
                    entityType={entityType}
                />
            </ErrorBoundary>
        ),
        [apiBaseUrl, userId, username, role, entityId, entityType],
    );

    const classes = ['slot', className].filter(Boolean).join(' ');

    if (isLoading || !isReady) {
        return (
            <div className={classes} data-slot={slot} data-slot-state="loading">
                {children}
            </div>
        );
    }

    const defaultContent = replace ? renderExtension(replace, 0) : children;

    return (
        <div className={classes} data-slot={slot} data-slot-state="ready">
            {before.map(renderExtension)}
            {defaultContent}
            {after.map(renderExtension)}
        </div>
    );
}

Slot.displayName = 'Slot';

export default Slot;
