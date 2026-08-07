import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import type { ReactNode } from 'react';

import type { UIExtension } from '@/extensions/types';
import { Sidebar, SidebarContent, SidebarItem } from '@/components/ui/Sidebar';

import { ExtensionAdminNavigation, ExtensionMainNavigation } from './ExtensionNavigation';

let mockExtensionsBySlot: Record<string, UIExtension[]> = {};
let mockIsReady = true;

vi.mock('@/extensions/useExtensions', () => ({
    useSlotExtensions: (slot: string) => ({
        extensions: mockExtensionsBySlot[slot] ?? [],
        isReady: mockIsReady,
        isLoading: false,
    }),
}));

function makeExtension(overrides: Partial<UIExtension> = {}): UIExtension {
    return {
        id: 'nav-1',
        extensionId: 'ext-1',
        name: 'Provider A',
        slot: overrides.slot ?? 'nav.admin',
        modifier: 'After',
        tagName: 'magpie-nav-item',
        priority: 100,
        attributes: {
            label: 'Provider A',
            href: '/admin/ext/provider-a',
            icon: 'database',
        },
        ...overrides,
    };
}

function renderNavigation(path = '/admin/general', onNavigate?: () => void, leading?: ReactNode) {
    render(
        <MemoryRouter initialEntries={[path]}>
            <Sidebar>
                <SidebarContent>
                    <ExtensionAdminNavigation onNavigate={onNavigate} leading={leading} />
                </SidebarContent>
            </Sidebar>
        </MemoryRouter>,
    );
}

function renderMainNavigation(path = '/', onNavigate?: () => void) {
    render(
        <MemoryRouter initialEntries={[path]}>
            <Sidebar>
                <SidebarContent>
                    <ExtensionMainNavigation onNavigate={onNavigate} />
                </SidebarContent>
            </Sidebar>
        </MemoryRouter>,
    );
}

describe('ExtensionAdminNavigation', () => {
    beforeEach(() => {
        mockExtensionsBySlot = {};
        mockIsReady = true;
    });

    it('renders admin nav items declared by extensions', () => {
        mockExtensionsBySlot['nav.admin'] = [makeExtension()];

        renderNavigation('/admin/ext/provider-a');

        const link = screen.getByRole('link', { name: 'Provider A' });
        expect(link).toHaveAttribute('href', '/admin/ext/provider-a');
        expect(link).toHaveAttribute('data-active', 'true');
    });

    it('ignores nav declarations without an internal href', () => {
        mockExtensionsBySlot['nav.admin'] = [
            makeExtension({
                id: 'invalid',
                attributes: {
                    label: 'Invalid',
                    href: 'https://example.invalid',
                },
            }),
        ];

        renderNavigation();

        expect(screen.queryByRole('link')).not.toBeInTheDocument();
    });

    it('ignores protocol-relative extension nav hrefs', () => {
        mockExtensionsBySlot['nav.admin'] = [
            makeExtension({
                id: 'protocol-relative',
                attributes: {
                    label: 'Invalid',
                    href: '//example.invalid/admin',
                },
            }),
        ];

        renderNavigation();

        expect(screen.queryByRole('link')).not.toBeInTheDocument();
    });

    it('calls onNavigate when an extension nav item is clicked', async () => {
        const onNavigate = vi.fn();
        const user = userEvent.setup();
        mockExtensionsBySlot['nav.admin'] = [makeExtension()];

        renderNavigation('/admin/general', onNavigate);

        await user.click(screen.getByRole('link', { name: 'Provider A' }));

        expect(onNavigate).toHaveBeenCalledOnce();
    });

    it('renders the leading item even with no extension nav items', () => {
        renderNavigation('/admin/extensions', undefined, <SidebarItem to="/admin/extensions">Extensions</SidebarItem>);

        expect(screen.getByRole('link', { name: 'Extensions' })).toBeInTheDocument();
    });

    it('renders the leading item above extension nav items', () => {
        mockExtensionsBySlot['nav.admin'] = [makeExtension()];

        renderNavigation('/admin/extensions', undefined, <SidebarItem to="/admin/extensions">Extensions</SidebarItem>);

        const links = screen.getAllByRole('link');
        expect(links[0]).toHaveTextContent('Extensions');
        expect(links[1]).toHaveTextContent('Provider A');
    });
});

describe('ExtensionMainNavigation', () => {
    beforeEach(() => {
        mockExtensionsBySlot = {};
        mockIsReady = true;
    });

    it('renders main nav items declared by extensions', () => {
        mockExtensionsBySlot['nav.main'] = [
            makeExtension({
                id: 'main-nav',
                name: 'Diagnostics',
                slot: 'nav.main',
                attributes: {
                    label: 'Diagnostics',
                    href: '/ext/ui-diagnostics',
                    icon: 'plug',
                },
            }),
        ];

        renderMainNavigation('/ext/ui-diagnostics');

        const link = screen.getByRole('link', { name: 'Diagnostics' });
        expect(link).toHaveAttribute('href', '/ext/ui-diagnostics');
        expect(link).toHaveAttribute('data-active', 'true');
    });

    it('does not render admin nav declarations in the main nav', () => {
        mockExtensionsBySlot['nav.admin'] = [makeExtension()];

        renderMainNavigation('/');

        expect(screen.queryByRole('link', { name: 'Provider A' })).not.toBeInTheDocument();
    });
});
