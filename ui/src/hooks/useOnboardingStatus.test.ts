import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { act, renderHook, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { createElement } from 'react';

vi.mock('./useEventStream', () => ({
    useEventStream: vi.fn(),
    useGlobalEventStreamConnection: vi.fn(),
}));

vi.mock('@/lib/api', () => ({
    apiGet: vi.fn(),
}));

import { apiGet } from '@/lib/api';
import {
    deriveOnboardingSteps,
    isOnboardingComplete,
    useOnboardingStatus,
    type OnboardingStatus,
} from './useOnboardingStatus';
import { useEventStream } from './useEventStream';

const mockedUseEventStream = vi.mocked(useEventStream);
const mockApiGet = vi.mocked(apiGet);

const allDone: OnboardingStatus = {
    hasIndexer: true,
    hasDownloadClient: true,
    hasRootFolder: true,
    hasMonitoredContent: true,
    isComplete: true,
};

describe('deriveOnboardingSteps', () => {
    it('returns the four steps in pipeline order', () => {
        const steps = deriveOnboardingSteps(undefined);
        expect(steps.map((s) => s.key)).toEqual(['indexer', 'downloadClient', 'rootFolder', 'monitoredContent']);
    });

    it('treats every step as not done when status is undefined', () => {
        const steps = deriveOnboardingSteps(undefined);
        expect(steps.every((s) => !s.done)).toBe(true);
    });

    it('maps each flag to the matching step done-state', () => {
        const steps = deriveOnboardingSteps({
            hasIndexer: true,
            hasDownloadClient: false,
            hasRootFolder: true,
            hasMonitoredContent: false,
            isComplete: false,
        });
        const byKey = Object.fromEntries(steps.map((s) => [s.key, s.done]));
        expect(byKey).toEqual({
            indexer: true,
            downloadClient: false,
            rootFolder: true,
            monitoredContent: false,
        });
    });

    it('points each step at its setup route', () => {
        const steps = deriveOnboardingSteps(allDone);
        const byKey = Object.fromEntries(steps.map((s) => [s.key, s.to]));
        expect(byKey).toEqual({
            indexer: '/admin/indexers',
            downloadClient: '/admin/download-clients',
            rootFolder: '/admin/root-folders',
            monitoredContent: '/library/add',
        });
    });

    it('gives every step a one-line reason', () => {
        const steps = deriveOnboardingSteps(undefined);
        expect(steps.every((s) => s.reason.length > 0)).toBe(true);
    });
});

describe('isOnboardingComplete', () => {
    it('is false for undefined status', () => {
        expect(isOnboardingComplete(undefined)).toBe(false);
    });

    it('honours the server-computed isComplete flag', () => {
        expect(isOnboardingComplete(allDone)).toBe(true);
    });

    it('is false when any step is missing', () => {
        expect(
            isOnboardingComplete({
                hasIndexer: true,
                hasDownloadClient: true,
                hasRootFolder: true,
                hasMonitoredContent: false,
                isComplete: false,
            }),
        ).toBe(false);
    });

    it('falls back to AND-ing the flags when isComplete is absent', () => {
        const status = {
            hasIndexer: true,
            hasDownloadClient: true,
            hasRootFolder: true,
            hasMonitoredContent: true,
        } as OnboardingStatus;
        expect(isOnboardingComplete(status)).toBe(true);
    });
});

describe('useOnboardingStatus', () => {
    beforeEach(() => {
        vi.clearAllMocks();
    });

    afterEach(() => {
        vi.useRealTimers();
    });

    it('coalesces a burst of content events into a single refetch', async () => {
        mockApiGet.mockResolvedValue(allDone);
        const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
        const wrapper = ({ children }: { children: React.ReactNode }) =>
            createElement(QueryClientProvider, { client: queryClient }, children);
        const { result } = renderHook(() => useOnboardingStatus(), { wrapper });
        await waitFor(() => expect(result.current.isLoading).toBe(false));

        vi.useFakeTimers();
        mockApiGet.mockClear();
        const handler = mockedUseEventStream.mock.calls[0]?.[0];
        for (let index = 0; index < 100; index += 1) {
            handler?.({
                name: 'content.hierarchyItem.created',
                context: { 'content.hierarchyItemId': `content-${index}` },
            });
        }
        await act(async () => {
            await vi.advanceTimersByTimeAsync(300);
        });

        expect(mockApiGet).toHaveBeenCalledTimes(1);
    });
});
