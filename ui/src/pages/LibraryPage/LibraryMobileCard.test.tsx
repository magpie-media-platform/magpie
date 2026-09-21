import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it } from 'vitest';

import { ContentLineageProvider } from '@/components/content/ContentLineageTitle';
import type { ContentListItem } from '@/types';

import { LibraryMobileCard } from './LibraryMobileCard';

describe('LibraryMobileCard', () => {
    it('resolves a nested title without changing the leaf link', async () => {
        const item: ContentListItem = {
            id: 'pilot',
            parentId: 'season',
            title: 'Pilot',
            level: 'episode',
            levelLabel: 'Episode',
            contentType: 'tv',
            monitoring: true,
            effectiveMonitoring: true,
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
        );

        expect(await screen.findByTitle('Season 1 > Pilot')).toBeInTheDocument();
        expect(screen.getByText('Season 1')).toBeInTheDocument();
        expect(screen.getByText('Pilot')).toBeInTheDocument();
        expect(screen.getByRole('link')).toHaveAttribute('href', '/library/pilot');
    });
});
