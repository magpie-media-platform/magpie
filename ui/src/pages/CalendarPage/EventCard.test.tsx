import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';

import { ContentLineageProvider } from '@/components/content/ContentLineageTitle';
import type { CalendarEvent } from '@/types/calendar';

vi.mock('@/hooks/useLibraryTypes', () => ({
    useTypeIconName: () => () => 'film-slate',
}));

import { EventCard } from './EventCard';

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
        monitoring: true,
        effectiveMonitoring: true,
        ...overrides,
    };
}

describe('EventCard', () => {
    it('renders the event title', () => {
        render(<EventCard event={makeEvent()} onClick={vi.fn()} />);
        expect(screen.getByText('Shogun', { selector: '.content-lineage-title__segment--leaf' })).toBeInTheDocument();
    });

    it('renders the subtitle when present', () => {
        render(<EventCard event={makeEvent()} onClick={vi.fn()} />);
        expect(screen.getByText('S01E10 - A Dream of a Dream')).toBeInTheDocument();
    });

    it('does not render subtitle when absent', () => {
        render(<EventCard event={makeEvent({ subtitle: undefined })} onClick={vi.fn()} />);
        expect(screen.queryByText('S01E10 - A Dream of a Dream')).not.toBeInTheDocument();
    });

    it('calls onClick with the event when clicked', async () => {
        const user = userEvent.setup();
        const handleClick = vi.fn();
        const event = makeEvent();
        render(<EventCard event={event} onClick={handleClick} />);

        await user.click(screen.getByTestId('calendar-page__event-card'));
        expect(handleClick).toHaveBeenCalledWith(event);
    });

    it('sets data attributes for event and content id', () => {
        render(<EventCard event={makeEvent()} onClick={vi.fn()} />);
        const card = screen.getByTestId('calendar-page__event-card');
        expect(card).toHaveAttribute('data-event-id', 'evt-1');
        expect(card).toHaveAttribute('data-content-id', 'content-1');
        expect(card).toHaveAttribute('data-fulfillment', 'missing');
    });

    it('shows poster image when posterUrl is provided', () => {
        const { container } = render(<EventCard event={makeEvent()} onClick={vi.fn()} />);
        expect(container.querySelector('img')).toHaveAttribute('src', 'https://example.com/poster.jpg');
    });

    it('shows placeholder when posterUrl is absent', () => {
        const { container } = render(<EventCard event={makeEvent({ posterUrl: undefined })} onClick={vi.fn()} />);
        expect(container.querySelector('img')).not.toBeInTheDocument();
    });

    it('hides poster in compact mode', () => {
        const { container } = render(<EventCard event={makeEvent()} onClick={vi.fn()} compact />);
        expect(container.querySelector('img')).not.toBeInTheDocument();
    });

    it('hides fulfillment badge in compact mode', () => {
        render(<EventCard event={makeEvent()} onClick={vi.fn()} compact />);
        expect(screen.queryByText('Missing')).not.toBeInTheDocument();
    });

    it('shows fulfillment badge in non-compact mode', () => {
        render(<EventCard event={makeEvent()} onClick={vi.fn()} />);
        expect(screen.getByText('Missing')).toBeInTheDocument();
    });

    it('shows a not monitored marker instead of the fulfillment badge for an unmonitored event', () => {
        render(<EventCard event={makeEvent({ monitoring: false, effectiveMonitoring: false })} onClick={vi.fn()} />);
        expect(screen.queryByText('Missing')).not.toBeInTheDocument();
        expect(screen.getByText('Not monitored')).toBeInTheDocument();
    });

    it('shows a not monitored marker instead of the fulfillment badge when monitoring is inherited', () => {
        render(<EventCard event={makeEvent({ monitoring: null, effectiveMonitoring: false })} onClick={vi.fn()} />);
        expect(screen.queryByText('Missing')).not.toBeInTheDocument();
        expect(screen.getByText('Not monitored')).toBeInTheDocument();
    });

    it('shows date when showDate is true', () => {
        render(<EventCard event={makeEvent()} onClick={vi.fn()} showDate />);
        expect(screen.getByTestId('calendar-page__event-date')).toBeInTheDocument();
    });

    it('shows midnight publication dates on their calendar date without a time', () => {
        const originalTimeZone = process.env.TZ;
        process.env.TZ = 'America/Los_Angeles';
        try {
            render(<EventCard event={makeEvent({ date: '2026-07-20T00:00:00Z' })} onClick={vi.fn()} showDate />);

            expect(screen.getByTestId('calendar-page__event-date')).toHaveTextContent('Mon, Jul 20');
            expect(document.querySelector('.calendar-page__event-time')).not.toBeInTheDocument();
        } finally {
            if (originalTimeZone === undefined) {
                delete process.env.TZ;
            } else {
                process.env.TZ = originalTimeZone;
            }
        }
    });

    it('does not show date by default', () => {
        render(<EventCard event={makeEvent()} onClick={vi.fn()} />);
        expect(screen.queryByTestId('calendar-page__event-date')).not.toBeInTheDocument();
    });

    it('resolves the contextual title for a nested event', async () => {
        render(
            <ContentLineageProvider
                contentTypes={[]}
                loadQualifiedName={async () => ({ id: 'show', title: 'Breaking Bad' })}
            >
                <EventCard event={makeEvent({ parentId: 'show', title: 'Pilot' })} onClick={vi.fn()} />
            </ContentLineageProvider>,
        );

        expect(await screen.findByTitle('Breaking Bad > Pilot')).toBeInTheDocument();
        expect(screen.getByText('Breaking Bad')).toBeInTheDocument();
        expect(screen.getByText('Pilot')).toBeInTheDocument();
    });
});
