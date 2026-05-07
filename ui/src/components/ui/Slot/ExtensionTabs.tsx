/* eslint-disable react-refresh/only-export-components */
import { createElement, useEffect, useRef } from 'react';
import { PuzzlePiece } from '@phosphor-icons/react';

import { useExtensions, useSlotExtensions } from '@/extensions/useExtensions';
import type { UIExtension } from '@/extensions/types';
import { ErrorBoundary } from '@/components/ui/ErrorBoundary';
import { TabsTrigger, TabsContent } from '@/components/ui/Tabs';
import { buildExtensionAttributes } from './buildExtensionAttributes';

function tabValue(ext: UIExtension): string {
    return `ext-${ext.id}`;
}

function tabLabel(ext: UIExtension): string {
    return ext.attributes?.['tab-label'] ?? ext.name;
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
}

export function ExtensionTabTriggers({ slot }: ExtensionTabsProps) {
    const { tabs } = useTabSlotExtensions(slot);

    if (tabs.length === 0) {
        return null;
    }

    return (
        <>
            {tabs.map((ext, i) => (
                <TabsTrigger key={`${ext.id}:${i}`} value={tabValue(ext)}>
                    <PuzzlePiece weight="bold" aria-hidden="true" />
                    <span>{tabLabel(ext)}</span>
                </TabsTrigger>
            ))}
        </>
    );
}

ExtensionTabTriggers.displayName = 'ExtensionTabTriggers';

export function ExtensionTabContents({ slot }: ExtensionTabsProps) {
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
