import { type ReactNode } from 'react';

import { act, renderHook } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { apiGet, apiPost } from '@/lib/api';
import { contentQueryKeys } from './useContent';
import { useAttachContentFile, useContentFileBrowserLocation, useUnlinkContentFile } from './useContentFileMutations';

vi.mock('@/lib/api', () => ({
    apiGet: vi.fn(),
    apiPost: vi.fn(),
}));

describe('content file hooks', () => {
    beforeEach(() => {
        vi.clearAllMocks();
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('loads the configured browser location on demand', async () => {
        const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
        vi.mocked(apiGet).mockResolvedValue({
            rootPath: '/media/library',
            initialPath: '/media/library/The Matrix',
        });
        const wrapper = ({ children }: { children: ReactNode }) => (
            <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
        );

        const { result } = renderHook(() => useContentFileBrowserLocation('content-1'), { wrapper });

        expect(apiGet).not.toHaveBeenCalled();
        const response = await act(() => result.current.refetch());
        expect(response.data).toEqual({
            rootPath: '/media/library',
            initialPath: '/media/library/The Matrix',
        });
        expect(apiGet).toHaveBeenCalledWith('/content/content-1/files/browser-location');
        queryClient.clear();
    });

    it('does not request a browser location without a content id', async () => {
        const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
        const wrapper = ({ children }: { children: ReactNode }) => (
            <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
        );
        const { result } = renderHook(() => useContentFileBrowserLocation(undefined), { wrapper });

        const response = await act(() => result.current.refetch());

        expect(response.error).toEqual(new Error('A content id is required.'));
        expect(apiGet).not.toHaveBeenCalled();
        queryClient.clear();
    });

    it('attaches a host file and refreshes content state', async () => {
        const queryClient = new QueryClient({ defaultOptions: { mutations: { retry: false } } });
        const invalidateQueries = vi.spyOn(queryClient, 'invalidateQueries');
        vi.mocked(apiPost).mockResolvedValue({
            mediaFile: { id: 'file-1', filePath: '/media/library/The Matrix/the-matrix.mkv' },
            isPrimary: true,
        });
        const wrapper = ({ children }: { children: ReactNode }) => (
            <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
        );
        const { result } = renderHook(() => useAttachContentFile(), { wrapper });

        await act(async () => {
            await result.current.mutateAsync({
                contentHierarchyItemId: 'content-1',
                filePath: '/media/library/The Matrix/the-matrix.mkv',
            });
        });

        expect(apiPost).toHaveBeenCalledWith('/content/content-1/files', {
            filePath: '/media/library/The Matrix/the-matrix.mkv',
        });
        expect(invalidateQueries).toHaveBeenCalledWith({ queryKey: contentQueryKeys.detail('content-1') });
        expect(invalidateQueries).toHaveBeenCalledWith({ queryKey: contentQueryKeys.all });
        queryClient.clear();
    });

    it('unlinks an association and refreshes content state', async () => {
        const queryClient = new QueryClient({ defaultOptions: { mutations: { retry: false } } });
        const invalidateQueries = vi.spyOn(queryClient, 'invalidateQueries');
        vi.mocked(apiPost).mockResolvedValue(undefined);
        const wrapper = ({ children }: { children: ReactNode }) => (
            <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
        );
        const { result } = renderHook(() => useUnlinkContentFile(), { wrapper });

        await act(async () => {
            await result.current.mutateAsync({ contentHierarchyItemId: 'content-1', mediaFileId: 'file-1' });
        });

        expect(apiPost).toHaveBeenCalledWith('/content/content-1/files/file-1/unlink');
        expect(invalidateQueries).toHaveBeenCalledWith({ queryKey: contentQueryKeys.detail('content-1') });
        expect(invalidateQueries).toHaveBeenCalledWith({ queryKey: contentQueryKeys.all });
        queryClient.clear();
    });
});
