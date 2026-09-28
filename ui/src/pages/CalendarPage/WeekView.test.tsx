import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeAll, describe, expect, it, vi } from 'vitest';

import type { CalendarEvent } from '@/types/calendar';

vi.mock('@/hooks/useLibraryTypes', () => ({
    useTypeIconName: () => () => 'film-slate',
}));

vi.mock('./calendarUtils', async (importActual) => {
    const actual = await importActual<typeof import('./calendarUtils')>();
    return { ...actual, getWeekStart: vi.fn(actual.getWeekStart) };
});

import { getWeekDays, getWeekStart, WEEKDAY_NAMES } from './calendarUtils';
import { WeekView } from './WeekView';
import { TooltipProvider } from '@/components/ui/Tooltip';

let realGetWeekStart: typeof getWeekStart;

beforeAll(async () => {
    const actual = await vi.importActual<typeof import('./calendarUtils')>('./calendarUtils');
    realGetWeekStart = actual.getWeekStart;
});

afterEach(() => {
    const mock = vi.mocked(getWeekStart);
    mock.mockReset();
    mock.mockImplementation(realGetWeekStart);
});

function readHeaderCells(): { name: string; date: string }[] {
    return Array.from(document.querySelectorAll('.calendar-page__weekday-col')).map((col) => ({
        name: col.querySelector('.calendar-page__weekday-name')?.textContent ?? '',
        date: col.querySelector('.calendar-page__weekday-date')?.textContent ?? '',
    }));
}

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
    onEventClick: vi.fn(),
};

describe('WeekView', () => {
    it('renders 7 weekday header columns', () => {
        render(<WeekView {...defaultProps} />, { wrapper: TooltipProvider });
        const dateElements = screen.getAllByTestId('calendar-page__weekday-date');
        expect(dateElements).toHaveLength(7);
    });

    it('calls onDayClick when its separate day button is clicked', async () => {
        const user = userEvent.setup();
        const onDayClick = vi.fn();
        render(
            <WeekView {...defaultProps} getEventsForDate={() => [makeEvent('1', 'Test')]} onDayClick={onDayClick} />,
            { wrapper: TooltipProvider },
        );

        const col = document.querySelector('[data-date="2024-01-14"] > button');
        expect(col).toBeInTheDocument();
        await user.click(col!);
        expect(onDayClick).toHaveBeenCalledTimes(1);
        expect(document.querySelector('button button, [role="button"] button')).toBeNull();
    });

    it('calls onDayClick when the weekday header column is clicked', async () => {
        // Arrange
        const user = userEvent.setup();
        const onDayClick = vi.fn();
        render(<WeekView {...defaultProps} onDayClick={onDayClick} />, { wrapper: TooltipProvider });
        // Act
        await user.click(document.querySelectorAll('button.calendar-page__weekday-col')[0]);
        // Assert
        expect(onDayClick).toHaveBeenCalledTimes(1);
        expect(document.querySelectorAll('button.calendar-page__weekday-col')).toHaveLength(7);
    });

    it('does not call onDayClick when an event card is clicked', async () => {
        const user = userEvent.setup();
        const onDayClick = vi.fn();
        const onEventClick = vi.fn();
        const event = makeEvent('1', 'Test');
        render(
            <WeekView
                {...defaultProps}
                getEventsForDate={() => [event]}
                onDayClick={onDayClick}
                onEventClick={onEventClick}
            />,
            { wrapper: TooltipProvider },
        );

        await user.click(screen.getAllByTestId('calendar-page__event-card')[0]);

        expect(onEventClick).toHaveBeenCalledWith(event);
        expect(onDayClick).not.toHaveBeenCalled();
    });

    it('has the correct data-component attribute', () => {
        const { container } = render(<WeekView {...defaultProps} />, { wrapper: TooltipProvider });
        expect(container.querySelector('[data-component="week-view"]')).toBeInTheDocument();
    });

    it('renders all 7 day columns', () => {
        render(<WeekView {...defaultProps} />, { wrapper: TooltipProvider });
        expect(document.querySelectorAll('.calendar-page__week-col')).toHaveLength(7);
    });

    it('provides readable day headings and empty labels for the stacked mobile presentation', () => {
        render(
            <WeekView
                {...defaultProps}
                getEventsForDate={(date) => (date.getDay() === 1 ? [makeEvent('1', 'Readable title')] : [])}
            />,
            { wrapper: TooltipProvider },
        );

        expect(document.querySelectorAll('.calendar-page__week-day-heading')).toHaveLength(7);
        expect(document.querySelectorAll('.calendar-page__week-day-empty')).toHaveLength(6);
        expect(screen.getByText('Readable title').closest('[data-component="content-lineage-title"]')).toHaveClass(
            'calendar-page__event-title',
        );
    });

    it('shows monitoring and fulfillment separately in the actual compact week path', () => {
        // Arrange
        const inheritedEvent = {
            ...makeEvent('1', 'Inherited external release'),
            monitoring: null,
            effectiveMonitoring: 'Unmonitored' as const,
            fulfillment: 'fulfilledExternally' as const,
        };
        const monitoredEvent = {
            ...makeEvent('2', 'Explicit monitored release'),
            fulfillment: 'fulfilled' as const,
        };

        // Act
        render(
            <WeekView
                {...defaultProps}
                getEventsForDate={(date) => (date.getDay() === 1 ? [inheritedEvent, monitoredEvent] : [])}
            />,
            { wrapper: TooltipProvider },
        );

        // Assert
        const inheritedCard = screen
            .getByText('Inherited external release', { selector: '.content-lineage-title__segment--leaf' })
            .closest('[data-testid="calendar-page__event-card"]');
        const monitoredCard = screen
            .getByText('Explicit monitored release', { selector: '.content-lineage-title__segment--leaf' })
            .closest('[data-testid="calendar-page__event-card"]');

        expect(within(inheritedCard as HTMLElement).getByLabelText('Unmonitored (inherited)')).toBeInTheDocument();
        expect(within(inheritedCard as HTMLElement).getByText('External')).toBeInTheDocument();
        expect(within(monitoredCard as HTMLElement).getByLabelText('Monitored')).toBeInTheDocument();
        expect(within(monitoredCard as HTMLElement).getByText('Fulfilled')).toBeInTheDocument();
    });

    it('keeps the 7-column structure with a long-titled event and a busy day', () => {
        const longTitle =
            'An Extremely Long Scene Title That Would Otherwise Blow Out The Column Width And Break The Seven Column Grid';
        const busyDay = Array.from({ length: 12 }, (_, n) => makeEvent(`busy-${n}`, `Busy Event ${n}`));
        const longEvent = makeEvent('long', longTitle);

        render(
            <WeekView
                {...defaultProps}
                getEventsForDate={(date) => {
                    if (date.getDay() === 6) {
                        return busyDay;
                    }
                    if (date.getDay() === 1) {
                        return [longEvent];
                    }
                    return [];
                }}
            />,
            { wrapper: TooltipProvider },
        );

        const cols = document.querySelectorAll('.calendar-page__week-col');
        expect(cols).toHaveLength(7);
        expect(document.querySelectorAll('[data-date]')).toHaveLength(7);

        const title = screen.getByText(longTitle).closest('[data-component="content-lineage-title"]');
        expect(title).toHaveClass('calendar-page__event-title');

        expect(screen.getAllByText(/Busy Event/, { selector: '.content-lineage-title__segment--leaf' })).toHaveLength(
            12,
        );
    });

    it('labels each column with the weekday of the date beneath it (Sunday-first)', () => {
        render(<WeekView {...defaultProps} />, { wrapper: TooltipProvider });

        const expected = getWeekDays(getWeekStart(defaultProps.currentDate));
        const header = readHeaderCells();
        expect(header).toHaveLength(7);
        header.forEach((cell, i) => {
            expect(cell.name).toBe(WEEKDAY_NAMES[expected[i].getDay()]);
            expect(cell.date).toBe(String(expected[i].getDate()));
        });
        expect(header[0].name).toBe('Sun');
        expect(header[0].date).toBe('14');
    });

    it('formats data-date in local terms under a positive UTC offset', () => {
        vi.stubEnv('TZ', 'Asia/Tokyo');
        try {
            render(<WeekView {...defaultProps} currentDate={new Date(2024, 0, 15)} />, { wrapper: TooltipProvider });

            const cols = document.querySelectorAll('.calendar-page__week-col');
            expect(cols[0].getAttribute('data-date')).toBe('2024-01-14');
            expect(document.querySelector('[data-date="2024-01-13"]')).not.toBeInTheDocument();
            expect(document.querySelector('[data-date="2024-01-15"]')).toBeInTheDocument();
        } finally {
            vi.unstubAllEnvs();
        }
    });

    it('keeps labels aligned with their dates in a Monday-first locale', () => {
        vi.mocked(getWeekStart).mockReturnValue(new Date(2024, 0, 15));
        render(<WeekView {...defaultProps} />, { wrapper: TooltipProvider });

        const expected = getWeekDays(new Date(2024, 0, 15));
        const header = readHeaderCells();
        expect(header).toHaveLength(7);

        expect(header[0].name).toBe('Mon');
        expect(header[0].date).toBe('15');

        header.forEach((cell, i) => {
            expect(cell.name).toBe(WEEKDAY_NAMES[expected[i].getDay()]);
            expect(cell.date).toBe(String(expected[i].getDate()));
        });

        expect(header[6].name).toBe('Sun');
        expect(header[6].date).toBe('21');
    });
});
