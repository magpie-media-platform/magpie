import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';

import type { CalendarEvent } from '@/types/calendar';

import { WeekView } from './WeekView';

function makeEvent(id: string, title: string): CalendarEvent {
    return {
        id,
        contentId: `content-${id}`,
        title,
        date: '2024-01-15T20:00:00Z',
        kind: 'episode',
        fulfillment: 'missing',
    };
}

const defaultProps = {
    currentDate: new Date(2024, 0, 15), // Monday Jan 15
    viewEvents: [] as CalendarEvent[],
    getEventsForDate: () => [] as CalendarEvent[],
    onDayClick: vi.fn(),
    onEventClick: vi.fn(),
};

describe('WeekView', () => {
    it('renders 7 weekday header columns', () => {
        render(<WeekView {...defaultProps} />);
        const dateElements = screen.getAllByTestId('calendar-page__weekday-date');
        expect(dateElements).toHaveLength(7);
    });

    it('shows empty state when no events this week', () => {
        render(<WeekView {...defaultProps} viewEvents={[]} />);
        expect(screen.getByText('No events this week')).toBeInTheDocument();
    });

    it('does not show empty state when events are present', () => {
        const events = [makeEvent('1', 'Shogun')];
        render(<WeekView {...defaultProps} viewEvents={events} getEventsForDate={() => events} />);
        expect(screen.queryByText('No events this week')).not.toBeInTheDocument();
    });

    it('calls onDayClick when a day column is clicked', async () => {
        const user = userEvent.setup();
        const onDayClick = vi.fn();
        const events = [makeEvent('1', 'Test')];
        render(<WeekView {...defaultProps} viewEvents={events} onDayClick={onDayClick} />);

        // Click on the first day column by its data-date attribute
        const col = document.querySelector('[data-date="2024-01-14"]');
        expect(col).toBeInTheDocument();
        await user.click(col!);
        expect(onDayClick).toHaveBeenCalledTimes(1);
    });

    it('has the correct data-component attribute', () => {
        const { container } = render(<WeekView {...defaultProps} />);
        expect(container.querySelector('[data-component="week-view"]')).toBeInTheDocument();
    });
});
