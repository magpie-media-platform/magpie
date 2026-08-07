import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { renderHook, waitFor, act } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { createElement } from 'react';

vi.mock('@/lib/api', async (importOriginal) => {
    const actual = await importOriginal<typeof import('@/lib/api')>();
    return {
        ...actual,
        apiGet: vi.fn(),
    };
});

import { apiGet } from '@/lib/api';
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
    const mockApiGet = vi.mocked(apiGet);

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
        mockApiGet.mockResolvedValue(mockEntries);

        const { result } = renderHook(() => useFileBrowser(), {
            wrapper: createWrapper(),
        });

        act(() => {
            result.current.navigate('/home/user');
        });

        await waitFor(() => {
            expect(result.current.isLoading).toBe(false);
        });

        expect(mockApiGet).toHaveBeenCalledWith('/system/files?path=%2Fhome%2Fuser');
        expect(result.current.entries).toEqual(mockEntries);
    });

    it('should set isLoading while browsing', async () => {
        let resolvePromise: (value: FileSystemEntry[]) => void;
        const pendingPromise = new Promise<FileSystemEntry[]>((resolve) => {
            resolvePromise = resolve;
        });
        mockApiGet.mockReturnValue(pendingPromise);

        const { result } = renderHook(() => useFileBrowser(), {
            wrapper: createWrapper(),
        });

        act(() => {
            result.current.navigate('/home/user');
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
        mockApiGet.mockRejectedValue(error);

        const { result } = renderHook(() => useFileBrowser(), {
            wrapper: createWrapper(),
        });

        act(() => {
            result.current.navigate('/root');
        });

        await waitFor(() => {
            expect(result.current.isLoading).toBe(false);
        });

        expect(result.current.error).toBeInstanceOf(Error);
        expect(result.current.error?.message).toBe('Access denied');
    });

    it('should use navigateAsync for promise-based browsing', async () => {
        mockApiGet.mockResolvedValue(mockEntries);

        const { result } = renderHook(() => useFileBrowser(), {
            wrapper: createWrapper(),
        });

        let entries: FileSystemEntry[] | undefined;
        await act(async () => {
            entries = await result.current.navigateAsync('/home/user');
        });

        expect(entries).toEqual(mockEntries);
    });

    it('should reset state', async () => {
        mockApiGet.mockResolvedValue(mockEntries);

        const { result } = renderHook(() => useFileBrowser(), {
            wrapper: createWrapper(),
        });

        await act(async () => {
            await result.current.navigateAsync('/home/user');
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
        mockApiGet.mockResolvedValue([]);

        const { result } = renderHook(() => useFileBrowser(), {
            wrapper: createWrapper(),
        });

        await act(async () => {
            await result.current.navigateAsync('/empty');
        });

        expect(result.current.entries).toEqual([]);
        expect(result.current.error).toBeNull();
    });

    it('keeps latest navigation results when an earlier request resolves later', async () => {
        const slowEntries: FileSystemEntry[] = [{ name: 'slow', path: '/slow/slow', type: 'folder' }];
        const fastEntries: FileSystemEntry[] = [{ name: 'fast', path: '/fast/fast', type: 'folder' }];

        let resolveSlow: ((value: FileSystemEntry[]) => void) | undefined;
        const slowPromise = new Promise<FileSystemEntry[]>((resolve) => {
            resolveSlow = resolve;
        });

        mockApiGet.mockImplementation((url: string) => {
            const path = new URLSearchParams(url.split('?')[1]).get('path');
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
            result.current.navigate('/slow');
        });

        act(() => {
            result.current.navigate('/fast');
        });

        await waitFor(() => {
            expect(result.current.entries).toEqual(fastEntries);
        });

        act(() => {
            resolveSlow!(slowEntries);
        });

        await waitFor(() => {
            expect(result.current.entries).toEqual(fastEntries);
        });

        expect(result.current.entries).not.toEqual(slowEntries);
    });
});
