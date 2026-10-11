import { renderHook, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { createElement, type ReactNode } from 'react';
import { describe, expect, it, afterEach, beforeEach, vi } from 'vitest';

import { useDocumentTitle } from './useDocumentTitle';
import { InstanceNameProvider } from './useInstanceName';

let queryClient: QueryClient;

function InstanceNameWrapper({ children }: { children: ReactNode }) {
    return createElement(
        QueryClientProvider,
        { client: queryClient },
        createElement(InstanceNameProvider, null, children),
    );
}

describe('useDocumentTitle', () => {
    beforeEach(() => {
        queryClient = new QueryClient({ defaultOptions: { queries: { retry: false, gcTime: 0 } } });
    });

    afterEach(() => {
        document.title = '';
        vi.unstubAllGlobals();
    });

    it('sets the document title with the base suffix', () => {
        renderHook(() => useDocumentTitle('Dashboard'));
        expect(document.title).toBe('Dashboard - Magpie');
    });

    it('sets just the base title when no title is provided', () => {
        renderHook(() => useDocumentTitle());
        expect(document.title).toBe('Magpie');
    });

    it('sets just the base title when title is undefined', () => {
        renderHook(() => useDocumentTitle(undefined));
        expect(document.title).toBe('Magpie');
    });

    it('updates the title when the argument changes', () => {
        const { rerender } = renderHook(({ title }) => useDocumentTitle(title), {
            initialProps: { title: 'Dashboard' },
        });
        expect(document.title).toBe('Dashboard - Magpie');

        rerender({ title: 'Settings' });
        expect(document.title).toBe('Settings - Magpie');
    });

    it('restores the base title on unmount', () => {
        const { unmount } = renderHook(() => useDocumentTitle('Dashboard'));
        expect(document.title).toBe('Dashboard - Magpie');

        unmount();
        expect(document.title).toBe('Magpie');
    });

    it('uses the instance name as the base title when the instance name is set', async () => {
        vi.stubGlobal(
            'fetch',
            vi.fn(
                async () =>
                    new Response(JSON.stringify('Home Lab'), { headers: { 'content-type': 'application/json' } }),
            ),
        );

        const { unmount } = renderHook(() => useDocumentTitle('Dashboard'), { wrapper: InstanceNameWrapper });

        await waitFor(() => expect(document.title).toBe('Dashboard - Home Lab'));

        unmount();
        expect(document.title).toBe('Home Lab');
    });

    it('uses the instance name as the whole title when no title is provided', async () => {
        vi.stubGlobal(
            'fetch',
            vi.fn(
                async () =>
                    new Response(JSON.stringify('Home Lab'), { headers: { 'content-type': 'application/json' } }),
            ),
        );

        renderHook(() => useDocumentTitle(), { wrapper: InstanceNameWrapper });

        await waitFor(() => expect(document.title).toBe('Home Lab'));
    });
});
