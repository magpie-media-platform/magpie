import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';

import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi, beforeEach } from 'vitest';
import { MemoryRouter, useSearchParams } from 'react-router-dom';

import { CalendarPage } from './CalendarPage';
import type { CalendarEvent } from '@/types/calendar';

const mockUseExtensions = vi.fn();
const mockUseSlotExtensions = vi.fn();

vi.mock('@/extensions/useExtensions', () => ({
    useExtensions: () => mockUseExtensions(),
    useSlotExtensions: (slot: string) => mockUseSlotExtensions(slot),
}));

const mockNavigate = vi.fn();
vi.mock('react-router-dom', async () => {
    const actual = await vi.importActual('react-router-dom');
    return {
        ...actual,
        useNavigate: () => mockNavigate,
    };
});

vi.mock('@/hooks/useLibraryTypes', () => ({
    useTypeIconName: () => () => 'film-slate',
}));

const mockEvents: CalendarEvent[] = [
    {
        id: 'evt-1',
        contentId: 'content-1',
        title: 'Shogun',
        subtitle: 'S01E10 - A Dream of a Dream',
        date: new Date().toISOString(),
        posterUrl: 'https://image.provider-a.example/t/p/w92/poster1.jpg',
        contentType: 'tv',
        level: 'episode',
        levelLabel: 'Episode',
        fulfillment: 'missing',
        monitoring: true,
        effectiveMonitoring: true,
    },
    {
        id: 'evt-2',
        contentId: 'content-2',
        title: 'Fallout',
        subtitle: 'S01E08 - The Beginning',
        date: new Date().toISOString(),
        posterUrl: 'https://image.provider-a.example/t/p/w92/poster2.jpg',
        contentType: 'tv',
        level: 'episode',
        levelLabel: 'Episode',
        fulfillment: 'fulfilled',
        monitoring: true,
        effectiveMonitoring: true,
    },
    {
        id: 'evt-3',
        contentId: 'content-3',
        title: 'Dune: Part Two',
        date: new Date(Date.now() + 86400000).toISOString(),
        posterUrl: 'https://image.provider-a.example/t/p/w92/poster3.jpg',
        contentType: 'movie',
        level: 'movie',
        levelLabel: 'Movie',
        fulfillment: 'missing',
        monitoring: true,
        effectiveMonitoring: true,
    },
];

const mockRefetch = vi.fn();
const mockUseCalendar = vi.fn();
vi.mock('@/hooks/useCalendar', () => ({
    useCalendar: () => mockUseCalendar(),
}));

describe('CalendarPage', () => {
    const defaultContext = {
        apiBaseUrl: '/api',
        userId: 'user-123',
        username: 'testuser',
        role: 'Admin',
    };

    const noExtensions = {
        extensions: [],
        isReady: true,
        isLoading: false,
    };

    beforeEach(() => {
        vi.clearAllMocks();
        mockUseExtensions.mockReturnValue(defaultContext);
        mockUseSlotExtensions.mockReturnValue(noExtensions);
        mockUseCalendar.mockReturnValue({
            events: mockEvents,
            isLoading: false,
            isFetching: false,
            error: null,
            refetch: mockRefetch,
        });
    });

    it('renders with correct data-page attribute', () => {
        render(
            <MemoryRouter>
                <CalendarPage />
            </MemoryRouter>,
        );

        expect(document.querySelector('[data-page="calendar"]')).toBeInTheDocument();
    });

    it('renders the page title', () => {
        render(
            <MemoryRouter>
                <CalendarPage />
            </MemoryRouter>,
        );

        expect(screen.getByRole('heading', { name: 'Calendar', level: 1 })).toBeInTheDocument();
    });

    it('renders the page icon as decorative', () => {
        render(
            <MemoryRouter>
                <CalendarPage />
            </MemoryRouter>,
        );

        const icon = screen.getByTestId('calendar-page__icon');
        expect(icon).toHaveAttribute('aria-hidden', 'true');
    });

    it('shows loading skeletons when events are loading', () => {
        mockUseCalendar.mockReturnValue({
            events: [],
            isLoading: true,
            isFetching: true,
            error: null,
            refetch: mockRefetch,
        });

        render(
            <MemoryRouter>
                <CalendarPage />
            </MemoryRouter>,
        );

        expect(screen.getByTestId('calendar-loading')).toBeInTheDocument();
        const skeletons = document.querySelectorAll('[data-component="skeleton"]');
        expect(skeletons.length).toBeGreaterThan(0);
    });

    it('hides loading skeletons and shows calendar when loaded', () => {
        render(
            <MemoryRouter>
                <CalendarPage />
            </MemoryRouter>,
        );

        expect(screen.queryByTestId('calendar-loading')).not.toBeInTheDocument();
        expect(document.querySelector('[data-component="month-view"]')).toBeInTheDocument();
    });
});

describe('CalendarPage view switcher', () => {
    const defaultContext = {
        apiBaseUrl: '/api',
        userId: 'user-123',
        username: 'testuser',
        role: 'Admin',
    };

    const noExtensions = {
        extensions: [],
        isReady: true,
        isLoading: false,
    };

    beforeEach(() => {
        vi.clearAllMocks();
        mockUseExtensions.mockReturnValue(defaultContext);
        mockUseSlotExtensions.mockReturnValue(noExtensions);
        mockUseCalendar.mockReturnValue({
            events: mockEvents,
            isLoading: false,
            isFetching: false,
            error: null,
            refetch: mockRefetch,
        });
    });

    it('renders view switcher buttons', () => {
        render(
            <MemoryRouter>
                <CalendarPage />
            </MemoryRouter>,
        );

        expect(document.querySelector('[data-view="month"]')).toBeInTheDocument();
        expect(document.querySelector('[data-view="week"]')).toBeInTheDocument();
        expect(document.querySelector('[data-view="day"]')).toBeInTheDocument();
        expect(document.querySelector('[data-view="list"]')).toBeInTheDocument();
    });

    it('defaults to month view', () => {
        render(
            <MemoryRouter>
                <CalendarPage />
            </MemoryRouter>,
        );

        const monthBtn = document.querySelector('[data-view="month"]');
        expect(monthBtn).toHaveAttribute('data-active', 'true');
        expect(document.querySelector('[data-component="month-view"]')).toBeInTheDocument();
    });

    it('switches to week view when week button is clicked', async () => {
        const user = userEvent.setup();

        render(
            <MemoryRouter>
                <CalendarPage />
            </MemoryRouter>,
        );

        const weekBtn = document.querySelector('[data-view="week"]');
        await user.click(weekBtn!);

        expect(weekBtn).toHaveAttribute('data-active', 'true');
        expect(document.querySelector('[data-component="week-view"]')).toBeInTheDocument();
    });

    it('switches to day view when day button is clicked', async () => {
        const user = userEvent.setup();

        render(
            <MemoryRouter>
                <CalendarPage />
            </MemoryRouter>,
        );

        const dayBtn = document.querySelector('[data-view="day"]');
        await user.click(dayBtn!);

        expect(dayBtn).toHaveAttribute('data-active', 'true');
        expect(document.querySelector('[data-component="day-view"]')).toBeInTheDocument();
    });

    it('switches to list view when list button is clicked', async () => {
        const user = userEvent.setup();

        render(
            <MemoryRouter>
                <CalendarPage />
            </MemoryRouter>,
        );

        const listBtn = document.querySelector('[data-view="list"]');
        await user.click(listBtn!);

        expect(listBtn).toHaveAttribute('data-active', 'true');
        expect(document.querySelector('[data-component="list-view"]')).toBeInTheDocument();
    });

    it('has proper aria-pressed on view buttons', () => {
        render(
            <MemoryRouter>
                <CalendarPage />
            </MemoryRouter>,
        );

        const monthBtn = document.querySelector('[data-view="month"]');
        expect(monthBtn).toHaveAttribute('aria-pressed', 'true');

        const weekBtn = document.querySelector('[data-view="week"]');
        expect(weekBtn).toHaveAttribute('aria-pressed', 'false');
    });

    it('renders visible labels on all view buttons', () => {
        render(
            <MemoryRouter>
                <CalendarPage />
            </MemoryRouter>,
        );

        expect(screen.getByText('Month')).toBeInTheDocument();
        expect(screen.getByText('Week')).toBeInTheDocument();
        expect(screen.getByText('Day')).toBeInTheDocument();
        expect(screen.getByText('List')).toBeInTheDocument();
    });
});

describe('CalendarPage navigation', () => {
    const defaultContext = {
        apiBaseUrl: '/api',
        userId: 'user-123',
        username: 'testuser',
        role: 'Admin',
    };

    const noExtensions = {
        extensions: [],
        isReady: true,
        isLoading: false,
    };

    beforeEach(() => {
        vi.clearAllMocks();
        mockUseExtensions.mockReturnValue(defaultContext);
        mockUseSlotExtensions.mockReturnValue(noExtensions);
        mockUseCalendar.mockReturnValue({
            events: mockEvents,
            isLoading: false,
            isFetching: false,
            error: null,
            refetch: mockRefetch,
        });
    });

    it('renders navigation controls', () => {
        render(
            <MemoryRouter>
                <CalendarPage />
            </MemoryRouter>,
        );

        expect(document.querySelector('[data-action="prev"]')).toBeInTheDocument();
        expect(document.querySelector('[data-action="next"]')).toBeInTheDocument();
        expect(document.querySelector('[data-action="today"]')).toBeInTheDocument();
    });

    it('renders current month in header title', () => {
        render(
            <MemoryRouter>
                <CalendarPage />
            </MemoryRouter>,
        );

        const now = new Date();
        const monthName = now.toLocaleDateString(undefined, { month: 'long', year: 'numeric' });
        expect(screen.getByText(monthName)).toBeInTheDocument();
    });

    it('navigates to previous month when prev is clicked', async () => {
        const user = userEvent.setup();

        render(
            <MemoryRouter>
                <CalendarPage />
            </MemoryRouter>,
        );

        const now = new Date();
        const prevMonth = new Date(now.getFullYear(), now.getMonth() - 1, 1);
        const prevMonthName = prevMonth.toLocaleDateString(undefined, { month: 'long', year: 'numeric' });

        const prevBtn = document.querySelector('[data-action="prev"]');
        await user.click(prevBtn!);

        expect(screen.getByText(prevMonthName)).toBeInTheDocument();
    });

    it('navigates to next month when next is clicked', async () => {
        const user = userEvent.setup();

        render(
            <MemoryRouter>
                <CalendarPage />
            </MemoryRouter>,
        );

        const now = new Date();
        const nextMonth = new Date(now.getFullYear(), now.getMonth() + 1, 1);
        const nextMonthName = nextMonth.toLocaleDateString(undefined, { month: 'long', year: 'numeric' });

        const nextBtn = document.querySelector('[data-action="next"]');
        await user.click(nextBtn!);

        expect(screen.getByText(nextMonthName)).toBeInTheDocument();
    });
});

describe('CalendarPage month overflow', () => {
    const defaultContext = {
        apiBaseUrl: '/api',
        userId: 'user-123',
        username: 'testuser',
        role: 'Admin',
    };

    const noExtensions = {
        extensions: [],
        isReady: true,
        isLoading: false,
    };

    beforeEach(() => {
        vi.useFakeTimers();
        vi.clearAllMocks();
        mockUseExtensions.mockReturnValue(defaultContext);
        mockUseSlotExtensions.mockReturnValue(noExtensions);
        mockUseCalendar.mockReturnValue({
            events: mockEvents,
            isLoading: false,
            isFetching: false,
            error: null,
            refetch: mockRefetch,
        });
    });

    afterEach(() => {
        vi.useRealTimers();
    });

    it('does not overflow day-of-month when navigating forward from Jan 31', () => {
        vi.setSystemTime(new Date(2025, 0, 31));

        render(
            <MemoryRouter>
                <CalendarPage />
            </MemoryRouter>,
        );

        const nextBtn = document.querySelector('[data-action="next"]');
        fireEvent.click(nextBtn!);

        const expected = new Date(2025, 1, 1).toLocaleDateString(undefined, { month: 'long', year: 'numeric' });
        expect(screen.getByText(expected)).toBeInTheDocument();
    });

    it('does not overflow day-of-month when navigating backward from Mar 31', () => {
        vi.setSystemTime(new Date(2025, 2, 31));

        render(
            <MemoryRouter>
                <CalendarPage />
            </MemoryRouter>,
        );

        const prevBtn = document.querySelector('[data-action="prev"]');
        fireEvent.click(prevBtn!);

        const expected = new Date(2025, 1, 1).toLocaleDateString(undefined, { month: 'long', year: 'numeric' });
        expect(screen.getByText(expected)).toBeInTheDocument();
    });
});

describe('CalendarPage month view', () => {
    const defaultContext = {
        apiBaseUrl: '/api',
        userId: 'user-123',
        username: 'testuser',
        role: 'Admin',
    };

    const noExtensions = {
        extensions: [],
        isReady: true,
        isLoading: false,
    };

    beforeEach(() => {
        vi.clearAllMocks();
        mockUseExtensions.mockReturnValue(defaultContext);
        mockUseSlotExtensions.mockReturnValue(noExtensions);
        mockUseCalendar.mockReturnValue({
            events: mockEvents,
            isLoading: false,
            isFetching: false,
            error: null,
            refetch: mockRefetch,
        });
    });

    it('renders weekday headers', () => {
        render(
            <MemoryRouter>
                <CalendarPage />
            </MemoryRouter>,
        );

        expect(screen.getByText('Sun')).toBeInTheDocument();
        expect(screen.getByText('Mon')).toBeInTheDocument();
        expect(screen.getByText('Tue')).toBeInTheDocument();
        expect(screen.getByText('Wed')).toBeInTheDocument();
        expect(screen.getByText('Thu')).toBeInTheDocument();
        expect(screen.getByText('Fri')).toBeInTheDocument();
        expect(screen.getByText('Sat')).toBeInTheDocument();
    });

    it('renders day cells', () => {
        render(
            <MemoryRouter>
                <CalendarPage />
            </MemoryRouter>,
        );

        const dayCells = document.querySelectorAll('[data-date]');
        expect(dayCells.length).toBeGreaterThanOrEqual(28);
    });

    it('highlights today cell', () => {
        render(
            <MemoryRouter>
                <CalendarPage />
            </MemoryRouter>,
        );

        const todayCell = screen.getByTestId('calendar-page__day-cell--today');
        expect(todayCell).toBeInTheDocument();
    });

    it('renders events on day cells', () => {
        render(
            <MemoryRouter>
                <CalendarPage />
            </MemoryRouter>,
        );

        const dayEvents = screen.getAllByTestId('calendar-page__day-event');
        expect(dayEvents.length).toBeGreaterThan(0);
    });

    it('switches to day view when a day cell is clicked', async () => {
        const user = userEvent.setup();

        render(
            <MemoryRouter>
                <CalendarPage />
            </MemoryRouter>,
        );

        const todayCell = screen.getByTestId('calendar-page__day-cell--today');
        await user.click(todayCell!);

        expect(document.querySelector('[data-component="day-view"]')).toBeInTheDocument();
    });
});

describe('CalendarPage day cell layout', () => {
    it('keeps the day cell top-anchored so the date does not drift with event count', () => {
        const css = readFileSync(resolve(process.cwd(), 'src/pages/CalendarPage/CalendarPage.css'), 'utf8');
        const dayCell = css.match(/\.calendar-page__day-cell\s*{(?<declarations>[\s\S]*?)\n {4}}/)?.groups
            ?.declarations;

        expect(dayCell).toContain('flex-direction: column;');
        expect(dayCell).toContain('justify-content: flex-start;');
    });
});

describe('CalendarPage week view', () => {
    const defaultContext = {
        apiBaseUrl: '/api',
        userId: 'user-123',
        username: 'testuser',
        role: 'Admin',
    };

    const noExtensions = {
        extensions: [],
        isReady: true,
        isLoading: false,
    };

    beforeEach(() => {
        vi.clearAllMocks();
        mockUseExtensions.mockReturnValue(defaultContext);
        mockUseSlotExtensions.mockReturnValue(noExtensions);
        mockUseCalendar.mockReturnValue({
            events: mockEvents,
            isLoading: false,
            isFetching: false,
            error: null,
            refetch: mockRefetch,
        });
    });

    it('renders 7 columns for days of the week', async () => {
        const user = userEvent.setup();

        render(
            <MemoryRouter>
                <CalendarPage />
            </MemoryRouter>,
        );

        const weekBtn = document.querySelector('[data-view="week"]');
        await user.click(weekBtn!);

        const weekCols = document.querySelectorAll('[data-component="week-view"] [data-date]');
        expect(weekCols.length).toBe(7);
    });

    it('shows weekday dates in header', async () => {
        const user = userEvent.setup();

        render(
            <MemoryRouter>
                <CalendarPage />
            </MemoryRouter>,
        );

        const weekBtn = document.querySelector('[data-view="week"]');
        await user.click(weekBtn!);

        const weekdayDates = screen.getAllByTestId('calendar-page__weekday-date');
        expect(weekdayDates.length).toBe(7);
    });

    it('highlights today column', async () => {
        const user = userEvent.setup();

        render(
            <MemoryRouter>
                <CalendarPage />
            </MemoryRouter>,
        );

        const weekBtn = document.querySelector('[data-view="week"]');
        await user.click(weekBtn!);

        const todayCol = screen.getByTestId('calendar-page__week-col--today');
        expect(todayCol).toBeInTheDocument();
    });
});

describe('CalendarPage event cards', () => {
    const defaultContext = {
        apiBaseUrl: '/api',
        userId: 'user-123',
        username: 'testuser',
        role: 'Admin',
    };

    const noExtensions = {
        extensions: [],
        isReady: true,
        isLoading: false,
    };

    beforeEach(() => {
        vi.clearAllMocks();
        mockUseExtensions.mockReturnValue(defaultContext);
        mockUseSlotExtensions.mockReturnValue(noExtensions);
        mockUseCalendar.mockReturnValue({
            events: mockEvents,
            isLoading: false,
            isFetching: false,
            error: null,
            refetch: mockRefetch,
        });
    });

    it('renders event cards in day view', async () => {
        const user = userEvent.setup();

        render(
            <MemoryRouter>
                <CalendarPage />
            </MemoryRouter>,
        );

        const dayBtn = document.querySelector('[data-view="day"]');
        await user.click(dayBtn!);

        const eventCards = screen.getAllByTestId('calendar-page__event-card');
        expect(eventCards.length).toBeGreaterThan(0);
    });

    it('renders event cards with fulfillment status styling', async () => {
        const user = userEvent.setup();

        render(
            <MemoryRouter>
                <CalendarPage />
            </MemoryRouter>,
        );

        const dayBtn = document.querySelector('[data-view="day"]');
        await user.click(dayBtn!);

        const eventCard = screen.getAllByTestId('calendar-page__event-card')[0];
        expect(eventCard).toHaveAttribute('data-fulfillment');
    });

    it('navigates to content details when event is clicked', async () => {
        const user = userEvent.setup();

        render(
            <MemoryRouter>
                <CalendarPage />
            </MemoryRouter>,
        );

        const dayBtn = document.querySelector('[data-view="day"]');
        await user.click(dayBtn!);

        const eventCard = screen.getAllByTestId('calendar-page__event-card')[0];
        const contentId = eventCard?.getAttribute('data-content-id');

        await user.click(eventCard!);

        expect(mockNavigate).toHaveBeenCalledWith(`/library/${contentId}`);
    });

    it('shows event title and subtitle', async () => {
        const user = userEvent.setup();

        render(
            <MemoryRouter>
                <CalendarPage />
            </MemoryRouter>,
        );

        const dayBtn = document.querySelector('[data-view="day"]');
        await user.click(dayBtn!);

        expect(screen.getByText('Shogun')).toBeInTheDocument();
    });
});

describe('CalendarPage list view', () => {
    const defaultContext = {
        apiBaseUrl: '/api',
        userId: 'user-123',
        username: 'testuser',
        role: 'Admin',
    };

    const noExtensions = {
        extensions: [],
        isReady: true,
        isLoading: false,
    };

    beforeEach(() => {
        vi.clearAllMocks();
        mockUseExtensions.mockReturnValue(defaultContext);
        mockUseSlotExtensions.mockReturnValue(noExtensions);
        mockUseCalendar.mockReturnValue({
            events: mockEvents,
            isLoading: false,
            isFetching: false,
            error: null,
            refetch: mockRefetch,
        });
    });

    it('renders upcoming events in list view', async () => {
        const user = userEvent.setup();

        render(
            <MemoryRouter>
                <CalendarPage />
            </MemoryRouter>,
        );

        const listBtn = document.querySelector('[data-view="list"]');
        await user.click(listBtn!);

        const listItems = document.querySelectorAll(
            '[data-testid="calendar-page__list-items"] [data-testid="calendar-page__event-card"]',
        );
        expect(listItems.length).toBeGreaterThan(0);
    });

    it('shows date in list view event cards', async () => {
        const user = userEvent.setup();

        render(
            <MemoryRouter>
                <CalendarPage />
            </MemoryRouter>,
        );

        const listBtn = document.querySelector('[data-view="list"]');
        await user.click(listBtn!);

        const eventDates = screen.getAllByTestId('calendar-page__event-date');
        expect(eventDates.length).toBeGreaterThan(0);
    });

    it('includes timed events on the final day', async () => {
        const user = userEvent.setup();
        const finalDay = new Date();
        finalDay.setHours(0, 0, 0, 0);
        finalDay.setDate(finalDay.getDate() + 6);
        finalDay.setHours(20);
        mockUseCalendar.mockReturnValue({
            events: [{ ...mockEvents[0], id: 'final-day', title: 'Final day event', date: finalDay.toISOString() }],
            isLoading: false,
            isFetching: false,
            error: null,
            refetch: mockRefetch,
        });

        render(
            <MemoryRouter>
                <CalendarPage />
            </MemoryRouter>,
        );

        await user.click(document.querySelector('[data-view="list"]')!);

        expect(screen.getByText('Final day event')).toBeInTheDocument();
    });
});

describe('CalendarPage empty state', () => {
    const defaultContext = {
        apiBaseUrl: '/api',
        userId: 'user-123',
        username: 'testuser',
        role: 'Admin',
    };

    const noExtensions = {
        extensions: [],
        isReady: true,
        isLoading: false,
    };

    beforeEach(() => {
        vi.clearAllMocks();
        mockUseExtensions.mockReturnValue(defaultContext);
        mockUseSlotExtensions.mockReturnValue(noExtensions);
        mockUseCalendar.mockReturnValue({
            events: mockEvents,
            isLoading: false,
            isFetching: false,
            error: null,
            refetch: mockRefetch,
        });
    });

    it('shows empty state in day view when no events', async () => {
        const user = userEvent.setup();

        render(
            <MemoryRouter>
                <CalendarPage />
            </MemoryRouter>,
        );

        const prevBtn = document.querySelector('[data-action="prev"]');
        for (let i = 0; i < 6; i++) {
            await user.click(prevBtn!);
        }

        const dayBtn = document.querySelector('[data-view="day"]');
        await user.click(dayBtn!);

        expect(screen.getByText('No events on this day')).toBeInTheDocument();
        const emptyState = document.querySelector('[data-component="day-view"] [data-component="empty-state"]');
        expect(emptyState).toBeInTheDocument();
    });

    it('does not show the empty-state banner on month or week views', async () => {
        const user = userEvent.setup();

        render(
            <MemoryRouter>
                <CalendarPage />
            </MemoryRouter>,
        );

        const prevBtn = document.querySelector('[data-action="prev"]');
        for (let i = 0; i < 6; i++) {
            await user.click(prevBtn!);
        }

        expect(document.querySelector('[data-component="month-view"] [data-component="empty-state"]')).toBeNull();

        const weekBtn = document.querySelector('[data-view="week"]');
        await user.click(weekBtn!);
        expect(document.querySelector('[data-component="week-view"] [data-component="empty-state"]')).toBeNull();
    });

    it('uses shared EmptyState component with description text on day view', async () => {
        const user = userEvent.setup();

        render(
            <MemoryRouter>
                <CalendarPage />
            </MemoryRouter>,
        );

        const prevBtn = document.querySelector('[data-action="prev"]');
        for (let i = 0; i < 6; i++) {
            await user.click(prevBtn!);
        }

        const dayBtn = document.querySelector('[data-view="day"]');
        await user.click(dayBtn!);

        expect(screen.getByText('Try a different date or check other views')).toBeInTheDocument();
    });
});

describe('CalendarPage accessibility', () => {
    const defaultContext = {
        apiBaseUrl: '/api',
        userId: 'user-123',
        username: 'testuser',
        role: 'Admin',
    };

    const noExtensions = {
        extensions: [],
        isReady: true,
        isLoading: false,
    };

    beforeEach(() => {
        vi.clearAllMocks();
        mockUseExtensions.mockReturnValue(defaultContext);
        mockUseSlotExtensions.mockReturnValue(noExtensions);
        mockUseCalendar.mockReturnValue({
            events: mockEvents,
            isLoading: false,
            isFetching: false,
            error: null,
            refetch: mockRefetch,
        });
    });

    it('has accessible view buttons with aria-labels', () => {
        render(
            <MemoryRouter>
                <CalendarPage />
            </MemoryRouter>,
        );

        expect(document.querySelector('[data-view="month"]')).toHaveAttribute('aria-label', 'Month view');
        expect(document.querySelector('[data-view="week"]')).toHaveAttribute('aria-label', 'Week view');
        expect(document.querySelector('[data-view="day"]')).toHaveAttribute('aria-label', 'Day view');
        expect(document.querySelector('[data-view="list"]')).toHaveAttribute('aria-label', 'List view');
    });

    it('has accessible navigation buttons', () => {
        render(
            <MemoryRouter>
                <CalendarPage />
            </MemoryRouter>,
        );

        expect(document.querySelector('[data-action="prev"]')).toHaveAttribute('aria-label', 'Previous');
        expect(document.querySelector('[data-action="next"]')).toHaveAttribute('aria-label', 'Next');
    });

    it('has accessible day cells with aria-labels', () => {
        render(
            <MemoryRouter>
                <CalendarPage />
            </MemoryRouter>,
        );

        const dayCells = document.querySelectorAll('[data-date]');
        dayCells.forEach((cell) => {
            expect(cell).toHaveAttribute('aria-label');
        });
    });

    it('renders icons as decorative', () => {
        render(
            <MemoryRouter>
                <CalendarPage />
            </MemoryRouter>,
        );

        const icon = screen.getByTestId('calendar-page__icon');
        expect(icon).toHaveAttribute('aria-hidden', 'true');
    });
});

describe('CalendarPage URL persistence', () => {
    const defaultContext = {
        apiBaseUrl: '/api',
        userId: 'user-123',
        username: 'testuser',
        role: 'Admin',
    };

    const noExtensions = {
        extensions: [],
        isReady: true,
        isLoading: false,
    };

    beforeEach(() => {
        vi.clearAllMocks();
        mockUseExtensions.mockReturnValue(defaultContext);
        mockUseSlotExtensions.mockReturnValue(noExtensions);
        mockUseCalendar.mockReturnValue({
            events: mockEvents,
            isLoading: false,
            isFetching: false,
            error: null,
            refetch: mockRefetch,
        });
    });

    function UrlObserver() {
        const [params] = useSearchParams();
        return <span data-testid="url-params">{params.toString()}</span>;
    }

    it('restores view from ?view= search param', () => {
        render(
            <MemoryRouter initialEntries={['/calendar?view=week']}>
                <CalendarPage />
                <UrlObserver />
            </MemoryRouter>,
        );

        const weekBtn = document.querySelector('[data-view="week"]');
        expect(weekBtn).toHaveAttribute('data-active', 'true');
        expect(document.querySelector('[data-component="week-view"]')).toBeInTheDocument();
    });

    it('restores date from ?date= search param', () => {
        render(
            <MemoryRouter initialEntries={['/calendar?date=2025-06-15']}>
                <CalendarPage />
                <UrlObserver />
            </MemoryRouter>,
        );

        const expected = new Date(2025, 5, 15).toLocaleDateString(undefined, { month: 'long', year: 'numeric' });
        expect(screen.getByText(expected)).toBeInTheDocument();
    });

    it('defaults to month view when no view param is present', () => {
        render(
            <MemoryRouter initialEntries={['/calendar']}>
                <CalendarPage />
                <UrlObserver />
            </MemoryRouter>,
        );

        const monthBtn = document.querySelector('[data-view="month"]');
        expect(monthBtn).toHaveAttribute('data-active', 'true');
    });

    it('writes view change to URL when switching views', async () => {
        const user = userEvent.setup();

        render(
            <MemoryRouter initialEntries={['/calendar']}>
                <CalendarPage />
                <UrlObserver />
            </MemoryRouter>,
        );

        const weekBtn = document.querySelector('[data-view="week"]');
        await user.click(weekBtn!);

        const urlParams = screen.getByTestId('url-params');
        expect(urlParams.textContent).toContain('view=week');
    });

    it('removes view param when switching back to month (default)', async () => {
        const user = userEvent.setup();

        render(
            <MemoryRouter initialEntries={['/calendar?view=week']}>
                <CalendarPage />
                <UrlObserver />
            </MemoryRouter>,
        );

        const monthBtn = document.querySelector('[data-view="month"]');
        await user.click(monthBtn!);

        const urlParams = screen.getByTestId('url-params');
        expect(urlParams.textContent).not.toContain('view=');
    });

    it('writes date change to URL when navigating months', async () => {
        const user = userEvent.setup();

        render(
            <MemoryRouter initialEntries={['/calendar?date=2025-06-15']}>
                <CalendarPage />
                <UrlObserver />
            </MemoryRouter>,
        );

        const nextBtn = document.querySelector('[data-action="next"]');
        await user.click(nextBtn!);

        const urlParams = screen.getByTestId('url-params');
        expect(urlParams.textContent).toContain('date=2025-07-01');
    });

    it('writes view and date when clicking a day cell', async () => {
        const user = userEvent.setup();

        render(
            <MemoryRouter initialEntries={['/calendar?date=2025-06-15']}>
                <CalendarPage />
                <UrlObserver />
            </MemoryRouter>,
        );

        const dayCell = document.querySelector('[data-date="2025-06-20"]');
        await user.click(dayCell!);

        const urlParams = screen.getByTestId('url-params');
        expect(urlParams.textContent).toContain('view=day');
        expect(urlParams.textContent).toContain('date=2025-06-20');
    });

    it('falls back to defaults and normalizes the URL for invalid view param', async () => {
        render(
            <MemoryRouter initialEntries={['/calendar?view=invalid']}>
                <CalendarPage />
                <UrlObserver />
            </MemoryRouter>,
        );

        const monthBtn = document.querySelector('[data-view="month"]');
        expect(monthBtn).toHaveAttribute('data-active', 'true');
        await waitFor(() => {
            expect(screen.getByTestId('url-params').textContent).toBe('');
        });
    });

    it('falls back to today and normalizes the URL for invalid date param', async () => {
        render(
            <MemoryRouter initialEntries={['/calendar?date=not-a-date']}>
                <CalendarPage />
                <UrlObserver />
            </MemoryRouter>,
        );

        const now = new Date();
        const monthName = now.toLocaleDateString(undefined, { month: 'long', year: 'numeric' });
        expect(screen.getByText(monthName)).toBeInTheDocument();
        await waitFor(() => {
            expect(screen.getByTestId('url-params').textContent).toBe('');
        });
    });
});
