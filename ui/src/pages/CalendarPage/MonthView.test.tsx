import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';

import type { CalendarEvent } from '@/types/calendar';

import { MonthView } from './MonthView';
import { TooltipProvider } from '@/components/ui/Tooltip';

function makeEvent(id: string, title: string): CalendarEvent {
    return {
        id,
        contentId: `content-${id}`,
        title,
        date: '2024-01-15T20:00:00Z',
        contentType: 'tv',
        level: 'episode',
        fulfillment: 'missing',
        monitoring: 'Monitored',
        effectiveMonitoring: 'Monitored',
    };
}

const defaultProps = {
    currentDate: new Date(2024, 0, 15),
    getEventsForDate: () => [] as CalendarEvent[],
    onDayClick: vi.fn(),
};

describe('MonthView', () => {
    it('aligns the grid and headers with a Monday-first locale', () => {
        // Arrange
        const language = vi.spyOn(navigator, 'language', 'get').mockReturnValue('fr-FR');
        // Act
        const { container } = render(<MonthView {...defaultProps} currentDate={new Date(2024, 8, 15)} />, {
            wrapper: TooltipProvider,
        });
        // Assert
        expect(container.querySelector('.calendar-page__weekday-name')).toHaveTextContent('Mon');
        expect(container.querySelector('[data-date]')).toHaveAttribute('data-date', '2024-08-26');
        language.mockRestore();
    });

    it('renders weekday names header', () => {
        render(<MonthView {...defaultProps} />, { wrapper: TooltipProvider });
        expect(screen.getByText('Sun')).toBeInTheDocument();
        expect(screen.getByText('Sat')).toBeInTheDocument();
    });

    it('renders day cells for the month grid', () => {
        render(<MonthView {...defaultProps} />, { wrapper: TooltipProvider });
        const dayCells = document.querySelectorAll('[data-date]');
        expect(dayCells.length).toBeGreaterThanOrEqual(31);
    });

    it('renders event indicators when events exist on a day', () => {
        const events = [makeEvent('1', 'Shogun')];
        render(<MonthView {...defaultProps} getEventsForDate={() => events} />, { wrapper: TooltipProvider });
        expect(screen.getAllByTestId('calendar-page__day-event').length).toBeGreaterThan(0);
    });

    it.each([
        ['missing', 'Missing'],
        ['partial', 'Partial'],
        ['upgradable', 'Upgradable'],
        ['fulfilled', 'Fulfilled'],
        ['fulfilledExternally', 'External'],
    ] as const)('shows inherited monitoring separately from %s fulfillment in month entries', (fulfillment, label) => {
        // Arrange
        const event = {
            ...makeEvent('1', 'Month release'),
            monitoring: null,
            effectiveMonitoring: 'Unmonitored' as const,
            fulfillment,
        };

        // Act
        render(
            <MonthView
                {...defaultProps}
                getEventsForDate={(date) =>
                    date.getFullYear() === 2024 && date.getMonth() === 0 && date.getDate() === 15 ? [event] : []
                }
            />,
            { wrapper: TooltipProvider },
        );

        // Assert
        const entry = screen.getByTestId('calendar-page__day-event');
        const title = within(entry).getByText('Month release', { selector: '.content-lineage-title__segment--leaf' });
        const monitoring = within(entry).getByLabelText('Unmonitored (inherited)');
        const fulfillmentBadge = within(entry).getByText(label).closest('[data-component="badge"]');

        expect(title.closest('[data-component="content-lineage-title"]')).toHaveClass('calendar-page__day-event-title');
        expect(monitoring.closest('.calendar-page__day-event-monitoring')).toBeInTheDocument();
        expect(fulfillmentBadge).toHaveClass('calendar-page__day-event-badge');
        expect(fulfillmentBadge).toHaveTextContent(label);
    });

    it('shows and activates the day affordance when more than 3 events exist', async () => {
        // Arrange
        const user = userEvent.setup();
        const onDayClick = vi.fn();
        const events = [
            makeEvent('1', 'Event 1'),
            makeEvent('2', 'Event 2'),
            makeEvent('3', 'Event 3'),
            makeEvent('4', 'Event 4'),
        ];

        // Act
        render(
            <MonthView
                {...defaultProps}
                getEventsForDate={(date) => (date.getDate() === 15 && date.getMonth() === 0 ? events : [])}
                onDayClick={onDayClick}
            />,
            { wrapper: TooltipProvider },
        );

        const cell = document.querySelector<HTMLElement>('[data-date="2024-01-15"]');
        const more = within(cell!).getByText('+1 more');

        // Assert
        expect(cell).toHaveAttribute('aria-label', expect.stringContaining('4 events'));
        expect(within(cell!).getAllByTestId('calendar-page__day-event')).toHaveLength(3);

        // Act
        await user.click(more);

        // Assert
        expect(onDayClick).toHaveBeenCalledTimes(1);
    });

    it('calls onDayClick when a day cell is clicked', async () => {
        const user = userEvent.setup();
        const onDayClick = vi.fn();
        render(<MonthView {...defaultProps} getEventsForDate={() => [makeEvent('1', 'x')]} onDayClick={onDayClick} />, {
            wrapper: TooltipProvider,
        });

        const cell = document.querySelector('[data-date="2024-01-01"]');
        expect(cell).toBeInTheDocument();
        await user.click(cell!);
        expect(onDayClick).toHaveBeenCalledTimes(1);
    });

    it('includes padding days from outside the current month', () => {
        render(<MonthView {...defaultProps} />, { wrapper: TooltipProvider });
        const paddingCell = document.querySelector('[data-date="2023-12-31"]');
        expect(paddingCell).toBeInTheDocument();
    });

    it('has the correct data-component attribute', () => {
        const { container } = render(<MonthView {...defaultProps} />, { wrapper: TooltipProvider });
        expect(container.querySelector('[data-component="month-view"]')).toBeInTheDocument();
    });

    it('formats data-date in local terms under a positive UTC offset', () => {
        vi.stubEnv('TZ', 'Asia/Tokyo');
        try {
            render(<MonthView {...defaultProps} currentDate={new Date(2024, 0, 15)} />, { wrapper: TooltipProvider });

            expect(document.querySelector('[data-date="2023-12-31"]')).toBeInTheDocument();
            expect(document.querySelector('[data-date="2023-12-30"]')).not.toBeInTheDocument();
            expect(document.querySelector('[data-date="2024-01-15"]')).toBeInTheDocument();
        } finally {
            vi.unstubAllEnvs();
        }
    });
});
