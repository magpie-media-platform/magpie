import { act, fireEvent, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';

import {
    ContentFulfillmentBadge,
    ContentMonitoringIndicator,
    ContentPoster,
    monitoringStateLabel,
} from './ContentCardPrimitives';
import { TooltipProvider } from '@/components/ui/Tooltip';

describe('ContentPoster', () => {
    it('shows a placeholder on failure and retries a replacement URL', () => {
        // Arrange
        const { container, rerender } = render(
            <ContentPoster
                posterUrl="/broken.jpg"
                title="Sample"
                className="poster"
                placeholderClassName="placeholder"
            />,
            { wrapper: TooltipProvider },
        );

        // Act
        fireEvent.error(screen.getByRole('img', { name: 'Sample artwork' }));

        // Assert
        expect(screen.queryByRole('img')).not.toBeInTheDocument();
        expect(container.querySelector('.placeholder')).toHaveAttribute('aria-hidden', 'true');
        rerender(
            <ContentPoster
                posterUrl="/replacement.jpg"
                title="Sample"
                className="poster"
                placeholderClassName="placeholder"
            />,
        );
        expect(screen.getByRole('img', { name: 'Sample artwork' })).toHaveAttribute('src', '/replacement.jpg');
        expect(container.querySelector('.placeholder')).not.toBeInTheDocument();
    });

    it('keeps the site placeholder when no URL was supplied and shows the failure glyph only on error', () => {
        // Arrange
        const empty = <span data-testid="site-empty" />;
        const { container, rerender } = render(
            <ContentPoster
                posterUrl={null}
                title="Sample"
                className="poster"
                placeholderClassName="placeholder"
                placeholder={empty}
            />,
            { wrapper: TooltipProvider },
        );

        // Assert
        expect(screen.getByTestId('site-empty')).toBeInTheDocument();
        expect(container.querySelector('.placeholder svg')).not.toBeInTheDocument();

        // Act
        rerender(
            <ContentPoster
                posterUrl="/broken.jpg"
                title="Sample"
                className="poster"
                placeholderClassName="placeholder"
                placeholder={empty}
            />,
        );
        fireEvent.error(screen.getByRole('img', { name: 'Sample artwork' }));

        // Assert
        expect(screen.queryByTestId('site-empty')).not.toBeInTheDocument();
        expect(container.querySelector('.placeholder svg')).toBeInTheDocument();
    });
});

describe('ContentMonitoringIndicator', () => {
    it('shows inherited monitoring as distinct text when a text label is requested', () => {
        // Arrange
        // Act
        render(
            <ContentMonitoringIndicator
                monitoring={null}
                effectiveMonitoring="Unmonitored"
                className="monitoring"
                labelClassName="monitoring-label"
            />,
            { wrapper: TooltipProvider },
        );

        // Assert
        expect(screen.getByText('Unmonitored (inherited)')).toBeInTheDocument();
        expect(screen.getByLabelText('Unmonitored (inherited)')).toBeInTheDocument();
    });
});

describe('monitoringStateLabel', () => {
    it.each([
        ['Monitored', 'Monitored', 'Monitored'],
        [null, 'Monitored', 'Monitored (inherited)'],
        ['Unmonitored', 'Unmonitored', 'Unmonitored'],
        [null, 'Unmonitored', 'Unmonitored (inherited)'],
        ['Shotgun', 'Shotgun', 'Shotgun'],
        [null, 'Shotgun', 'Shotgun (inherited)'],
    ] as const)('names monitoring %s, effective %s as %s', (monitoring, effective, expected) => {
        // Arrange
        // Act
        const label = monitoringStateLabel(monitoring, effective);

        // Assert
        expect(label).toBe(expected);
    });
});

describe('ContentMonitoringIndicator icon', () => {
    it('shows only an icon whose accessible name is the full state', () => {
        // Arrange
        // Act
        render(<ContentMonitoringIndicator monitoring={null} effectiveMonitoring="Shotgun" className="monitoring" />, {
            wrapper: TooltipProvider,
        });

        // Assert
        const icon = screen.getByRole('img', { name: 'Shotgun (inherited)' });
        expect(icon).toHaveAttribute('data-shotgun', 'true');
        expect(icon).not.toHaveTextContent('Shotgun');
    });

    it('shows the full state in a tooltip on hover', async () => {
        // Arrange
        const user = userEvent.setup();
        render(
            <ContentMonitoringIndicator
                monitoring="Unmonitored"
                effectiveMonitoring="Unmonitored"
                className="monitoring"
            />,
            {
                wrapper: TooltipProvider,
            },
        );

        // Act
        await user.hover(screen.getByRole('img', { name: 'Unmonitored' }));

        // Assert
        expect(await screen.findByRole('tooltip')).toHaveTextContent('Unmonitored');
    });

    it('shows the full state in a tooltip on a touch long press without a click reaching the card', () => {
        // Arrange
        vi.useFakeTimers();
        const onCardClick = vi.fn();
        render(
            <a href="#card" onClick={onCardClick}>
                <ContentMonitoringIndicator monitoring={null} effectiveMonitoring="Monitored" className="monitoring" />
            </a>,
            { wrapper: TooltipProvider },
        );
        const icon = screen.getByRole('img', { name: 'Monitored (inherited)' });

        // Act
        fireEvent.pointerDown(icon, { pointerType: 'touch', clientX: 5, clientY: 5 });
        act(() => {
            vi.advanceTimersByTime(500);
        });
        fireEvent.pointerUp(icon, { pointerType: 'touch' });
        fireEvent.click(icon);
        vi.useRealTimers();

        // Assert
        expect(screen.getByRole('tooltip')).toHaveTextContent('Monitored (inherited)');
        expect(onCardClick).not.toHaveBeenCalled();
    });
});

describe('ContentFulfillmentBadge', () => {
    it.each([
        ['missing', 'Missing'],
        ['partial', 'Partial'],
        ['upgradable', 'Upgradable'],
        ['fulfilled', 'Fulfilled'],
        ['fulfilledExternally', 'External'],
    ] as const)('renders %s as %s without a monitoring prerequisite', (fulfillment, label) => {
        // Arrange
        // Act
        render(<ContentFulfillmentBadge fulfillment={fulfillment} />, { wrapper: TooltipProvider });

        // Assert
        expect(screen.getByText(label)).toBeInTheDocument();
    });
});

describe('ContentFulfillmentBadge import pending', () => {
    it('labels a missing item that waits for a discovered file as import pending', () => {
        render(<ContentFulfillmentBadge fulfillment="missing" importPending />, { wrapper: TooltipProvider });

        expect(screen.getByText('Import pending')).toBeInTheDocument();
        expect(screen.queryByText('Missing')).toBeNull();
    });
});
