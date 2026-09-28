import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it } from 'vitest';

import { ContentLineageProvider } from '@/components/content/ContentLineageTitle';
import type { ContentListItem } from '@/types';

import { LibraryMobileCard } from './LibraryMobileCard';
import { TooltipProvider } from '@/components/ui/Tooltip';

describe('LibraryMobileCard', () => {
    it('resolves a nested title without changing the leaf link', async () => {
        const item: ContentListItem = {
            id: 'pilot',
            parentId: 'season',
            title: 'Pilot',
            level: 'episode',
            levelLabel: 'Episode',
            contentType: 'tv',
            monitoring: 'Monitored',
            effectiveMonitoring: 'Monitored',
            fulfillment: 'fulfilled',
        };

        const queryClient = new QueryClient();
        queryClient.setQueryData(['content-lineage', 'season'], { id: 'season', title: 'Season 1' });

        render(
            <MemoryRouter>
                <QueryClientProvider client={queryClient}>
                    <ContentLineageProvider contentTypes={[]}>
                        <LibraryMobileCard item={item} />
                    </ContentLineageProvider>
                </QueryClientProvider>
            </MemoryRouter>,
            { wrapper: TooltipProvider },
        );

        expect(await screen.findByTitle('Season 1 > Pilot')).toBeInTheDocument();
        expect(screen.getByText('Season 1')).toBeInTheDocument();
        expect(screen.getByText('Pilot')).toBeInTheDocument();
        expect(screen.getByRole('link')).toHaveAttribute('href', '/library/pilot');
    });

    it('uses a singular primary-media file label', () => {
        const item: ContentListItem = {
            id: 'history',
            title: 'Unknown History',
            level: 'movie',
            levelLabel: 'Movie',
            contentType: 'movie',
            monitoring: 'Monitored',
            effectiveMonitoring: 'Monitored',
            fulfillment: 'fulfilled',
            primaryMediaFileCount: 1,
            primaryMediaSize: 1024,
        };

        render(
            <MemoryRouter>
                <QueryClientProvider client={new QueryClient()}>
                    <ContentLineageProvider contentTypes={[]}>
                        <LibraryMobileCard item={item} />
                    </ContentLineageProvider>
                </QueryClientProvider>
            </MemoryRouter>,
            { wrapper: TooltipProvider },
        );

        expect(screen.getByText('Media: 1 file · 1 KB')).toBeInTheDocument();
    });

    it('shows inherited unmonitored, external fulfillment, and the fulfillment date independently', () => {
        // Arrange
        const item: ContentListItem = {
            id: 'external',
            title: 'External Movie',
            level: 'movie',
            levelLabel: 'Movie',
            contentType: 'movie',
            monitoring: null,
            effectiveMonitoring: 'Unmonitored',
            fulfillment: 'fulfilledExternally',
            fulfilledAt: '2026-09-20T00:00:00Z',
        };

        // Act
        render(
            <MemoryRouter>
                <QueryClientProvider client={new QueryClient()}>
                    <ContentLineageProvider contentTypes={[]}>
                        <LibraryMobileCard item={item} />
                    </ContentLineageProvider>
                </QueryClientProvider>
            </MemoryRouter>,
            { wrapper: TooltipProvider },
        );

        // Assert
        expect(screen.getByLabelText('Unmonitored (inherited)')).toBeInTheDocument();
        expect(screen.getByText('External')).toBeInTheDocument();
        expect(screen.getByText(/Fulfilled:/)).not.toHaveTextContent('—');
    });
});
