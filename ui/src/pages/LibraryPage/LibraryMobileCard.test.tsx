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

    it('LibraryMobileCard_RendersReadableDescription_WhenStoredOverviewHasMarkdown', () => {
        // Arrange
        const item: ContentListItem = {
            id: 'book',
            title: 'The Left Hand of Darkness',
            level: 'ebook',
            levelLabel: 'E-book',
            contentType: 'book',
            monitoring: 'Monitored',
            effectiveMonitoring: 'Monitored',
            fulfillment: 'missing',
            overview:
                "[Comment by Kim Stanley Robinson, on The Guardian's website][1]: The Left Hand of Darkness by Ursula K Le Guin (1969) > One of my favorite novels is The Left Hand of Darkness. [1]: https://www.theguardian.com/books/2011/may/14/science-fiction-authors-choice",
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
        expect(
            screen.getByText(
                "Comment by Kim Stanley Robinson, on The Guardian's website: The Left Hand of Darkness by Ursula K Le Guin (1969) One of my favorite novels is The Left Hand of Darkness.",
            ),
        ).toBeInTheDocument();
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

    it('shows unmonitored, external fulfillment, and the fulfillment date independently', () => {
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
        expect(screen.getByLabelText('Unmonitored')).toBeInTheDocument();
        expect(screen.getByText('External')).toBeInTheDocument();
        expect(screen.getByText(/Fulfilled:/)).not.toHaveTextContent('—');
    });
});
