import { MemoryRouter } from 'react-router-dom';
import { act, fireEvent, render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, it, expect, vi } from 'vitest';

import { MobileMediaCard } from './MobileMediaCard';

function renderCard(ui: React.ReactElement) {
    return render(<MemoryRouter>{ui}</MemoryRouter>);
}

describe('MobileMediaCard', () => {
    it('renders the poster image with the supplied alt text', () => {
        renderCard(
            <MobileMediaCard posterUrl="https://example.com/p.jpg" posterAlt="The Matrix poster" title="The Matrix" />,
        );

        const img = screen.getByAltText('The Matrix poster') as HTMLImageElement;
        expect(img).toHaveAttribute('src', 'https://example.com/p.jpg');
        expect(img).toHaveAttribute('loading', 'lazy');
    });

    it('renders a broken-image placeholder when no poster url is given', () => {
        const { container } = renderCard(<MobileMediaCard posterAlt="" title="Untitled" />);

        expect(container.querySelector('.mobile-media-card__poster--placeholder')).not.toBeNull();
        expect(container.querySelector('img')).toBeNull();
    });

    it('falls back to the placeholder when the poster image fails to load', () => {
        const { container } = renderCard(
            <MobileMediaCard
                posterUrl="https://example.com/dead.jpg"
                posterAlt="The Matrix poster"
                title="The Matrix"
            />,
        );

        fireEvent.error(screen.getByAltText('The Matrix poster'));

        expect(screen.queryByAltText('The Matrix poster')).not.toBeInTheDocument();
        expect(container.querySelector('img')).toBeNull();
        expect(container.querySelector('.mobile-media-card__poster--placeholder')).not.toBeNull();
    });

    it('makes the whole card a router link when "to" is set', () => {
        renderCard(<MobileMediaCard to="/library/abc" data-content-id="abc" posterAlt="" title="Breaking Bad" />);

        const link = screen.getByText('Breaking Bad').closest('a');
        expect(link).toHaveAttribute('href', '/library/abc');
        expect(link).toHaveAttribute('data-content-id', 'abc');
    });

    it('renders a non-link card root when "to" is omitted', () => {
        const { container } = renderCard(<MobileMediaCard posterAlt="" title="A title" />);

        const card = container.querySelector('.mobile-media-card');
        expect(card?.tagName).toBe('DIV');
    });

    it('line-clamps the description so it cannot grow the card', () => {
        renderCard(<MobileMediaCard posterAlt="" title="A title" description="A long blurb." />);

        const description = screen.getByText('A long blurb.');
        expect(description).toHaveClass('line-clamp-2');
    });

    it('renders the children meta slot, the title accessory, and the trailing slot', () => {
        const { container } = renderCard(
            <MobileMediaCard
                posterAlt=""
                title="A title"
                titleAccessory={<span data-testid="accessory">eye</span>}
                trailing={<button type="button">Search Now</button>}
            >
                <span data-testid="badge">Movie</span>
            </MobileMediaCard>,
        );

        const meta = container.querySelector('.mobile-media-card__meta') as HTMLElement;
        expect(within(meta).getByTestId('badge')).toBeInTheDocument();
        expect(screen.getByTestId('accessory')).toBeInTheDocument();
        const trailing = container.querySelector('.mobile-media-card__trailing') as HTMLElement;
        expect(within(trailing).getByRole('button', { name: 'Search Now' })).toBeInTheDocument();
    });

    it('renders a selection button instead of a link when "selectable" is set', () => {
        renderCard(
            <MobileMediaCard
                to="/library/abc"
                posterAlt=""
                title="Breaking Bad"
                selectable
                selected
                selectionLabel="Select Breaking Bad card"
            />,
        );

        const button = screen.getByRole('button', { name: 'Select Breaking Bad card' });
        expect(button).toHaveAttribute('aria-pressed', 'true');
        expect(screen.queryByRole('link')).not.toBeInTheDocument();
        expect(button.querySelector('.mobile-media-card__select-indicator')).not.toBeNull();
    });

    it('toggles selection on click when selectable', async () => {
        const user = userEvent.setup();
        const onToggle = vi.fn();
        renderCard(
            <MobileMediaCard
                to="/library/abc"
                posterAlt=""
                title="Breaking Bad"
                selectable
                onToggleSelected={onToggle}
                selectionLabel="Select Breaking Bad card"
            />,
        );

        await user.click(screen.getByRole('button', { name: 'Select Breaking Bad card' }));
        expect(onToggle).toHaveBeenCalledTimes(1);
    });

    it('fires onLongPress after a sustained press on the link card', () => {
        vi.useFakeTimers();
        try {
            const onLongPress = vi.fn();
            renderCard(
                <MobileMediaCard to="/library/abc" posterAlt="" title="Breaking Bad" onLongPress={onLongPress} />,
            );

            const link = screen.getByText('Breaking Bad').closest('a') as HTMLElement;
            fireEvent.pointerDown(link, { clientX: 0, clientY: 0 });
            act(() => {
                vi.advanceTimersByTime(500);
            });

            expect(onLongPress).toHaveBeenCalledTimes(1);
        } finally {
            vi.useRealTimers();
        }
    });

    it('does not fire onLongPress when the press is released early', () => {
        vi.useFakeTimers();
        try {
            const onLongPress = vi.fn();
            renderCard(
                <MobileMediaCard to="/library/abc" posterAlt="" title="Breaking Bad" onLongPress={onLongPress} />,
            );

            const link = screen.getByText('Breaking Bad').closest('a') as HTMLElement;
            fireEvent.pointerDown(link, { clientX: 0, clientY: 0 });
            act(() => {
                vi.advanceTimersByTime(200);
            });
            fireEvent.pointerUp(link);
            act(() => {
                vi.advanceTimersByTime(400);
            });

            expect(onLongPress).not.toHaveBeenCalled();
        } finally {
            vi.useRealTimers();
        }
    });

    it('wraps the poster in a router link when "posterTo" is set', () => {
        renderCard(
            <MobileMediaCard
                posterUrl="https://example.com/p.jpg"
                posterAlt=""
                posterTo="/library/xyz"
                posterLinkLabel="View in library"
                title="A title"
            />,
        );

        const posterLink = screen.getByRole('link', { name: 'View in library' });
        expect(posterLink).toHaveAttribute('href', '/library/xyz');
        expect(posterLink.querySelector('img')).not.toBeNull();
    });
});
