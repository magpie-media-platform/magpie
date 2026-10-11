import { renderHook, waitFor } from '@testing-library/react';
import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import type { ReactNode } from 'react';

import { InstanceNameProvider, useInstanceName } from './useInstanceName';

let queryClient: QueryClient;

function Wrapper({ children }: { children: ReactNode }) {
    return (
        <QueryClientProvider client={queryClient}>
            <InstanceNameProvider>{children}</InstanceNameProvider>
        </QueryClientProvider>
    );
}

describe('useInstanceName', () => {
    beforeEach(() => {
        queryClient = new QueryClient({ defaultOptions: { queries: { retry: false, gcTime: 0 } } });
        document.head.innerHTML = '<meta name="apple-mobile-web-app-title" content="Magpie" />';
        localStorage.clear();
    });

    afterEach(() => {
        vi.unstubAllGlobals();
    });

    it('useInstanceName_ReturnsMagpie_WhenNoProviderWrapsTheHook', () => {
        // Arrange

        // Act
        const { result } = renderHook(() => useInstanceName());

        // Assert
        expect(result.current).toBe('Magpie');
    });

    it('useInstanceName_ReturnsTheName_WhenTheInstanceNameIsSet', async () => {
        // Arrange
        const fetchMock = vi.fn(
            async () =>
                new Response(JSON.stringify('Home Lab'), {
                    status: 200,
                    headers: { 'content-type': 'application/json' },
                }),
        );
        vi.stubGlobal('fetch', fetchMock);

        // Act
        const { result } = renderHook(() => useInstanceName(), { wrapper: Wrapper });

        // Assert
        await waitFor(() => expect(result.current).toBe('Home Lab'));
        expect(fetchMock).toHaveBeenCalledWith('/api/system/instance-name', expect.anything());
    });

    it('useInstanceName_ReturnsTheStoredName_WhenTheInstanceNameEndpointHasNotAnswered', async () => {
        // Arrange
        localStorage.setItem('magpie.instanceName', 'Home Lab');
        const fetchMock = vi.fn(
            async () =>
                new Response(JSON.stringify('Lab Two'), {
                    status: 200,
                    headers: { 'content-type': 'application/json' },
                }),
        );
        vi.stubGlobal('fetch', fetchMock);

        // Act
        const { result } = renderHook(() => useInstanceName(), { wrapper: Wrapper });

        // Assert
        expect(result.current).toBe('Home Lab');
        await waitFor(() => expect(result.current).toBe('Lab Two'));
    });

    it.each([
        ['empty', '""', 200],
        ['blank', '"   "', 200],
        ['missing', null, 204],
    ])('useInstanceName_ReturnsMagpie_WhenTheInstanceNameIs %s', async (_case, body, status) => {
        // Arrange
        const fetchMock = vi.fn(
            async () => new Response(body, { status, headers: { 'content-type': 'application/json' } }),
        );
        vi.stubGlobal('fetch', fetchMock);

        // Act
        const { result } = renderHook(() => useInstanceName(), { wrapper: Wrapper });

        // Assert
        await waitFor(() => expect(fetchMock).toHaveBeenCalled());
        await waitFor(() => expect(localStorage.getItem('magpie.instanceName')).toBe('Magpie'));
        expect(result.current).toBe('Magpie');
    });

    it('useInstanceName_ReturnsTheStoredName_WhenTheInstanceNameRequestFails', async () => {
        // Arrange
        localStorage.setItem('magpie.instanceName', 'Home Lab');
        const fetchMock = vi.fn(
            async () =>
                new Response(JSON.stringify('Lab Two'), {
                    status: 500,
                    headers: { 'content-type': 'application/json' },
                }),
        );
        vi.stubGlobal('fetch', fetchMock);

        // Act
        const { result } = renderHook(() => useInstanceName(), { wrapper: Wrapper });

        // Assert
        await waitFor(() => expect(fetchMock).toHaveBeenCalled());
        await waitFor(() => expect(queryClient.isFetching()).toBe(0));
        expect(result.current).toBe('Home Lab');
        expect(localStorage.getItem('magpie.instanceName')).toBe('Home Lab');
    });

    it('useInstanceName_ReturnsMagpie_WhenTheInstanceNameRequestFailsAndNothingIsStored', async () => {
        // Arrange
        const fetchMock = vi.fn(
            async () =>
                new Response(JSON.stringify('Home Lab'), {
                    status: 500,
                    headers: { 'content-type': 'application/json' },
                }),
        );
        vi.stubGlobal('fetch', fetchMock);

        // Act
        const { result } = renderHook(() => useInstanceName(), { wrapper: Wrapper });

        // Assert
        await waitFor(() => expect(fetchMock).toHaveBeenCalled());
        await waitFor(() => expect(queryClient.isFetching()).toBe(0));
        expect(result.current).toBe('Magpie');
        expect(localStorage.getItem('magpie.instanceName')).toBeNull();
    });

    it('useInstanceName_StoresTheNameForTheOfflinePageAndTheHomeScreenTitle_WhenTheInstanceNameIsSet', async () => {
        // Arrange
        vi.stubGlobal(
            'fetch',
            vi.fn(
                async () =>
                    new Response(JSON.stringify('Home Lab'), {
                        status: 200,
                        headers: { 'content-type': 'application/json' },
                    }),
            ),
        );

        // Act
        renderHook(() => useInstanceName(), { wrapper: Wrapper });

        // Assert
        await waitFor(() => expect(localStorage.getItem('magpie.instanceName')).toBe('Home Lab'));
        expect(document.querySelector('meta[name="apple-mobile-web-app-title"]')).toHaveAttribute(
            'content',
            'Home Lab',
        );
    });
});
