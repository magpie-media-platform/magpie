import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';

import type { CalendarEvent } from '@/types/calendar';

import { MonthView } from './MonthView';

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
    currentDate: new Date(2024, 0, 15), // Jan 15 2024
    viewEvents: [] as CalendarEvent[],
    getEventsForDate: () => [] as CalendarEvent[],
    onDayClick: vi.fn(),
};

describe('MonthView', () => {
    it('renders weekday names header', () => {
        render(<MonthView {...defaultProps} />);
        expect(screen.getByText('Sun')).toBeInTheDocument();
        expect(screen.getByText('Sat')).toBeInTheDocument();
    });

    it('renders day cells for the month grid', () => {
        render(<MonthView {...defaultProps} />);
        // January 2024 has 31 days + padding days; each cell has a data-date attribute
        const dayCells = document.querySelectorAll('[data-date]');
        expect(dayCells.length).toBeGreaterThanOrEqual(31);
    });

    it('shows empty state when no events this month', () => {
        render(<MonthView {...defaultProps} viewEvents={[]} />);
        expect(screen.getByText('No events this month')).toBeInTheDocument();
    });

    it('renders event indicators when events exist on a day', () => {
        const events = [makeEvent('1', 'Shogun')];
        render(<MonthView {...defaultProps} viewEvents={events} getEventsForDate={() => events} />);
        expect(screen.getAllByTestId('calendar-page__day-event').length).toBeGreaterThan(0);
    });

    it('shows +N more when more than 3 events on a day', () => {
        const events = [
            makeEvent('1', 'Event 1'),
            makeEvent('2', 'Event 2'),
            makeEvent('3', 'Event 3'),
            makeEvent('4', 'Event 4'),
        ];
        render(<MonthView {...defaultProps} viewEvents={events} getEventsForDate={() => events} />);
        // Should show "+1 more" on at least one day cell
        expect(screen.getAllByText('+1 more').length).toBeGreaterThan(0);
    });

    it('calls onDayClick when a day cell is clicked', async () => {
        const user = userEvent.setup();
        const onDayClick = vi.fn();
        render(<MonthView {...defaultProps} viewEvents={[makeEvent('1', 'x')]} onDayClick={onDayClick} />);

        // Click a specific day cell by its data-date attribute
        const cell = document.querySelector('[data-date="2024-01-01"]');
        expect(cell).toBeInTheDocument();
        await user.click(cell!);
        expect(onDayClick).toHaveBeenCalledTimes(1);
    });

    it('includes padding days from outside the current month', () => {
        render(<MonthView {...defaultProps} />);
        // Jan 2024 starts on Monday, so Dec 31 2023 should appear as padding
        const paddingCell = document.querySelector('[data-date="2023-12-31"]');
        expect(paddingCell).toBeInTheDocument();
    });

    it('has the correct data-component attribute', () => {
        const { container } = render(<MonthView {...defaultProps} />);
        expect(container.querySelector('[data-component="month-view"]')).toBeInTheDocument();
    });
});
