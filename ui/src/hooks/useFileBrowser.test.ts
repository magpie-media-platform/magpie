import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { renderHook, waitFor, act } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { createElement } from 'react';

vi.mock('@/lib/api', () => ({
    apiPost: vi.fn(),
}));

import { apiPost } from '@/lib/api';
import { useFileBrowser, type FileSystemEntry } from './useFileBrowser';

const mockEntries: FileSystemEntry[] = [
    { name: 'Documents', path: '/home/user/Documents', type: 'folder' },
    { name: 'Downloads', path: '/home/user/Downloads', type: 'folder' },
    { name: 'notes.txt', path: '/home/user/notes.txt', type: 'file', size: 1024 },
];

function createWrapper() {
    const queryClient = new QueryClient({
        defaultOptions: {
            queries: {
                retry: false,
                gcTime: 0,
            },
            mutations: {
                retry: false,
            },
        },
    });

    return ({ children }: { children: React.ReactNode }) =>
        createElement(QueryClientProvider, { client: queryClient }, children);
}

describe('useFileBrowser', () => {
    const mockApiPost = vi.mocked(apiPost);

    beforeEach(() => {
        vi.clearAllMocks();
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('should start with empty entries', () => {
        const { result } = renderHook(() => useFileBrowser(), {
            wrapper: createWrapper(),
        });

        expect(result.current.entries).toEqual([]);
        expect(result.current.isLoading).toBe(false);
        expect(result.current.error).toBeNull();
    });

    it('should browse a directory', async () => {
        mockApiPost.mockResolvedValue(mockEntries);

        const { result } = renderHook(() => useFileBrowser(), {
            wrapper: createWrapper(),
        });

        act(() => {
            result.current.browse('/home/user');
        });

        await waitFor(() => {
            expect(result.current.isLoading).toBe(false);
        });

        expect(mockApiPost).toHaveBeenCalledWith('/file-browser/contents', { path: '/home/user' });
        expect(result.current.entries).toEqual(mockEntries);
    });

    it('should set isLoading while browsing', async () => {
        let resolvePromise: (value: FileSystemEntry[]) => void;
        const pendingPromise = new Promise<FileSystemEntry[]>((resolve) => {
            resolvePromise = resolve;
        });
        mockApiPost.mockReturnValue(pendingPromise);

        const { result } = renderHook(() => useFileBrowser(), {
            wrapper: createWrapper(),
        });

        act(() => {
            result.current.browse('/home/user');
        });

        await waitFor(() => {
            expect(result.current.isLoading).toBe(true);
        });

        act(() => {
            resolvePromise!(mockEntries);
        });

        await waitFor(() => {
            expect(result.current.isLoading).toBe(false);
        });
    });

    it('should set error on failure', async () => {
        const error = new Error('Access denied');
        mockApiPost.mockRejectedValue(error);

        const { result } = renderHook(() => useFileBrowser(), {
            wrapper: createWrapper(),
        });

        act(() => {
            result.current.browse('/root');
        });

        await waitFor(() => {
            expect(result.current.isLoading).toBe(false);
        });

        expect(result.current.error).toBeInstanceOf(Error);
        expect(result.current.error?.message).toBe('Access denied');
    });

    it('should use browseAsync for promise-based browsing', async () => {
        mockApiPost.mockResolvedValue(mockEntries);

        const { result } = renderHook(() => useFileBrowser(), {
            wrapper: createWrapper(),
        });

        let entries: FileSystemEntry[] | undefined;
        await act(async () => {
            entries = await result.current.browseAsync('/home/user');
        });

        expect(entries).toEqual(mockEntries);
    });

    it('should reset state', async () => {
        mockApiPost.mockResolvedValue(mockEntries);

        const { result } = renderHook(() => useFileBrowser(), {
            wrapper: createWrapper(),
        });

        await act(async () => {
            await result.current.browseAsync('/home/user');
        });

        await waitFor(() => {
            expect(result.current.entries).toEqual(mockEntries);
        });

        act(() => {
            result.current.reset();
        });

        await waitFor(() => {
            expect(result.current.entries).toEqual([]);
        });
    });

    it('should handle empty directory', async () => {
        mockApiPost.mockResolvedValue([]);

        const { result } = renderHook(() => useFileBrowser(), {
            wrapper: createWrapper(),
        });

        await act(async () => {
            await result.current.browseAsync('/empty');
        });

        expect(result.current.entries).toEqual([]);
        expect(result.current.error).toBeNull();
    });

    it('should show entries for the most recently navigated path even when an earlier slow request resolves later', async () => {
        // Simulate a race: first navigate to /slow, which resolves slowly,
        // then navigate to /fast, which resolves quickly. The hook must
        // ultimately reflect /fast's entries, not /slow's.
        const slowEntries: FileSystemEntry[] = [{ name: 'slow', path: '/slow/slow', type: 'folder' }];
        const fastEntries: FileSystemEntry[] = [{ name: 'fast', path: '/fast/fast', type: 'folder' }];

        let resolveSlow: ((value: FileSystemEntry[]) => void) | undefined;
        const slowPromise = new Promise<FileSystemEntry[]>((resolve) => {
            resolveSlow = resolve;
        });

        mockApiPost.mockImplementation((_url: string, body?: unknown) => {
            const path = (body as { path?: string } | undefined)?.path;
            if (path === '/slow') {
                return slowPromise;
            }
            if (path === '/fast') {
                return Promise.resolve(fastEntries);
            }
            return Promise.resolve([]);
        });

        const { result } = renderHook(() => useFileBrowser(), {
            wrapper: createWrapper(),
        });

        act(() => {
            result.current.browse('/slow');
        });

        // Switch to /fast before /slow has resolved.
        act(() => {
            result.current.browse('/fast');
        });

        await waitFor(() => {
            expect(result.current.entries).toEqual(fastEntries);
        });

        // Now resolve the slower request -- the hook must NOT switch back to /slow.
        act(() => {
            resolveSlow!(slowEntries);
        });

        // Give react-query a tick to settle, then reassert.
        await waitFor(() => {
            expect(result.current.entries).toEqual(fastEntries);
        });

        expect(result.current.entries).not.toEqual(slowEntries);
    });
});
