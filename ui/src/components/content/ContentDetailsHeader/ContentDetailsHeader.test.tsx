import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { ContentDetailsHeader, type ContentDetailItem, type ParentContentRef } from './ContentDetailsHeader';
import { TooltipProvider } from '@/components/ui/Tooltip';

function renderWithRouter(ui: React.ReactElement) {
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    return render(
        <QueryClientProvider client={queryClient}>
            <MemoryRouter>{ui}</MemoryRouter>
        </QueryClientProvider>,
        { wrapper: TooltipProvider },
    );
}

const mockContent: ContentDetailItem = {
    id: 'test-1',
    title: 'Test Movie',
    posterUrl: 'https://example.com/poster.jpg',
    contentType: 'movie',
    levelLabel: 'Movie',
    effectiveMonitoring: 'Monitored',
    fulfillment: 'fulfilled',
    year: 2024,
    overview: 'A test movie about testing things.',
};

const mockParent: ParentContentRef = {
    id: 'parent-1',
    title: 'Parent Show',
    path: '/library/parent-1',
};

describe('ContentDetailsHeader', () => {
    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('renders the title', () => {
        renderWithRouter(<ContentDetailsHeader content={mockContent} />);
        expect(screen.getByRole('heading', { name: /Test Movie/i })).toBeInTheDocument();
    });

    it('renders the year when provided', () => {
        renderWithRouter(<ContentDetailsHeader content={mockContent} />);
        expect(screen.getByText('(2024)')).toBeInTheDocument();
    });

    it('does not render year when not provided', () => {
        const contentWithoutYear = { ...mockContent, year: undefined };
        renderWithRouter(<ContentDetailsHeader content={contentWithoutYear} />);
        expect(screen.queryByText(/\(\d{4}\)/)).not.toBeInTheDocument();
    });

    it('renders the in-flight acquisition badge next to the fulfillment badge', () => {
        const downloading = {
            ...mockContent,
            fulfillment: 'missing' as const,
            acquisitionStatus: 'downloading' as const,
        };
        renderWithRouter(<ContentDetailsHeader content={downloading} />);

        const badge = document.querySelector('[data-acquisition-status="downloading"]');
        expect(badge).toBeInTheDocument();
        expect(badge).toHaveTextContent('Downloading');
    });

    it('does not render an acquisition badge when no acquisition is in flight', () => {
        renderWithRouter(<ContentDetailsHeader content={mockContent} />);
        expect(document.querySelector('[data-acquisition-status]')).not.toBeInTheDocument();
    });

    it('renders the poster image', () => {
        renderWithRouter(<ContentDetailsHeader content={mockContent} />);
        const poster = screen.getByAltText('Test Movie artwork');
        expect(poster).toBeInTheDocument();
        expect(poster).toHaveAttribute('src', mockContent.posterUrl);
        fireEvent.error(poster);
        expect(screen.queryByAltText('Test Movie artwork')).not.toBeInTheDocument();
        expect(document.querySelector('.content-details-header__poster--placeholder')).toBeInTheDocument();
    });

    it('renders placeholder when no poster URL provided', () => {
        const contentWithoutPoster = { ...mockContent, posterUrl: undefined };
        renderWithRouter(<ContentDetailsHeader content={contentWithoutPoster} />);
        expect(screen.queryByAltText('Test Movie artwork')).not.toBeInTheDocument();

        const posterContainer = document.querySelector('.content-details-header__poster--placeholder');
        expect(posterContainer).toBeInTheDocument();
    });

    describe('hero preview on the poster', () => {
        const heroPreview = { url: 'https://www.youtube.com/watch?v=KEY123', kind: 'trailer' };

        it('renders a play affordance on the poster when a heroPreview is supplied', () => {
            renderWithRouter(<ContentDetailsHeader content={mockContent} heroPreview={heroPreview} />);
            expect(screen.queryByAltText('Test Movie artwork')).not.toBeInTheDocument();
            expect(screen.getByTestId('content-details-header__poster--preview')).toBeInTheDocument();
            const facade = screen.getByTestId('preview-player-facade');
            expect(facade).toBeInTheDocument();
            expect(screen.getByTestId('preview-player-poster-stage').querySelector('img')).toHaveAttribute(
                'src',
                mockContent.posterUrl,
            );
            expect(facade.closest('[data-component="preview-player"]')).toHaveAttribute('data-fit', 'poster');
        });

        it('renders the plain poster image when no heroPreview is supplied', () => {
            renderWithRouter(<ContentDetailsHeader content={mockContent} />);
            expect(screen.getByAltText('Test Movie artwork')).toBeInTheDocument();
            expect(screen.queryByTestId('content-details-header__poster--preview')).not.toBeInTheDocument();
            expect(screen.queryByTestId('preview-player-facade')).not.toBeInTheDocument();
        });
    });

    it('renders the kind badge', () => {
        renderWithRouter(<ContentDetailsHeader content={mockContent} />);
        expect(screen.getByText('Movie')).toBeInTheDocument();
    });

    it('renders monitoring status separately from fulfillment', () => {
        // Arrange
        // Act
        renderWithRouter(<ContentDetailsHeader content={mockContent} />);

        // Assert
        expect(screen.getByText('Monitored')).toBeInTheDocument();
        expect(screen.getByText('Fulfilled')).toBeInTheDocument();
    });

    it('does not render the publish date in the primary card', () => {
        renderWithRouter(<ContentDetailsHeader content={{ ...mockContent, publishDate: '2024-01-15' }} />);
        expect(screen.queryByText(/Jan 15, 2024/)).not.toBeInTheDocument();
    });

    it('omits the publish date cleanly when absent', () => {
        renderWithRouter(<ContentDetailsHeader content={mockContent} />);
        expect(screen.queryByText(/^Published /)).not.toBeInTheDocument();
    });

    it('omits an unknown publish-date sentinel cleanly', () => {
        renderWithRouter(<ContentDetailsHeader content={{ ...mockContent, publishDate: '1970-01-01' }} />);
        expect(screen.queryByText(/^Published /)).not.toBeInTheDocument();
    });

    it('renders fulfillment badge with correct variant', () => {
        renderWithRouter(<ContentDetailsHeader content={mockContent} />);
        expect(screen.getByText('Fulfilled')).toBeInTheDocument();
    });

    it('renders different fulfillment statuses', () => {
        // Arrange
        const statuses = [
            { status: 'missing', label: 'Missing' },
            { status: 'upgradable', label: 'Upgradable' },
            { status: 'fulfilled', label: 'Fulfilled' },
            { status: 'fulfilledExternally', label: 'External' },
            { status: 'partial', label: 'Partial' },
        ] as const;

        // Act
        statuses.forEach(({ status, label }) => {
            const content = { ...mockContent, fulfillment: status };
            const { unmount } = renderWithRouter(<ContentDetailsHeader content={content} />);

            // Assert
            expect(screen.getByText(label)).toBeInTheDocument();
            unmount();
        });
    });

    it('renders unmonitored and fulfillment states separately', () => {
        // Arrange
        const unmonitoredContent = {
            ...mockContent,
            effectiveMonitoring: 'Unmonitored' as const,
            fulfillment: 'missing' as const,
        };

        // Act
        renderWithRouter(<ContentDetailsHeader content={unmonitoredContent} />);

        // Assert
        expect(screen.getByText('Unmonitored')).toBeInTheDocument();
        expect(screen.getByText('Missing')).toBeInTheDocument();
    });

    it('renders the level label from the shared resolver for different types', () => {
        const cases = [
            { kind: 'episode', contentType: 'tv', levelLabel: 'Episode', label: 'Episode' },
            { kind: 'movie', contentType: 'movie', levelLabel: 'Movie', label: 'Movie' },
            { kind: 'music', contentType: 'music', levelLabel: 'Album', label: 'Album' },
            { kind: 'book', contentType: 'books', levelLabel: 'Book', label: 'Book' },
        ] as const;

        cases.forEach(({ kind, contentType, levelLabel, label }) => {
            const content = { ...mockContent, kind, contentType, levelLabel };
            const { unmount } = renderWithRouter(<ContentDetailsHeader content={content} />);
            expect(screen.getByText(label)).toBeInTheDocument();
            expect(screen.queryByText('Unknown')).not.toBeInTheDocument();
            unmount();
        });
    });

    it('renders overview when provided', () => {
        renderWithRouter(<ContentDetailsHeader content={mockContent} />);
        expect(screen.getByText('A test movie about testing things.')).toBeInTheDocument();
    });

    it('ContentDetailsHeader_RendersReadableOverview_WhenStoredOverviewHasMarkdown', () => {
        // Arrange
        const content = {
            ...mockContent,
            overview:
                "[Comment by Kim Stanley Robinson, on The Guardian's website][1]: The Left Hand of Darkness by Ursula K Le Guin (1969) > One of my favorite novels is The Left Hand of Darkness. [1]: https://www.theguardian.com/books/2011/may/14/science-fiction-authors-choice",
        };

        // Act
        renderWithRouter(<ContentDetailsHeader content={content} />);

        // Assert
        expect(
            screen.getByText(
                "Comment by Kim Stanley Robinson, on The Guardian's website: The Left Hand of Darkness by Ursula K Le Guin (1969) One of my favorite novels is The Left Hand of Darkness.",
            ),
        ).toBeInTheDocument();
    });

    it('does not render overview when not provided', () => {
        const contentWithoutOverview = { ...mockContent, overview: undefined };
        renderWithRouter(<ContentDetailsHeader content={contentWithoutOverview} />);
        expect(screen.queryByText('A test movie about testing things.')).not.toBeInTheDocument();
    });

    describe('overview expansion', () => {
        it('ContentDetailsHeader_OffersNoOverviewToggle_WhenOverviewFits', () => {
            // Arrange
            vi.spyOn(Element.prototype, 'scrollHeight', 'get').mockReturnValue(72);
            vi.spyOn(Element.prototype, 'clientHeight', 'get').mockReturnValue(72);

            // Act
            renderWithRouter(<ContentDetailsHeader content={mockContent} />);

            // Assert
            expect(screen.queryByRole('button', { name: /Show/ })).not.toBeInTheDocument();
        });

        describe('when the overview is clamped', () => {
            beforeEach(() => {
                vi.spyOn(Element.prototype, 'scrollHeight', 'get').mockReturnValue(160);
                vi.spyOn(Element.prototype, 'clientHeight', 'get').mockReturnValue(72);
            });

            it('ContentDetailsHeader_OffersCollapsedOverviewToggle_WhenOverviewIsClamped', () => {
                // Arrange
                // Act
                renderWithRouter(<ContentDetailsHeader content={mockContent} />);

                // Assert
                const toggle = screen.getByRole('button', { name: 'Show more' });
                const overview = screen.getByText('A test movie about testing things.');
                expect(toggle).toHaveAttribute('aria-expanded', 'false');
                expect(toggle).toHaveAttribute('aria-controls', overview.id);
                expect(overview).not.toHaveClass('content-details-header__overview--expanded');
            });

            it('ContentDetailsHeader_ExpandsOverview_WhenToggleIsClicked', async () => {
                // Arrange
                const user = userEvent.setup();
                renderWithRouter(<ContentDetailsHeader content={mockContent} />);

                // Act
                await user.click(screen.getByRole('button', { name: 'Show more' }));

                // Assert
                expect(screen.getByRole('button', { name: 'Show less' })).toHaveAttribute('aria-expanded', 'true');
                expect(screen.getByText('A test movie about testing things.')).toHaveClass(
                    'content-details-header__overview--expanded',
                );
            });

            it('ContentDetailsHeader_CollapsesOverview_WhenExpandedToggleIsClicked', async () => {
                // Arrange
                const user = userEvent.setup();
                renderWithRouter(<ContentDetailsHeader content={mockContent} />);
                await user.click(screen.getByRole('button', { name: 'Show more' }));

                // Act
                await user.click(screen.getByRole('button', { name: 'Show less' }));

                // Assert
                expect(screen.getByRole('button', { name: 'Show more' })).toHaveAttribute('aria-expanded', 'false');
                expect(screen.getByText('A test movie about testing things.')).not.toHaveClass(
                    'content-details-header__overview--expanded',
                );
            });

            it('ContentDetailsHeader_TogglesOverviewFromKeyboard_WhenToggleHasFocus', async () => {
                // Arrange
                const user = userEvent.setup();
                renderWithRouter(<ContentDetailsHeader content={mockContent} />);
                screen.getByRole('button', { name: 'Show more' }).focus();

                // Act
                await user.keyboard('{Enter}');

                // Assert
                expect(screen.getByRole('button', { name: 'Show less' })).toBeInTheDocument();
            });

            it('ContentDetailsHeader_CollapsesOverview_WhenContentChanges', async () => {
                // Arrange
                const user = userEvent.setup();
                const { rerender } = renderWithRouter(<ContentDetailsHeader content={mockContent} />);
                await user.click(screen.getByRole('button', { name: 'Show more' }));

                // Act
                rerender(
                    <QueryClientProvider client={new QueryClient()}>
                        <MemoryRouter>
                            <ContentDetailsHeader
                                content={{ ...mockContent, id: 'test-2', overview: 'Another overview.' }}
                            />
                        </MemoryRouter>
                    </QueryClientProvider>,
                );

                // Assert
                expect(screen.getByRole('button', { name: 'Show more' })).toBeInTheDocument();
                expect(screen.getByText('Another overview.')).not.toHaveClass(
                    'content-details-header__overview--expanded',
                );
            });
        });
    });

    describe('content ratings', () => {
        const ratings = [
            { id: 'r1', body: 'GB', rating: '15' },
            { id: 'r2', body: 'DE', rating: '12' },
            { id: 'r3', body: 'FR', rating: 'U' },
            { id: 'r4', body: 'US', rating: 'PG' },
            { id: 'r5', body: 'JP', rating: 'G' },
            { id: 'r6', body: 'AU', rating: 'M' },
        ];

        it.each([
            ['en-US', /^USPGGB15DE12FRU$/],
            ['de-DE', /^DE12GB15FRUUSPG$/],
            ['fr', /^FRUGB15DE12USPG$/],
        ])('ContentDetailsHeader_ShowsFourRatingsWithLocaleFirst_WhenLocaleIs%s', (locale, expected) => {
            // Arrange
            vi.spyOn(window.navigator, 'language', 'get').mockReturnValue(locale);

            // Act
            renderWithRouter(<ContentDetailsHeader content={mockContent} contentRatings={ratings} />);

            // Assert
            expect(screen.getByRole('list')).toHaveTextContent(expected);
            expect(screen.getByRole('button', { name: 'Show all (2 more)' })).toHaveAttribute('aria-expanded', 'false');
        });

        it('ContentDetailsHeader_ExpandsAndCollapsesRatings_WhenToggleIsClicked', async () => {
            // Arrange
            const user = userEvent.setup();
            renderWithRouter(<ContentDetailsHeader content={mockContent} contentRatings={ratings} />);

            // Act
            await user.click(screen.getByRole('button', { name: 'Show all (2 more)' }));

            // Assert
            expect(screen.getByRole('list')).toHaveTextContent(/^USPGGB15DE12FRUJPGAUM$/);
            expect(screen.getByRole('button', { name: 'Show fewer' })).toHaveAttribute('aria-expanded', 'true');

            // Act
            await user.click(screen.getByRole('button', { name: 'Show fewer' }));

            // Assert
            expect(screen.getByRole('list')).toHaveTextContent(/^USPGGB15DE12FRU$/);
        });

        it('ContentDetailsHeader_TogglesRatingsFromKeyboard_WhenToggleHasFocus', async () => {
            // Arrange
            const user = userEvent.setup();
            renderWithRouter(<ContentDetailsHeader content={mockContent} contentRatings={ratings} />);
            screen.getByRole('button', { name: 'Show all (2 more)' }).focus();

            // Act
            await user.keyboard('{Enter}');

            // Assert
            expect(screen.getByRole('list')).toHaveTextContent(/^USPGGB15DE12FRUJPGAUM$/);
        });

        it('ContentDetailsHeader_ShowsNoRatingsToggle_WhenFourOrFewerRatings', () => {
            // Arrange
            // Act
            renderWithRouter(<ContentDetailsHeader content={mockContent} contentRatings={ratings.slice(0, 4)} />);

            // Assert
            expect(screen.getByRole('list')).toHaveTextContent(/^USPGGB15DE12FRU$/);
            expect(screen.queryByRole('button', { name: /Show/ })).not.toBeInTheDocument();
        });
    });

    it('renders actions when provided', () => {
        renderWithRouter(<ContentDetailsHeader content={mockContent} actions={<button>Test Action</button>} />);
        expect(screen.getByRole('button', { name: 'Test Action' })).toBeInTheDocument();
    });

    it('does not render actions container when actions not provided', () => {
        renderWithRouter(<ContentDetailsHeader content={mockContent} />);
        const actionsContainer = document.querySelector('.content-details-header__actions');
        expect(actionsContainer).not.toBeInTheDocument();
    });

    it('applies data attributes for automation', () => {
        renderWithRouter(<ContentDetailsHeader content={mockContent} />);
        const header = document.querySelector('[data-component="content-details-header"]');
        expect(header).toBeInTheDocument();
        expect(header).toHaveAttribute('data-content-id', 'test-1');
    });

    it('forwards ref', () => {
        const ref = vi.fn();
        renderWithRouter(<ContentDetailsHeader ref={ref} content={mockContent} />);
        expect(ref).toHaveBeenCalledWith(expect.any(HTMLElement));
    });

    it('applies custom className', () => {
        renderWithRouter(<ContentDetailsHeader content={mockContent} className="custom-class" />);
        const header = document.querySelector('.content-details-header');
        expect(header).toHaveClass('custom-class');
    });

    it('passes through additional props', () => {
        renderWithRouter(<ContentDetailsHeader content={mockContent} data-testid="my-header" />);
        expect(screen.getByTestId('my-header')).toBeInTheDocument();
    });

    it('renders as a header element', () => {
        renderWithRouter(<ContentDetailsHeader content={mockContent} />);
        const header = screen.getByRole('banner');
        expect(header).toBeInTheDocument();
    });

    describe('parent link', () => {
        it('renders parent link when parent is provided', () => {
            renderWithRouter(<ContentDetailsHeader content={mockContent} parent={mockParent} />);
            const link = screen.getByRole('link', { name: /Parent Show/i });
            expect(link).toBeInTheDocument();
            expect(link).toHaveAttribute('href', '/library/parent-1');
        });

        it('does not render parent link when parent is not provided', () => {
            renderWithRouter(<ContentDetailsHeader content={mockContent} />);
            const link = screen.queryByRole('link', { name: /Parent Show/i });
            expect(link).not.toBeInTheDocument();
        });

        it('applies data attributes to parent link', () => {
            renderWithRouter(<ContentDetailsHeader content={mockContent} parent={mockParent} />);
            const link = screen.getByRole('link', { name: /Parent Show/i });
            expect(link).toHaveAttribute('data-action', 'navigate-parent');
            expect(link).toHaveAttribute('data-parent-id', 'parent-1');
        });

        it('parent link has correct styling class', () => {
            renderWithRouter(<ContentDetailsHeader content={mockContent} parent={mockParent} />);
            const link = screen.getByRole('link', { name: /Parent Show/i });
            expect(link).toHaveClass('content-details-header__parent-link');
        });
    });
});
