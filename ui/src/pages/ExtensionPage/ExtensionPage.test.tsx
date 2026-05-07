import { render } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { describe, expect, it } from 'vitest';

import { ExtensionProvider } from '@/extensions';

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
});
