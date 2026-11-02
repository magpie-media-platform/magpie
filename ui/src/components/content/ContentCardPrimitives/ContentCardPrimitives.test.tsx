import { act, fireEvent, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';

import { ContentFulfillmentBadge, ContentMonitoringIndicator, ContentPoster } from './ContentCardPrimitives';
import { TooltipProvider } from '@/components/ui/Tooltip';

describe('ContentPoster', () => {
    it('shows a placeholder on failure and retries a replacement URL', () => {
        // Arrange
        const { container, rerender } = render(
            <ContentPoster
                posterUrl="/broken.jpg"
                title="Sample"
                className="poster"
                placeholderClassName="placeholder"
            />,
            { wrapper: TooltipProvider },
        );

        // Act
        fireEvent.error(screen.getByRole('img', { name: 'Sample artwork' }));

        // Assert
        expect(screen.queryByRole('img')).not.toBeInTheDocument();
        expect(container.querySelector('.placeholder')).toHaveAttribute('aria-hidden', 'true');
        rerender(
            <ContentPoster
                posterUrl="/replacement.jpg"
                title="Sample"
                className="poster"
                placeholderClassName="placeholder"
            />,
        );
        expect(screen.getByRole('img', { name: 'Sample artwork' })).toHaveAttribute('src', '/replacement.jpg');
        expect(container.querySelector('.placeholder')).not.toBeInTheDocument();
    });

    it('Poster_ShowsTheBackdropWithoutDuplicatingAltText_WhenTheImageLoads', () => {
        // Arrange
        render(
            <ContentPoster posterUrl="/art.png" title="Sample" className="poster" placeholderClassName="placeholder" />,
        );
        const image = screen.getByRole('img', { name: 'Sample artwork' });

        // Assert
        expect(image).toHaveAttribute('loading', 'lazy');
        expect(image.parentElement).toHaveClass('poster');
        expect(document.querySelector('.content-poster__backdrop')).not.toBeInTheDocument();

        // Act
        fireEvent.load(image);

        // Assert
        expect(screen.getAllByRole('img')).toHaveLength(1);
        expect(document.querySelector('.content-poster__backdrop')).toHaveAttribute('aria-hidden', 'true');
        expect(document.querySelector('.content-poster__backdrop')).toHaveStyle({
            backgroundImage: 'url("/art.png")',
        });
    });

    it('Poster_WaitsForTheNewImage_WhenTheUrlChanges', () => {
        // Arrange
        const { rerender } = render(
            <ContentPoster
                posterUrl="/logo.png"
                title="Sample"
                className="poster"
                placeholderClassName="placeholder"
            />,
        );
        const logo = screen.getByRole('img', { name: 'Sample artwork' });
        fireEvent.load(logo);
        expect(document.querySelector('.content-poster__backdrop')).toHaveStyle({
            backgroundImage: 'url("/logo.png")',
        });

        // Act
        rerender(
            <ContentPoster
                posterUrl="/poster.png"
                title="Sample"
                className="poster"
                placeholderClassName="placeholder"
            />,
        );

        // Assert
        expect(document.querySelector('.content-poster__backdrop')).not.toBeInTheDocument();
        expect(screen.getByRole('img', { name: 'Sample artwork' })).toHaveAttribute('src', '/poster.png');

        // Act
        fireEvent.load(screen.getByRole('img', { name: 'Sample artwork' }));

        // Assert
        expect(document.querySelector('.content-poster__backdrop')).toHaveStyle({
            backgroundImage: 'url("/poster.png")',
        });
    });

    it('Poster_KeepsDecorativeArtworkHidden_WhenEagerLoadingIsRequested', () => {
        // Act
        render(
            <ContentPoster
                posterUrl="/art.png"
                alt=""
                className="poster"
                placeholderClassName="placeholder"
                loading="eager"
                data-testid="artwork"
            />,
        );
        fireEvent.load(screen.getByTestId('artwork'));

        // Assert
        expect(screen.queryByRole('img')).not.toBeInTheDocument();
        expect(screen.getByTestId('artwork')).toHaveAttribute('loading', 'eager');
        expect(screen.getByTestId('artwork')).toHaveAttribute('alt', '');
        expect(document.querySelector('.content-poster__backdrop')).toHaveAttribute('aria-hidden', 'true');
    });

    it('keeps the site placeholder when no URL was supplied and shows the failure glyph only on error', () => {
        // Arrange
        const empty = <span data-testid="site-empty" />;
        const { container, rerender } = render(
            <ContentPoster
                posterUrl={null}
                title="Sample"
                className="poster"
                placeholderClassName="placeholder"
                placeholder={empty}
            />,
            { wrapper: TooltipProvider },
        );

        // Assert
        expect(screen.getByTestId('site-empty')).toBeInTheDocument();
        expect(container.querySelector('.placeholder svg')).not.toBeInTheDocument();

        // Act
        rerender(
            <ContentPoster
                posterUrl="/broken.jpg"
                title="Sample"
                className="poster"
                placeholderClassName="placeholder"
                placeholder={empty}
            />,
        );
        fireEvent.error(screen.getByRole('img', { name: 'Sample artwork' }));

        // Assert
        expect(screen.queryByTestId('site-empty')).not.toBeInTheDocument();
        expect(container.querySelector('.placeholder svg')).toBeInTheDocument();
    });
});

describe('ContentMonitoringIndicator', () => {
    it.each(['Monitored', 'Unmonitored', 'Shotgun'] as const)(
        'shows the %s state as text when a text label is requested',
        (effective) => {
            // Arrange
            // Act
            render(
                <ContentMonitoringIndicator
                    effectiveMonitoring={effective}
                    className="monitoring"
                    labelClassName="monitoring-label"
                />,
                { wrapper: TooltipProvider },
            );

            // Assert
            expect(screen.getByText(effective)).toBeInTheDocument();
            expect(screen.getByLabelText(effective)).toBeInTheDocument();
        },
    );
});

describe('ContentMonitoringIndicator icon', () => {
    it('shows only an icon whose accessible name is the monitoring state', () => {
        // Arrange
        // Act
        render(<ContentMonitoringIndicator effectiveMonitoring="Shotgun" className="monitoring" />, {
            wrapper: TooltipProvider,
        });

        // Assert
        const icon = screen.getByRole('img', { name: 'Shotgun' });
        expect(icon).toHaveAttribute('data-shotgun', 'true');
        expect(icon).not.toHaveTextContent('Shotgun');
    });

    it('shows the monitoring state in a tooltip on hover', async () => {
        // Arrange
        const user = userEvent.setup();
        render(<ContentMonitoringIndicator effectiveMonitoring="Unmonitored" className="monitoring" />, {
            wrapper: TooltipProvider,
        });

        // Act
        await user.hover(screen.getByRole('img', { name: 'Unmonitored' }));

        // Assert
        expect(await screen.findByRole('tooltip')).toHaveTextContent(/^Unmonitored$/);
    });

    it('shows the monitoring state in a tooltip on a touch long press without a click reaching the card', () => {
        // Arrange
        vi.useFakeTimers();
        const onCardClick = vi.fn();
        render(
            <a href="#card" onClick={onCardClick}>
                <ContentMonitoringIndicator effectiveMonitoring="Monitored" className="monitoring" />
            </a>,
            { wrapper: TooltipProvider },
        );
        const icon = screen.getByRole('img', { name: 'Monitored' });

        // Act
        fireEvent.pointerDown(icon, { pointerType: 'touch', clientX: 5, clientY: 5 });
        act(() => {
            vi.advanceTimersByTime(500);
        });
        fireEvent.pointerUp(icon, { pointerType: 'touch' });
        fireEvent.click(icon);
        vi.useRealTimers();

        // Assert
        expect(screen.getByRole('tooltip')).toHaveTextContent(/^Monitored$/);
        expect(onCardClick).not.toHaveBeenCalled();
    });
});

describe('ContentFulfillmentBadge', () => {
    it.each([
        ['missing', 'Missing'],
        ['partial', 'Partial'],
        ['upgradable', 'Upgradable'],
        ['fulfilled', 'Fulfilled'],
        ['fulfilledExternally', 'External'],
    ] as const)('renders %s as %s without a monitoring prerequisite', (fulfillment, label) => {
        // Arrange
        // Act
        render(<ContentFulfillmentBadge fulfillment={fulfillment} />, { wrapper: TooltipProvider });

        // Assert
        expect(screen.getByText(label)).toBeInTheDocument();
    });

    it.each([
        ['Upcoming', 'AFuturePublishDate', '2099-01-01'],
        ['Missing', 'APastPublishDate', '2000-01-01'],
        ['Missing', 'NoPublishDate', undefined],
    ] as const)('ContentFulfillmentBadge_Shows%s_WhenAMissingItemHas%s', (label, _condition, publishDate) => {
        // Act
        render(<ContentFulfillmentBadge fulfillment="missing" publishDate={publishDate} />, {
            wrapper: TooltipProvider,
        });

        // Assert
        expect(screen.getByText(label)).toBeInTheDocument();
    });
});

describe('ContentFulfillmentBadge import pending', () => {
    it('labels a missing item that waits for a discovered file as import pending', () => {
        render(<ContentFulfillmentBadge fulfillment="missing" importPending />, { wrapper: TooltipProvider });

        expect(screen.getByText('Import pending')).toBeInTheDocument();
        expect(screen.queryByText('Missing')).toBeNull();
    });
});
