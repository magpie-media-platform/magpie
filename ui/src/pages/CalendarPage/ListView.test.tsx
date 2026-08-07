import { render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';

import type { CalendarEvent } from '@/types/calendar';

vi.mock('@/hooks/useLibraryTypes', () => ({
    useTypeIconName: () => () => 'film-slate',
}));

import { ListView } from './ListView';

function makeEvent(id: string, title: string): CalendarEvent {
    return {
        id,
        contentId: `content-${id}`,
        title,
        date: '2024-04-15T20:00:00Z',
        contentType: 'tv',
        level: 'episode',
        fulfillment: 'missing',
        monitoring: true,
        effectiveMonitoring: true,
    };
}

describe('ListView', () => {
    it('renders empty state when no events', () => {
        render(<ListView viewEvents={[]} onEventClick={vi.fn()} />);
        expect(screen.getByText('No upcoming events')).toBeInTheDocument();
    });

    it('renders event cards with showDate when events are provided', () => {
        const events = [makeEvent('1', 'Shogun'), makeEvent('2', 'Fallout')];
        render(<ListView viewEvents={events} onEventClick={vi.fn()} />);
        expect(screen.getByText('Shogun')).toBeInTheDocument();
        expect(screen.getByText('Fallout')).toBeInTheDocument();
        expect(screen.getByTestId('calendar-page__list-items')).toBeInTheDocument();
    });

    it('shows dates on event cards', () => {
        const events = [makeEvent('1', 'Shogun')];
        render(<ListView viewEvents={events} onEventClick={vi.fn()} />);
        expect(screen.getByTestId('calendar-page__event-date')).toBeInTheDocument();
    });

    it('has the correct data-component attribute', () => {
        const { container } = render(<ListView viewEvents={[]} onEventClick={vi.fn()} />);
        expect(container.querySelector('[data-component="list-view"]')).toBeInTheDocument();
    });
});
