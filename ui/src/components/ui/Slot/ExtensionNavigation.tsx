import type { ReactNode } from 'react';

import { useSlotExtensions } from '@/extensions/useExtensions';
import type { UIExtension } from '@/extensions/types';
import { CdnPhosphorIcon } from '@/components/ui/CdnPhosphorIcon';
import { SidebarGroup, SidebarItem, SidebarSeparator } from '@/components/ui/Sidebar';
import { useIsActive } from '@/lib/routes';

function navLabel(extension: UIExtension): string {
    return extension.attributes?.label?.trim() || extension.name;
}

function navHref(extension: UIExtension): string | null {
    const href = extension.attributes?.href?.trim();
    if (!href || !href.startsWith('/') || href.startsWith('//')) {
        return null;
    }

    try {
        const parsed = new URL(href, 'http://magpie.local');
        if (parsed.origin !== 'http://magpie.local') {
            return null;
        }

        return `${parsed.pathname}${parsed.search}${parsed.hash}`;
    } catch {
        return null;
    }
}

function navIcon(extension: UIExtension): ReactNode {
    return <CdnPhosphorIcon name={extension.attributes?.icon} weight="duotone" />;
}

function navItems(extensions: UIExtension[]): Array<{ extension: UIExtension; href: string }> {
    return extensions
        .filter((extension) => extension.modifier === 'After')
        .map((extension) => ({ extension, href: navHref(extension) }))
        .filter((item): item is { extension: UIExtension; href: string } => item.href !== null);
}

export interface ExtensionNavigationProps {
    onNavigate?: () => void;
}

interface ExtensionNavigationGroupProps extends ExtensionNavigationProps {
    slot: 'nav.main' | 'nav.admin';

    leading?: ReactNode;
}

function ExtensionNavigationGroup({ slot, onNavigate, leading }: ExtensionNavigationGroupProps) {
    const { extensions, isReady } = useSlotExtensions(slot);
    const isActive = useIsActive();
    const items = isReady ? navItems(extensions) : [];

    if (items.length === 0 && !leading) {
        return null;
    }

    return (
        <>
            <SidebarSeparator />
            <SidebarGroup label="Extensions">
                {leading}
                {items.map(({ extension, href }) => (
                    <SidebarItem
                        key={extension.id}
                        to={href}
                        icon={navIcon(extension)}
                        active={isActive(href)}
                        onClick={onNavigate}
                    >
                        {navLabel(extension)}
                    </SidebarItem>
                ))}
            </SidebarGroup>
        </>
    );
}

ExtensionNavigationGroup.displayName = 'ExtensionNavigationGroup';

export function ExtensionMainNavigation({ onNavigate }: ExtensionNavigationProps) {
    return <ExtensionNavigationGroup slot="nav.main" onNavigate={onNavigate} />;
}

ExtensionMainNavigation.displayName = 'ExtensionMainNavigation';

interface ExtensionAdminNavigationProps extends ExtensionNavigationProps {
    leading?: ReactNode;
}

export function ExtensionAdminNavigation({ onNavigate, leading }: ExtensionAdminNavigationProps) {
    return <ExtensionNavigationGroup slot="nav.admin" onNavigate={onNavigate} leading={leading} />;
}

ExtensionAdminNavigation.displayName = 'ExtensionAdminNavigation';
