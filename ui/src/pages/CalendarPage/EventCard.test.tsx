import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';

import type { CalendarEvent } from '@/types/calendar';

import { EventCard } from './EventCard';

function makeEvent(overrides: Partial<CalendarEvent> = {}): CalendarEvent {
    return {
        id: 'evt-1',
        contentId: 'content-1',
        title: 'Shogun',
        subtitle: 'S01E10 - A Dream of a Dream',
        date: '2024-04-15T20:00:00Z',
        posterUrl: 'https://example.com/poster.jpg',
        kind: 'episode',
        fulfillment: 'missing',
        ...overrides,
    };
}

describe('EventCard', () => {
    it('renders the event title', () => {
        render(<EventCard event={makeEvent()} onClick={vi.fn()} />);
        expect(screen.getByText('Shogun')).toBeInTheDocument();
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
        render(<EventCard event={makeEvent()} onClick={vi.fn()} />);
        expect(screen.getByRole('img')).toHaveAttribute('src', 'https://example.com/poster.jpg');
    });

    it('shows placeholder when posterUrl is absent', () => {
        render(<EventCard event={makeEvent({ posterUrl: undefined })} onClick={vi.fn()} />);
        expect(screen.queryByRole('img')).not.toBeInTheDocument();
    });

    it('hides poster in compact mode', () => {
        render(<EventCard event={makeEvent()} onClick={vi.fn()} compact />);
        expect(screen.queryByRole('img')).not.toBeInTheDocument();
    });

    it('hides fulfillment badge in compact mode', () => {
        render(<EventCard event={makeEvent()} onClick={vi.fn()} compact />);
        expect(screen.queryByText('missing')).not.toBeInTheDocument();
    });

    it('shows fulfillment badge in non-compact mode', () => {
        render(<EventCard event={makeEvent()} onClick={vi.fn()} />);
        expect(screen.getByText('missing')).toBeInTheDocument();
    });

    it('shows date when showDate is true', () => {
        render(<EventCard event={makeEvent()} onClick={vi.fn()} showDate />);
        expect(screen.getByTestId('calendar-page__event-date')).toBeInTheDocument();
    });

    it('does not show date by default', () => {
        render(<EventCard event={makeEvent()} onClick={vi.fn()} />);
        expect(screen.queryByTestId('calendar-page__event-date')).not.toBeInTheDocument();
    });
});
