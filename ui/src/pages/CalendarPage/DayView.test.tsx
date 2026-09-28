import { render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';

import type { CalendarEvent } from '@/types/calendar';

vi.mock('@/hooks/useLibraryTypes', () => ({
    useTypeIconName: () => () => 'film-slate',
}));

import { DayView } from './DayView';
import { TooltipProvider } from '@/components/ui/Tooltip';

function makeEvent(id: string, title: string): CalendarEvent {
    return {
        id,
        contentId: `content-${id}`,
        title,
        date: '2024-04-15T20:00:00Z',
        contentType: 'tv',
        level: 'episode',
        fulfillment: 'missing',
        monitoring: 'Monitored',
        effectiveMonitoring: 'Monitored',
    };
}

describe('DayView', () => {
    it('renders empty state when no events', () => {
        render(<DayView viewEvents={[]} onEventClick={vi.fn()} />, { wrapper: TooltipProvider });
        expect(screen.getByText('No events on this day')).toBeInTheDocument();
    });

    it('renders event cards when events are provided', () => {
        const events = [makeEvent('1', 'Shogun'), makeEvent('2', 'Fallout')];
        render(<DayView viewEvents={events} onEventClick={vi.fn()} />, { wrapper: TooltipProvider });
        expect(screen.getByText('Shogun')).toBeInTheDocument();
        expect(screen.getByText('Fallout')).toBeInTheDocument();
    });

    it('has the correct data-component attribute', () => {
        const { container } = render(<DayView viewEvents={[]} onEventClick={vi.fn()} />, { wrapper: TooltipProvider });
        expect(container.querySelector('[data-component="day-view"]')).toBeInTheDocument();
    });
});
