import { render, screen, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { MemoryRouter } from 'react-router-dom';

import type { CalendarEvent } from '@/types/calendar';

import { CalendarPage } from './CalendarPage';
import { getMonthDays, getWeekStart } from './calendarUtils';
import { TooltipProvider } from '@/components/ui/Tooltip';

const mockUseExtensions = vi.fn();
const mockUseSlotExtensions = vi.fn();

vi.mock('@/extensions/useExtensions', () => ({
    useExtensions: () => mockUseExtensions(),
    useSlotExtensions: (slot: string) => mockUseSlotExtensions(slot),
}));

vi.mock('@/hooks/useLibraryTypes', () => ({
    useTypeIconName: () => () => 'film-slate',
}));

const mockUseCalendar = vi.fn();
let mockEvents: CalendarEvent[] = [];
vi.mock('@/hooks/useCalendar', () => ({
    useCalendar: (params: unknown) => {
        mockUseCalendar(params);
        return {
            events: mockEvents,
            isLoading: false,
            isFetching: false,
            error: null,
            refetch: vi.fn(),
        };
    },
}));

interface FetchRange {
    startDate: string;
    endDate: string;
}

function lastFetchRange(): FetchRange {
    const calls = mockUseCalendar.mock.calls;
    return calls[calls.length - 1]?.[0] as FetchRange;
}

function localYmd(date: Date): string {
    const y = date.getFullYear();
    const m = String(date.getMonth() + 1).padStart(2, '0');
    const d = String(date.getDate()).padStart(2, '0');
    return `${y}-${m}-${d}`;
}

const timezones = [
    { label: 'positive offset (Asia/Tokyo, UTC+9)', tz: 'Asia/Tokyo' },
    { label: 'negative offset (America/New_York, UTC-4/-5)', tz: 'America/New_York' },
];

describe('CalendarPage fetch range timezone handling', () => {
    beforeEach(() => {
        vi.clearAllMocks();
        mockEvents = [];
        mockUseExtensions.mockReturnValue({
            apiBaseUrl: '/api',
            userId: 'user-123',
            username: 'testuser',
            role: 'Admin',
        });
        mockUseSlotExtensions.mockReturnValue({ extensions: [], isReady: true, isLoading: false });
    });

    afterEach(() => {
        vi.unstubAllEnvs();
    });

    for (const { label, tz } of timezones) {
        describe(label, () => {
            beforeEach(() => {
                vi.stubEnv('TZ', tz);
            });

            it('day view fetches the locally selected date, not the UTC-shifted one', () => {
                render(
                    <MemoryRouter initialEntries={['/calendar?view=day&date=2025-06-15']}>
                        <CalendarPage />
                    </MemoryRouter>,
                    { wrapper: TooltipProvider },
                );

                expect(lastFetchRange()).toEqual({ startDate: '2025-06-15', endDate: '2025-06-15' });
            });

            it('month view fetches the local first and last grid days', () => {
                render(
                    <MemoryRouter initialEntries={['/calendar?view=month&date=2025-06-15']}>
                        <CalendarPage />
                    </MemoryRouter>,
                    { wrapper: TooltipProvider },
                );

                const monthDays = getMonthDays(2025, 5);
                const range = lastFetchRange();
                expect(range.startDate).toBe(localYmd(monthDays[0]));
                expect(range.endDate).toBe(localYmd(monthDays[monthDays.length - 1]));
            });

            it('week view fetches the local week start and end', () => {
                render(
                    <MemoryRouter initialEntries={['/calendar?view=week&date=2025-06-15']}>
                        <CalendarPage />
                    </MemoryRouter>,
                    { wrapper: TooltipProvider },
                );

                const weekStart = getWeekStart(new Date(2025, 5, 15));
                const weekEnd = new Date(weekStart);
                weekEnd.setDate(weekStart.getDate() + 6);
                const range = lastFetchRange();
                expect(range.startDate).toBe(localYmd(weekStart));
                expect(range.endDate).toBe(localYmd(weekEnd));
            });
        });
    }

    it('keeps last-grid-day events in range under a positive UTC offset', () => {
        vi.stubEnv('TZ', 'Asia/Tokyo');

        render(
            <MemoryRouter initialEntries={['/calendar?view=month&date=2025-06-15']}>
                <CalendarPage />
            </MemoryRouter>,
            { wrapper: TooltipProvider },
        );

        const range = lastFetchRange();
        expect(range.endDate).toBe('2025-07-05');
        expect(range.endDate).not.toBe('2025-07-04');
    });

    describe('date-only publication events', () => {
        beforeEach(() => {
            vi.stubEnv('TZ', 'America/Los_Angeles');
            mockEvents = [
                {
                    id: 'evt-july-20',
                    contentId: 'content-july-20',
                    title: 'July 20 release',
                    date: '2026-07-20T00:00:00Z',
                    contentType: 'movies',
                    level: 'movie',
                    fulfillment: 'missing',
                    monitoring: 'Monitored',
                    effectiveMonitoring: 'Monitored',
                },
            ];
        });

        it('places the event in the July 20 month cell', () => {
            render(
                <MemoryRouter initialEntries={['/calendar?view=month&date=2026-07-20']}>
                    <CalendarPage />
                </MemoryRouter>,
                { wrapper: TooltipProvider },
            );

            const july20 = document.querySelector<HTMLElement>(
                '[data-component="month-view"] [data-date="2026-07-20"]',
            );
            const july19 = document.querySelector<HTMLElement>(
                '[data-component="month-view"] [data-date="2026-07-19"]',
            );
            expect(july20).not.toBeNull();
            expect(july19).not.toBeNull();
            expect(within(july20!).getByText('July 20 release')).toBeInTheDocument();
            expect(within(july19!).queryByText('July 20 release')).not.toBeInTheDocument();
        });

        it('places the event in the July 20 week column', () => {
            render(
                <MemoryRouter initialEntries={['/calendar?view=week&date=2026-07-20']}>
                    <CalendarPage />
                </MemoryRouter>,
                { wrapper: TooltipProvider },
            );

            const july20 = document.querySelector<HTMLElement>('[data-component="week-view"] [data-date="2026-07-20"]');
            const july19 = document.querySelector<HTMLElement>('[data-component="week-view"] [data-date="2026-07-19"]');
            expect(july20).not.toBeNull();
            expect(july19).not.toBeNull();
            expect(within(july20!).getByText('July 20 release')).toBeInTheDocument();
            expect(within(july19!).queryByText('July 20 release')).not.toBeInTheDocument();
        });

        it('keeps the event in the July 20 day view', () => {
            render(
                <MemoryRouter initialEntries={['/calendar?view=day&date=2026-07-20']}>
                    <CalendarPage />
                </MemoryRouter>,
                { wrapper: TooltipProvider },
            );

            expect(screen.getByText('July 20 release')).toBeInTheDocument();
            expect(screen.queryByText('No events on this day')).not.toBeInTheDocument();
        });

        it('labels the event as July 20 in list view', () => {
            const today = new Date();
            today.setHours(0, 0, 0, 0);
            const releaseDate = new Date(today);
            releaseDate.setDate(today.getDate() + 1);
            const releaseDateKey = localYmd(releaseDate);
            mockEvents = [{ ...mockEvents[0], date: `${releaseDateKey}T00:00:00Z` }];

            render(
                <MemoryRouter initialEntries={['/calendar?view=list']}>
                    <CalendarPage />
                </MemoryRouter>,
                { wrapper: TooltipProvider },
            );

            const expectedLabel = releaseDate.toLocaleDateString(undefined, {
                weekday: 'short',
                month: 'short',
                day: 'numeric',
            });
            expect(screen.getByTestId('calendar-page__event-date')).toHaveTextContent(expectedLabel);
        });
    });
});
