import { createElement, useEffect, useRef } from 'react';

import { useExtensions, useSlotExtensions } from '@/extensions/useExtensions';
import type { UIExtension } from '@/extensions/types';
import { ErrorBoundary } from '@/components/ui/ErrorBoundary';
import { TabsContent } from '@/components/ui/Tabs';
import { buildExtensionAttributes } from './buildExtensionAttributes';

function tabValue(ext: UIExtension): string {
    return `ext-${ext.id}`;
}

export function useTabSlotExtensions(slot: string) {
    const warnedRef = useRef(false);

    useEffect(() => {
        if (import.meta.env.DEV && !slot.endsWith('.tabs') && !warnedRef.current) {
            warnedRef.current = true;
            console.warn(
                `useTabSlotExtensions: slot "${slot}" does not end with ".tabs". ` +
                    `Tab slots must follow the convention "section.context.tabs".`,
            );
        }
    }, [slot]);

    const { isReady, isLoading, extensions } = useSlotExtensions(slot);

    const tabs = isReady ? extensions.filter((ext) => ext.modifier === 'After') : [];

    return { tabs, isReady, isLoading };
}

export interface ExtensionTabsProps {
    slot: string;

    entityId?: string;

    entityType?: string;
}

export function ExtensionTabContents({ slot, entityId, entityType }: ExtensionTabsProps) {
    const { tabs } = useTabSlotExtensions(slot);
    const { apiBaseUrl, userId, username, role } = useExtensions();

    if (tabs.length === 0) {
        return null;
    }

    return (
        <>
            {tabs.map((ext, i) => {
                const attributes = buildExtensionAttributes({
                    extension: ext,
                    apiBaseUrl,
                    userId,
                    username,
                    role,
                    entityId,
                    entityType,
                });

                return (
                    <TabsContent key={`${ext.id}:${i}`} value={tabValue(ext)}>
                        <ErrorBoundary>
                            {createElement(ext.tagName, {
                                'data-slot-extension': ext.id,
                                ...attributes,
                            })}
                        </ErrorBoundary>
                    </TabsContent>
                );
            })}
        </>
    );
}

ExtensionTabContents.displayName = 'ExtensionTabContents';
