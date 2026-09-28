import { createRef } from 'react';
import { render, screen, fireEvent, waitFor, act } from '@testing-library/react';
import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';

import { PullToRefresh, type PullToRefreshRef } from './PullToRefresh';

describe('PullToRefresh', () => {
    const mockOnRefresh = vi.fn().mockResolvedValue(undefined);

    beforeEach(() => {
        vi.clearAllMocks();
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('renders children', () => {
        render(
            <PullToRefresh onRefresh={mockOnRefresh}>
                <div>List content</div>
            </PullToRefresh>,
        );
        expect(screen.getByText('List content')).toBeInTheDocument();
    });

    it('applies default className', () => {
        const { container } = render(
            <PullToRefresh onRefresh={mockOnRefresh}>
                <div>Content</div>
            </PullToRefresh>,
        );
        expect(container.firstChild).toHaveClass('pull-to-refresh');
    });

    it('applies custom className', () => {
        const { container } = render(
            <PullToRefresh onRefresh={mockOnRefresh} className="custom-class">
                <div>Content</div>
            </PullToRefresh>,
        );
        expect(container.firstChild).toHaveClass('custom-class');
    });

    it('applies idle state class by default', () => {
        const { container } = render(
            <PullToRefresh onRefresh={mockOnRefresh}>
                <div>Content</div>
            </PullToRefresh>,
        );
        expect(container.firstChild).toHaveAttribute('data-state', 'idle');
    });

    it('applies disabled class when disabled', () => {
        const { container } = render(
            <PullToRefresh onRefresh={mockOnRefresh} disabled>
                <div>Content</div>
            </PullToRefresh>,
        );
        expect(container.firstChild).toHaveAttribute('data-disabled', 'true');
    });

    it('shows default pull label', () => {
        render(
            <PullToRefresh onRefresh={mockOnRefresh}>
                <div>Content</div>
            </PullToRefresh>,
        );
        expect(screen.getByText('Pull to refresh')).toBeInTheDocument();
    });

    it('shows custom pull label', () => {
        render(
            <PullToRefresh onRefresh={mockOnRefresh} pullLabel="Swipe down">
                <div>Content</div>
            </PullToRefresh>,
        );
        expect(screen.getByText('Swipe down')).toBeInTheDocument();
    });

    it('renders custom indicator when provided', () => {
        render(
            <PullToRefresh onRefresh={mockOnRefresh} indicator={<span data-testid="custom-indicator">Custom</span>}>
                <div>Content</div>
            </PullToRefresh>,
        );
        expect(screen.getByTestId('custom-indicator')).toBeInTheDocument();
    });

    it('exposes state via ref', () => {
        const ref = createRef<PullToRefreshRef>();
        render(
            <PullToRefresh ref={ref} onRefresh={mockOnRefresh}>
                <div>Content</div>
            </PullToRefresh>,
        );
        expect(ref.current?.state).toBe('idle');
    });

    it('exposes refresh function via ref', async () => {
        const ref = createRef<PullToRefreshRef>();
        render(
            <PullToRefresh ref={ref} onRefresh={mockOnRefresh}>
                <div>Content</div>
            </PullToRefresh>,
        );

        await act(async () => {
            await ref.current?.refresh();
        });

        expect(mockOnRefresh).toHaveBeenCalledTimes(1);
    });

    it('does not call onRefresh when disabled and refresh called via ref', async () => {
        const ref = createRef<PullToRefreshRef>();
        render(
            <PullToRefresh ref={ref} onRefresh={mockOnRefresh} disabled>
                <div>Content</div>
            </PullToRefresh>,
        );

        await act(async () => {
            await ref.current?.refresh();
        });

        expect(mockOnRefresh).not.toHaveBeenCalled();
    });

    it('passes additional props to container', () => {
        const { container } = render(
            <PullToRefresh onRefresh={mockOnRefresh} data-testid="ptr-container">
                <div>Content</div>
            </PullToRefresh>,
        );
        expect(container.firstChild).toHaveAttribute('data-testid', 'ptr-container');
    });

    describe('touch interactions', () => {
        const createTouchEvent = (clientY: number) => ({
            touches: [{ clientY }],
        });

        it('ignores touch when disabled', () => {
            const { container } = render(
                <PullToRefresh onRefresh={mockOnRefresh} disabled>
                    <div>Content</div>
                </PullToRefresh>,
            );

            const content = container.querySelector('.pull-to-refresh__content');
            expect(content).toBeInTheDocument();

            fireEvent.touchStart(content!, createTouchEvent(0));
            fireEvent.touchMove(content!, createTouchEvent(100));
            fireEvent.touchEnd(content!);

            expect(container.firstChild).toHaveAttribute('data-state', 'idle');
        });

        it('tracks pulling state on touch move', () => {
            const { container } = render(
                <PullToRefresh onRefresh={mockOnRefresh}>
                    <div>Content</div>
                </PullToRefresh>,
            );

            const content = container.querySelector('.pull-to-refresh__content');
            Object.defineProperty(content, 'scrollTop', { value: 0, writable: true });

            fireEvent.touchStart(content!, createTouchEvent(0));
            fireEvent.touchMove(content!, createTouchEvent(50));

            expect(container.firstChild).toHaveAttribute('data-state', 'pulling');
        });

        it('shows ready state when threshold is reached', () => {
            const { container } = render(
                <PullToRefresh onRefresh={mockOnRefresh} threshold={40}>
                    <div>Content</div>
                </PullToRefresh>,
            );

            const content = container.querySelector('.pull-to-refresh__content');
            Object.defineProperty(content, 'scrollTop', { value: 0, writable: true });

            fireEvent.touchStart(content!, createTouchEvent(0));
            fireEvent.touchMove(content!, createTouchEvent(100));

            expect(container.firstChild).toHaveAttribute('data-state', 'ready');
        });

        it('triggers refresh on touch end when ready', async () => {
            const { container } = render(
                <PullToRefresh onRefresh={mockOnRefresh} threshold={40}>
                    <div>Content</div>
                </PullToRefresh>,
            );

            const content = container.querySelector('.pull-to-refresh__content');
            Object.defineProperty(content, 'scrollTop', { value: 0, writable: true });

            fireEvent.touchStart(content!, createTouchEvent(0));
            fireEvent.touchMove(content!, createTouchEvent(100));
            fireEvent.touchEnd(content!);

            await waitFor(() => {
                expect(mockOnRefresh).toHaveBeenCalledTimes(1);
            });
        });

        it('does not trigger refresh on touch end when not ready', async () => {
            const { container } = render(
                <PullToRefresh onRefresh={mockOnRefresh} threshold={80}>
                    <div>Content</div>
                </PullToRefresh>,
            );

            const content = container.querySelector('.pull-to-refresh__content');
            Object.defineProperty(content, 'scrollTop', { value: 0, writable: true });

            fireEvent.touchStart(content!, createTouchEvent(0));
            fireEvent.touchMove(content!, createTouchEvent(50));
            fireEvent.touchEnd(content!);

            expect(mockOnRefresh).not.toHaveBeenCalled();
            expect(container.firstChild).toHaveAttribute('data-state', 'idle');
        });

        it('resets an armed pull when the touch is cancelled', () => {
            const { container } = render(
                <PullToRefresh onRefresh={mockOnRefresh} threshold={40}>
                    <div>Content</div>
                </PullToRefresh>,
            );

            const content = container.querySelector('.pull-to-refresh__content');
            Object.defineProperty(content, 'scrollTop', { value: 0, writable: true });

            fireEvent.touchStart(content!, createTouchEvent(0));
            fireEvent.touchMove(content!, createTouchEvent(100));
            expect(container.firstChild).toHaveAttribute('data-state', 'ready');

            fireEvent.touchCancel(content!);
            fireEvent.touchEnd(content!);

            expect(container.firstChild).toHaveAttribute('data-state', 'idle');
            expect(mockOnRefresh).not.toHaveBeenCalled();
        });

        it('ignores upward swipes', () => {
            const { container } = render(
                <PullToRefresh onRefresh={mockOnRefresh}>
                    <div>Content</div>
                </PullToRefresh>,
            );

            const content = container.querySelector('.pull-to-refresh__content');
            Object.defineProperty(content, 'scrollTop', { value: 0, writable: true });

            fireEvent.touchStart(content!, createTouchEvent(100));
            fireEvent.touchMove(content!, createTouchEvent(50));

            expect(container.firstChild).toHaveAttribute('data-state', 'idle');
        });

        it('ignores touch when not at scroll top', () => {
            const { container } = render(
                <PullToRefresh onRefresh={mockOnRefresh}>
                    <div>Content</div>
                </PullToRefresh>,
            );

            const content = container.querySelector('.pull-to-refresh__content');
            Object.defineProperty(content, 'scrollTop', { value: 100, writable: true });

            fireEvent.touchStart(content!, createTouchEvent(0));
            fireEvent.touchMove(content!, createTouchEvent(100));

            expect(container.firstChild).toHaveAttribute('data-state', 'idle');
        });

        it('uses the full-width page wrapper as the desktop scroll owner', () => {
            const { container } = render(
                <main>
                    <div data-component="app-shell-main-wrapper">
                        <PullToRefresh onRefresh={mockOnRefresh}>
                            <div>Content</div>
                        </PullToRefresh>
                    </div>
                </main>,
            );

            const scrollContainer = container.querySelector<HTMLElement>('[data-component="app-shell-main-wrapper"]');
            const content = container.querySelector('.pull-to-refresh__content');
            Object.defineProperty(scrollContainer, 'scrollTop', { value: 100, writable: true });

            fireEvent.touchStart(content!, createTouchEvent(0));
            fireEvent.touchMove(content!, createTouchEvent(100));

            expect(container.querySelector('.pull-to-refresh')).toHaveAttribute('data-state', 'idle');
        });

        it('uses the full-width main area as the mobile scroll owner', () => {
            vi.spyOn(window, 'innerWidth', 'get').mockReturnValue(390);

            const { container } = render(
                <main data-scroll-container="">
                    <PullToRefresh onRefresh={mockOnRefresh}>
                        <div>Content</div>
                    </PullToRefresh>
                </main>,
            );

            const scrollContainer = container.querySelector<HTMLElement>('[data-scroll-container]');
            const content = container.querySelector('.pull-to-refresh__content');
            Object.defineProperty(scrollContainer, 'scrollTop', { value: 100, writable: true });

            fireEvent.touchStart(content!, createTouchEvent(0));
            fireEvent.touchMove(content!, createTouchEvent(100));

            expect(container.querySelector('.pull-to-refresh')).toHaveAttribute('data-state', 'idle');
        });

        it('uses the main scroll owner at tablet width', () => {
            // Arrange
            vi.spyOn(window, 'innerWidth', 'get').mockReturnValue(900);
            const { container } = render(
                <main data-scroll-container="">
                    <div data-component="app-shell-main-wrapper">
                        <PullToRefresh onRefresh={mockOnRefresh}>
                            <div>Content</div>
                        </PullToRefresh>
                    </div>
                </main>,
            );
            const main = container.querySelector<HTMLElement>('[data-scroll-container]');
            const content = container.querySelector('.pull-to-refresh__content');
            Object.defineProperty(main, 'scrollTop', { value: 100, writable: true });

            // Act
            fireEvent.touchStart(content!, createTouchEvent(0));
            fireEvent.touchMove(content!, createTouchEvent(100));

            // Assert
            expect(container.querySelector('.pull-to-refresh')).toHaveAttribute('data-state', 'idle');
            expect(mockOnRefresh).not.toHaveBeenCalled();
        });

        it('shows release label when ready', () => {
            const { container } = render(
                <PullToRefresh onRefresh={mockOnRefresh} threshold={40} releaseLabel="Let go!">
                    <div>Content</div>
                </PullToRefresh>,
            );

            const content = container.querySelector('.pull-to-refresh__content');
            Object.defineProperty(content, 'scrollTop', { value: 0, writable: true });

            fireEvent.touchStart(content!, createTouchEvent(0));
            fireEvent.touchMove(content!, createTouchEvent(100));

            expect(screen.getByText('Let go!')).toBeInTheDocument();
        });

        it('shows refreshing label during refresh', async () => {
            let resolveRefresh: () => void;
            const slowRefresh = new Promise<void>((resolve) => {
                resolveRefresh = resolve;
            });

            const { container } = render(
                <PullToRefresh onRefresh={() => slowRefresh} threshold={40} refreshingLabel="Loading data...">
                    <div>Content</div>
                </PullToRefresh>,
            );

            const content = container.querySelector('.pull-to-refresh__content');
            Object.defineProperty(content, 'scrollTop', { value: 0, writable: true });

            fireEvent.touchStart(content!, createTouchEvent(0));
            fireEvent.touchMove(content!, createTouchEvent(100));
            fireEvent.touchEnd(content!);

            await waitFor(() => {
                expect(screen.getByText('Loading data...')).toBeInTheDocument();
            });

            await act(async () => {
                resolveRefresh!();
            });
        });
    });

    describe('accessibility', () => {
        it('sets aria-live on indicator', () => {
            const { container } = render(
                <PullToRefresh onRefresh={mockOnRefresh}>
                    <div>Content</div>
                </PullToRefresh>,
            );

            const indicator = container.querySelector('.pull-to-refresh__indicator');
            expect(indicator).toHaveAttribute('aria-live', 'polite');
        });

        it('sets aria-busy during refresh', async () => {
            const ref = createRef<PullToRefreshRef>();
            let resolveRefresh: () => void;
            const slowRefresh = new Promise<void>((resolve) => {
                resolveRefresh = resolve;
            });

            const { container } = render(
                <PullToRefresh ref={ref} onRefresh={() => slowRefresh}>
                    <div>Content</div>
                </PullToRefresh>,
            );

            act(() => {
                void ref.current?.refresh();
            });

            await waitFor(() => {
                const indicator = container.querySelector('.pull-to-refresh__indicator');
                expect(indicator).toHaveAttribute('aria-busy', 'true');
            });

            await act(async () => {
                resolveRefresh!();
            });
        });
    });
});
