import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { renderHook, waitFor } from '@testing-library/react';
import { createElement, type ReactNode } from 'react';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { ExtensionContext, type ExtensionContextValue } from '@/extensions/ExtensionProvider';
import type { LoaderState } from '@/extensions/types';

vi.mock('@/lib/api', () => ({
    apiGet: vi.fn(),
}));

import { apiGet } from '@/lib/api';
import type { SettingSchemaOptionsSource } from './useProviderSchema';
import { useOptionsSource, type OptionsSourceContext } from './useOptionsSource';

function createWrapper(loaderState: LoaderState = 'ready') {
    const client = new QueryClient({
        defaultOptions: {
            queries: { retry: false, gcTime: 0, staleTime: 0 },
        },
    });
    const extensionContext: ExtensionContextValue = {
        state: { state: loaderState, error: null, manifest: { assets: [], extensions: [] } },
        context: {
            apiBaseUrl: '/api',
            userId: 'user-1',
            username: 'admin',
            role: 'Admin',
            theme: null,
        },
        getExtensionsForSlot: () => [],
        retry: () => Promise.resolve(),
    };

    return ({ children }: { children: ReactNode }) =>
        createElement(
            ExtensionContext.Provider,
            { value: extensionContext },
            createElement(QueryClientProvider, { client }, children),
        );
}

const jsFunctionSource: SettingSchemaOptionsSource = {
    kind: 'JSFunction',
    functionName: 'testPlugin.listOptions',
};

const defaultContext: OptionsSourceContext = {
    extensionId: 'extension-1',
    entityId: 'entity-1',
    state: { host: 'http://example.test', apiKey: 'secret' },
};

function setWindowFunction(value: unknown): void {
    (window as unknown as Record<string, unknown>).testPlugin = value;
}

describe('useOptionsSource', () => {
    const mockApiGet = vi.mocked(apiGet);

    beforeEach(() => {
        mockApiGet.mockReset();
        delete (window as unknown as Record<string, unknown>).testPlugin;
    });

    it('projects and sorts root folders from the root-folders endpoint', async () => {
        mockApiGet.mockResolvedValueOnce([
            { name: 'Shows', path: '/data/shows' },
            { name: 'Movies', path: '/data/movies' },
        ]);
        const source: SettingSchemaOptionsSource = { kind: 'RootFolders' };

        const { result } = renderHook(() => useOptionsSource(source, defaultContext), {
            wrapper: createWrapper(),
        });

        await waitFor(() => expect(result.current.isSuccess).toBe(true));

        expect(mockApiGet).toHaveBeenCalledWith('/root-folders');
        expect(result.current.data).toEqual([
            { value: '/data/movies', label: 'Movies', description: '/data/movies' },
            { value: '/data/shows', label: 'Shows', description: '/data/shows' },
        ]);
    });

    it('projects and sorts content types from the content-types endpoint', async () => {
        mockApiGet.mockResolvedValueOnce([
            { slug: 'shows', displayName: 'Shows' },
            { slug: 'movies', displayName: 'Movies' },
        ]);
        const source: SettingSchemaOptionsSource = { kind: 'ContentTypes' };

        const { result } = renderHook(() => useOptionsSource(source, defaultContext), {
            wrapper: createWrapper(),
        });

        await waitFor(() => expect(result.current.isSuccess).toBe(true));

        expect(mockApiGet).toHaveBeenCalledWith('/content-types');
        expect(result.current.data).toEqual([
            { value: 'movies', label: 'Movies', description: 'movies' },
            { value: 'shows', label: 'Shows', description: 'shows' },
        ]);
    });

    it('resolves a window-scoped dot path and passes the complete context', async () => {
        const listOptions = vi.fn(() => [
            { value: 'lib-1', label: 'Movies', description: 'Movie library' },
            { value: 2, label: 'Invalid' },
            null,
        ]);
        setWindowFunction({ listOptions });

        const { result } = renderHook(() => useOptionsSource(jsFunctionSource, defaultContext), {
            wrapper: createWrapper(),
        });

        await waitFor(() => expect(result.current.isSuccess).toBe(true));

        expect(listOptions).toHaveBeenCalledWith(defaultContext);
        expect(result.current.data).toEqual([{ value: 'lib-1', label: 'Movies', description: 'Movie library' }]);
    });

    it('supports a promise result from a JSFunction source', async () => {
        const listOptions = vi.fn(async () => [{ value: 'lib-2', label: 'Shows' }]);
        setWindowFunction({ listOptions });

        const { result } = renderHook(() => useOptionsSource(jsFunctionSource, defaultContext), {
            wrapper: createWrapper(),
        });

        await waitFor(() => expect(result.current.isSuccess).toBe(true));

        expect(result.current.data).toEqual([{ value: 'lib-2', label: 'Shows', description: undefined }]);
    });

    it('debounces full form-state changes before calling the JSFunction again', async () => {
        const listOptions = vi.fn(() => [{ value: 'lib-1', label: 'Movies' }]);
        setWindowFunction({ listOptions });

        const { result, rerender } = renderHook(
            ({ context }: { context: OptionsSourceContext }) => useOptionsSource(jsFunctionSource, context),
            { wrapper: createWrapper(), initialProps: { context: defaultContext } },
        );
        await waitFor(() => expect(result.current.isSuccess).toBe(true));
        expect(listOptions).toHaveBeenCalledTimes(1);

        const changedContext = {
            ...defaultContext,
            state: { ...defaultContext.state, host: 'http://changed.test', unrelated: 'also included' },
        };
        rerender({ context: changedContext });

        await new Promise((resolve) => window.setTimeout(resolve, 100));
        expect(listOptions).toHaveBeenCalledTimes(1);

        await waitFor(() => expect(listOptions).toHaveBeenCalledTimes(2), { timeout: 1000 });
        expect(listOptions).toHaveBeenLastCalledWith(changedContext);
    });

    it('does not call the JSFunction again when state key order changes', async () => {
        const listOptions = vi.fn(() => []);
        setWindowFunction({ listOptions });

        const first = { ...defaultContext, state: { host: 'http://example.test', apiKey: 'secret' } };
        const second = { ...defaultContext, state: { apiKey: 'secret', host: 'http://example.test' } };
        const { result, rerender } = renderHook(
            ({ context }: { context: OptionsSourceContext }) => useOptionsSource(jsFunctionSource, context),
            { wrapper: createWrapper(), initialProps: { context: first } },
        );
        await waitFor(() => expect(result.current.isSuccess).toBe(true));

        rerender({ context: second });
        await new Promise((resolve) => window.setTimeout(resolve, 300));

        expect(listOptions).toHaveBeenCalledTimes(1);
    });

    it('waits for extension assets before resolving a JSFunction source', async () => {
        const listOptions = vi.fn(() => []);
        setWindowFunction({ listOptions });

        const { result } = renderHook(() => useOptionsSource(jsFunctionSource, defaultContext), {
            wrapper: createWrapper('loading'),
        });
        await new Promise((resolve) => window.setTimeout(resolve, 20));

        expect(listOptions).not.toHaveBeenCalled();
        expect(result.current.fetchStatus).toBe('idle');
    });

    it('reports an error when the declared window function is unavailable', async () => {
        const { result } = renderHook(() => useOptionsSource(jsFunctionSource, defaultContext), {
            wrapper: createWrapper(),
        });

        await waitFor(() => expect(result.current.isError).toBe(true));
        expect(result.current.error).toEqual(
            new Error('Options source function "testPlugin.listOptions" was not found.'),
        );
    });
});
