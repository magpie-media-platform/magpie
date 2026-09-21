import { render, screen } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { describe, expect, it } from 'vitest';

import { ExtensionProvider } from '@/extensions';
import { ExtensionContext, type ExtensionContextValue } from '@/extensions/ExtensionProvider';
import type { UIExtension } from '@/extensions/types';

import { ExtensionPage } from './ExtensionPage';

function renderWithRoute(path: string, admin = false) {
    const routePath = admin ? '/admin/ext/:slug/*' : '/ext/:slug/*';
    return render(
        <MemoryRouter initialEntries={[path]}>
            <ExtensionProvider>
                <Routes>
                    <Route path={routePath} element={<ExtensionPage admin={admin} />} />
                </Routes>
            </ExtensionProvider>
        </MemoryRouter>,
    );
}

function makeReadyContext(extensionsBySlot: Record<string, UIExtension[]>): ExtensionContextValue {
    return {
        state: { state: 'ready', error: null, manifest: { assets: [], extensions: [] } },
        retriesExhausted: false,
        context: { apiBaseUrl: '/api', userId: 'user-1', username: 'user', role: 'Admin' },
        getExtensionsForSlot: (slot: string) => extensionsBySlot[slot] ?? [],
        retry: () => Promise.resolve(),
    };
}

function renderReady(path: string, context: ExtensionContextValue, admin = false) {
    const routePath = admin ? '/admin/ext/:slug/*' : '/ext/:slug/*';
    return render(
        <MemoryRouter initialEntries={[path]}>
            <ExtensionContext.Provider value={context}>
                <Routes>
                    <Route path={routePath} element={<ExtensionPage admin={admin} />} />
                </Routes>
            </ExtensionContext.Provider>
        </MemoryRouter>,
    );
}

describe('ExtensionPage', () => {
    it('renders with data-page attribute', () => {
        renderWithRoute('/ext/my-plugin');

        expect(document.querySelector('[data-page="extension"]')).toBeInTheDocument();
    });

    it('sets the slug as data attribute', () => {
        renderWithRoute('/ext/my-plugin');

        expect(document.querySelector('[data-extension-slug="my-plugin"]')).toBeInTheDocument();
    });

    it('uses route.<slug> slot for non-admin pages', () => {
        renderWithRoute('/ext/my-plugin');

        expect(document.querySelector('[data-slot="route.my-plugin"]')).toBeInTheDocument();
    });

    it('uses admin.route.<slug> slot for admin pages', () => {
        renderWithRoute('/admin/ext/my-plugin', true);

        expect(document.querySelector('[data-slot="admin.route.my-plugin"]')).toBeInTheDocument();
    });

    it('renders an empty state when the extension provides no route content', () => {
        renderReady('/ext/my-plugin', makeReadyContext({}));

        const emptyState = document.querySelector('[data-component="empty-state"]');
        expect(emptyState).toBeInTheDocument();
        expect(screen.getByText('This extension does not provide a page here.')).toBeInTheDocument();
        expect(document.querySelector('[data-slot="route.my-plugin"]')).not.toBeInTheDocument();
    });

    it('titles the empty state with the extension nav label when available', () => {
        const navItem: UIExtension = {
            id: 'nav-1',
            extensionId: 'ext-1',
            name: 'my-plugin',
            slot: 'nav.main',
            modifier: 'After',
            tagName: 'x-nav',
            priority: 0,
            attributes: { href: '/ext/my-plugin', label: 'My Plugin' },
        };

        renderReady('/ext/my-plugin', makeReadyContext({ 'nav.main': [navItem] }));

        expect(screen.getByRole('heading', { name: 'My Plugin', level: 1 })).toBeInTheDocument();
    });

    it('renders exactly one host-shell h1 for an admin extension settings page', () => {
        const navItem: UIExtension = {
            id: 'nav-1',
            extensionId: 'ext-1',
            name: 'provider-a',
            slot: 'nav.admin',
            modifier: 'After',
            tagName: 'x-nav',
            priority: 0,
            attributes: { href: '/admin/ext/provider-a', label: 'Provider A' },
        };
        const routeContent: UIExtension = {
            id: 'route-1',
            extensionId: 'ext-1',
            name: 'provider-a-settings',
            slot: 'admin.route.provider-a',
            modifier: 'After',
            tagName: 'x-provider-a-settings',
            priority: 0,
            attributes: null,
        };

        renderReady(
            '/admin/ext/provider-a',
            makeReadyContext({ 'nav.admin': [navItem], 'admin.route.provider-a': [routeContent] }),
            true,
        );

        const headings = screen.getAllByRole('heading', { level: 1 });
        expect(headings).toHaveLength(1);
        expect(headings[0]).toHaveTextContent('Provider A Settings');
    });

    it('renders extension content instead of the empty state when the slot has content', () => {
        const routeContent: UIExtension = {
            id: 'route-1',
            extensionId: 'ext-1',
            name: 'my-plugin-page',
            slot: 'route.my-plugin',
            modifier: 'Replace',
            tagName: 'x-my-plugin',
            priority: 0,
            attributes: null,
        };

        renderReady('/ext/my-plugin', makeReadyContext({ 'route.my-plugin': [routeContent] }));

        expect(document.querySelector('[data-slot="route.my-plugin"]')).toBeInTheDocument();
        expect(document.querySelector('[data-component="empty-state"]')).not.toBeInTheDocument();
    });
});
