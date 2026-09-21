import { act, render, screen, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { apiGet } from '@/lib/api';
import type { EventStreamPayload } from '@/hooks/useEventStream';

import { ContentLineageProvider, ContentLineageTitle } from './ContentLineageTitle';

const stream = vi.hoisted(() => ({ handle: (_event: EventStreamPayload) => {} }));
vi.mock('@/hooks/useEventStream', () => ({
    useEventStream: (handle: (event: EventStreamPayload) => void) => {
        stream.handle = handle;
    },
}));

vi.mock('@/lib/api', async (importOriginal) => ({
    ...(await importOriginal<typeof import('@/lib/api')>()),
    apiGet: vi.fn(),
}));
const load = vi.mocked(apiGet);
let client: QueryClient;
beforeEach(() => {
    load.mockReset();
    client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
});

describe('ContentLineageTitle', () => {
    it('deduplicates ancestor requests and refreshes only the changed ancestor', async () => {
        // Arrange
        load.mockResolvedValue({ id: 'parent', name: 'Parent' });
        render(
            <QueryClientProvider client={client}>
                <ContentLineageProvider contentTypes={[]}>
                    <ContentLineageTitle qualifiedName={{ id: 'one', parentId: 'parent', title: 'One' }} />
                    <ContentLineageTitle qualifiedName={{ id: 'two', parentId: 'parent', title: 'Two' }} />
                </ContentLineageProvider>
            </QueryClientProvider>,
        );
        expect(await screen.findByTitle('Parent > One')).toBeInTheDocument();
        expect(await screen.findByTitle('Parent > Two')).toBeInTheDocument();
        expect(load).toHaveBeenCalledTimes(1);

        // Act
        act(() =>
            stream.handle({
                name: 'content.hierarchyItem.updated',
                context: { 'content.hierarchyItemId': 'other' },
            } as EventStreamPayload),
        );
        expect(load).toHaveBeenCalledTimes(1);
        load.mockResolvedValue({ id: 'parent', name: 'Renamed' });
        act(() =>
            stream.handle({
                name: 'content.hierarchyItem.updated',
                context: { 'content.hierarchyItemId': 'parent' },
            } as EventStreamPayload),
        );

        // Assert
        expect(await screen.findByTitle('Renamed > One')).toBeInTheDocument();
        expect(await screen.findByTitle('Renamed > Two')).toBeInTheDocument();
        expect(load).toHaveBeenCalledTimes(2);
    });

    it('retries a failed ancestor after reconnect and stops a cycle', async () => {
        // Arrange
        load.mockRejectedValueOnce(new Error('Unavailable')).mockResolvedValue({
            id: 'parent',
            parentId: 'leaf',
            name: 'Parent',
        });
        render(
            <QueryClientProvider client={client}>
                <ContentLineageProvider contentTypes={[]}>
                    <ContentLineageTitle qualifiedName={{ id: 'leaf', parentId: 'parent', title: 'Leaf' }} />
                </ContentLineageProvider>
            </QueryClientProvider>,
        );
        await waitFor(() => expect(screen.getByTitle('Leaf')).not.toHaveAttribute('aria-busy'));

        // Act
        act(() => stream.handle({ name: 'stream.reconnected', context: {} } as EventStreamPayload));

        // Assert
        expect(await screen.findByTitle('Parent > Leaf')).toBeInTheDocument();
        expect(load).toHaveBeenCalledTimes(2);
    });
});
