import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';

import { ContentLineageProvider } from '@/components/content/ContentLineageTitle';
import type { CalendarEvent } from '@/types/calendar';

vi.mock('@/hooks/useLibraryTypes', () => ({
    useTypeIconName: () => () => 'film-slate',
}));

import { EventCard } from './EventCard';
import { TooltipProvider } from '@/components/ui/Tooltip';

function makeEvent(overrides: Partial<CalendarEvent> = {}): CalendarEvent {
    return {
        id: 'evt-1',
        contentId: 'content-1',
        title: 'Shogun',
        subtitle: 'S01E10 - A Dream of a Dream',
        date: '2024-04-15T20:00:00Z',
        posterUrl: 'https://example.com/poster.jpg',
        contentType: 'tv',
        level: 'episode',
        fulfillment: 'missing',
        monitoring: 'Monitored',
        effectiveMonitoring: 'Monitored',
        ...overrides,
    };
}

describe('EventCard', () => {
    afterEach(() => {
        vi.unstubAllEnvs();
    });

    it('renders the event title', () => {
        render(<EventCard event={makeEvent()} onClick={vi.fn()} />, { wrapper: TooltipProvider });
        expect(screen.getByText('Shogun', { selector: '.content-lineage-title__segment--leaf' })).toBeInTheDocument();
    });

    it('renders the subtitle when present', () => {
        render(<EventCard event={makeEvent()} onClick={vi.fn()} />, { wrapper: TooltipProvider });
        expect(screen.getByText('S01E10 - A Dream of a Dream')).toBeInTheDocument();
    });

    it('does not render subtitle when absent', () => {
        render(<EventCard event={makeEvent({ subtitle: undefined })} onClick={vi.fn()} />, {
            wrapper: TooltipProvider,
        });
        expect(screen.queryByText('S01E10 - A Dream of a Dream')).not.toBeInTheDocument();
    });

    it('calls onClick with the event when clicked', async () => {
        const user = userEvent.setup();
        const handleClick = vi.fn();
        const event = makeEvent();
        render(<EventCard event={event} onClick={handleClick} />, { wrapper: TooltipProvider });

        await user.click(screen.getByTestId('calendar-page__event-card'));
        expect(handleClick).toHaveBeenCalledWith(event);
    });

    it('sets data attributes for event and content id', () => {
        render(<EventCard event={makeEvent()} onClick={vi.fn()} />, { wrapper: TooltipProvider });
        const card = screen.getByTestId('calendar-page__event-card');
        expect(card).toHaveAttribute('data-event-id', 'evt-1');
        expect(card).toHaveAttribute('data-content-id', 'content-1');
        expect(card).toHaveAttribute('data-fulfillment', 'missing');
    });

    it('shows poster image when posterUrl is provided', () => {
        const { container } = render(<EventCard event={makeEvent()} onClick={vi.fn()} />, { wrapper: TooltipProvider });
        expect(container.querySelector('img')).toHaveAttribute('src', 'https://example.com/poster.jpg');
    });

    it('shows placeholder when posterUrl is absent', () => {
        const { container } = render(<EventCard event={makeEvent({ posterUrl: undefined })} onClick={vi.fn()} />, {
            wrapper: TooltipProvider,
        });
        expect(container.querySelector('img')).not.toBeInTheDocument();
    });

    it('hides poster in compact mode', () => {
        const { container } = render(<EventCard event={makeEvent()} onClick={vi.fn()} compact />, {
            wrapper: TooltipProvider,
        });
        expect(container.querySelector('img')).not.toBeInTheDocument();
    });

    it.each([
        ['missing', 'Missing'],
        ['partial', 'Partial'],
        ['upgradable', 'Upgradable'],
        ['fulfilled', 'Fulfilled'],
        ['fulfilledExternally', 'External'],
    ] as const)('shows %s fulfillment in compact mode', (fulfillment, label) => {
        // Arrange
        // Act
        render(<EventCard event={makeEvent({ fulfillment })} onClick={vi.fn()} compact />, {
            wrapper: TooltipProvider,
        });

        // Assert
        expect(screen.getByText(label)).toBeInTheDocument();
    });

    it('shows inherited monitoring in compact mode', () => {
        // Arrange
        // Act
        render(
            <EventCard
                event={makeEvent({ monitoring: null, effectiveMonitoring: 'Unmonitored' })}
                onClick={vi.fn()}
                compact
            />,
            { wrapper: TooltipProvider },
        );

        // Assert
        expect(screen.getByLabelText('Unmonitored (inherited)')).toBeInTheDocument();
    });

    it('shows fulfillment badge in non-compact mode', () => {
        render(<EventCard event={makeEvent()} onClick={vi.fn()} />, { wrapper: TooltipProvider });
        expect(screen.getByText('Missing')).toBeInTheDocument();
    });

    it('shows explicit unmonitored and fulfillment states separately', () => {
        // Arrange
        // Act
        render(
            <EventCard
                event={makeEvent({ monitoring: 'Unmonitored', effectiveMonitoring: 'Unmonitored' })}
                onClick={vi.fn()}
            />,
            {
                wrapper: TooltipProvider,
            },
        );

        // Assert
        expect(screen.getByText('Missing')).toBeInTheDocument();
        expect(screen.getByLabelText('Unmonitored')).toBeInTheDocument();
        expect(screen.queryByText('Unmonitored')).not.toBeInTheDocument();
    });

    it('shows inherited unmonitored and fulfillment states separately', () => {
        // Arrange
        // Act
        render(
            <EventCard event={makeEvent({ monitoring: null, effectiveMonitoring: 'Unmonitored' })} onClick={vi.fn()} />,
            {
                wrapper: TooltipProvider,
            },
        );

        // Assert
        expect(screen.getByText('Missing')).toBeInTheDocument();
        expect(screen.getByLabelText('Unmonitored (inherited)')).toBeInTheDocument();
    });

    it('shows date when showDate is true', () => {
        render(<EventCard event={makeEvent()} onClick={vi.fn()} showDate />, { wrapper: TooltipProvider });
        expect(screen.getByTestId('calendar-page__event-date')).toBeInTheDocument();
    });

    it('shows midnight publication dates on their calendar date without a time', () => {
        vi.stubEnv('TZ', 'America/Los_Angeles');
        render(<EventCard event={makeEvent({ date: '2026-07-20T00:00:00Z' })} onClick={vi.fn()} showDate />, {
            wrapper: TooltipProvider,
        });

        expect(screen.getByTestId('calendar-page__event-date')).toHaveTextContent('Mon, Jul 20');
        expect(document.querySelector('.calendar-page__event-time')).not.toBeInTheDocument();
    });

    it('does not show date by default', () => {
        render(<EventCard event={makeEvent()} onClick={vi.fn()} />, { wrapper: TooltipProvider });
        expect(screen.queryByTestId('calendar-page__event-date')).not.toBeInTheDocument();
    });

    it('resolves the contextual title for a nested event', async () => {
        const queryClient = new QueryClient();
        queryClient.setQueryData(['content-lineage', 'show'], { id: 'show', title: 'Breaking Bad' });

        render(
            <QueryClientProvider client={queryClient}>
                <ContentLineageProvider contentTypes={[]}>
                    <EventCard event={makeEvent({ parentId: 'show', title: 'Pilot' })} onClick={vi.fn()} />
                </ContentLineageProvider>
            </QueryClientProvider>,
            { wrapper: TooltipProvider },
        );

        expect(await screen.findByTitle('Breaking Bad > Pilot')).toBeInTheDocument();
        expect(screen.getByText('Breaking Bad')).toBeInTheDocument();
        expect(screen.getByText('Pilot')).toBeInTheDocument();
    });
});
