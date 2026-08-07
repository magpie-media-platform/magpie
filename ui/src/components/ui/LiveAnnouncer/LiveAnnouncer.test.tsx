import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';

import { act, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';

import { LiveAnnouncerProvider, useLiveAnnouncer } from './index';

function TestAnnouncer() {
    const { announce } = useLiveAnnouncer();

    return (
        <div>
            <button data-testid="polite-btn" onClick={() => announce('Polite message')}>
                Announce Polite
            </button>
            <button data-testid="assertive-btn" onClick={() => announce('Assertive message', 'assertive')}>
                Announce Assertive
            </button>
            <button data-testid="empty-btn" onClick={() => announce('   ')}>
                Announce Empty
            </button>
        </div>
    );
}

describe('LiveAnnouncer', () => {
    it('renders aria-live regions', () => {
        render(
            <LiveAnnouncerProvider>
                <div>Content</div>
            </LiveAnnouncerProvider>,
        );

        const politeRegion = document.querySelector('[data-politeness="polite"]');
        const assertiveRegion = document.querySelector('[data-politeness="assertive"]');

        expect(politeRegion).toBeInTheDocument();
        expect(assertiveRegion).toBeInTheDocument();
    });

    it('polite region has correct aria attributes', () => {
        render(
            <LiveAnnouncerProvider>
                <div>Content</div>
            </LiveAnnouncerProvider>,
        );

        const politeRegion = document.querySelector('[data-politeness="polite"]');

        expect(politeRegion).toHaveAttribute('role', 'status');
        expect(politeRegion).toHaveAttribute('aria-live', 'polite');
        expect(politeRegion).toHaveAttribute('aria-atomic', 'true');
    });

    it('assertive region has correct aria attributes', () => {
        render(
            <LiveAnnouncerProvider>
                <div>Content</div>
            </LiveAnnouncerProvider>,
        );

        const assertiveRegion = document.querySelector('[data-politeness="assertive"]');

        expect(assertiveRegion).toHaveAttribute('role', 'alert');
        expect(assertiveRegion).toHaveAttribute('aria-live', 'assertive');
        expect(assertiveRegion).toHaveAttribute('aria-atomic', 'true');
    });

    it('announces polite message', async () => {
        render(
            <LiveAnnouncerProvider>
                <TestAnnouncer />
            </LiveAnnouncerProvider>,
        );

        await act(async () => {
            screen.getByTestId('polite-btn').click();
        });

        const politeRegion = document.querySelector('[data-politeness="polite"]');
        expect(politeRegion).toHaveTextContent('Polite message');
    });

    it('announces assertive message', async () => {
        render(
            <LiveAnnouncerProvider>
                <TestAnnouncer />
            </LiveAnnouncerProvider>,
        );

        await act(async () => {
            screen.getByTestId('assertive-btn').click();
        });

        const assertiveRegion = document.querySelector('[data-politeness="assertive"]');
        expect(assertiveRegion).toHaveTextContent('Assertive message');
    });

    it('clears announcement after timeout', async () => {
        vi.useFakeTimers();

        render(
            <LiveAnnouncerProvider>
                <TestAnnouncer />
            </LiveAnnouncerProvider>,
        );

        await act(async () => {
            screen.getByTestId('polite-btn').click();
        });

        const politeRegion = document.querySelector('[data-politeness="polite"]');
        expect(politeRegion).toHaveTextContent('Polite message');

        await act(async () => {
            vi.advanceTimersByTime(7100);
        });

        expect(politeRegion).not.toHaveTextContent('Polite message');

        vi.useRealTimers();
    });

    it('ignores empty messages', async () => {
        render(
            <LiveAnnouncerProvider>
                <TestAnnouncer />
            </LiveAnnouncerProvider>,
        );

        await act(async () => {
            screen.getByTestId('empty-btn').click();
        });

        const politeRegion = document.querySelector('[data-politeness="polite"]');
        expect(politeRegion).toHaveTextContent('');
    });

    it('queues multiple announcements', async () => {
        render(
            <LiveAnnouncerProvider>
                <TestAnnouncer />
            </LiveAnnouncerProvider>,
        );

        await act(async () => {
            screen.getByTestId('polite-btn').click();
        });

        await act(async () => {
            screen.getByTestId('polite-btn').click();
        });

        const politeRegion = document.querySelector('[data-politeness="polite"]');

        expect(politeRegion?.querySelectorAll('span').length).toBe(2);
    });

    it('renders children', () => {
        render(
            <LiveAnnouncerProvider>
                <div data-testid="child">Child content</div>
            </LiveAnnouncerProvider>,
        );

        expect(screen.getByTestId('child')).toBeInTheDocument();
    });

    it('has data-component attribute', () => {
        render(
            <LiveAnnouncerProvider>
                <div>Content</div>
            </LiveAnnouncerProvider>,
        );

        const regions = document.querySelectorAll('[data-component="live-announcer"]');
        expect(regions.length).toBe(2);
    });

    it('has live-announcer class for styling', () => {
        render(
            <LiveAnnouncerProvider>
                <div>Content</div>
            </LiveAnnouncerProvider>,
        );

        const regions = document.querySelectorAll('.live-announcer');
        expect(regions.length).toBe(2);
    });

    it('anchors hidden regions to the viewport origin so they cannot extend the document', () => {
        render(
            <LiveAnnouncerProvider>
                <div>Content</div>
            </LiveAnnouncerProvider>,
        );

        const regions = document.querySelectorAll('.live-announcer');
        expect(regions.length).toBe(2);

        const css = readFileSync(resolve(process.cwd(), 'src/components/ui/LiveAnnouncer/LiveAnnouncer.css'), 'utf8');
        expect(css).toContain('position: fixed;');
        expect(css).toContain('inset: 0 auto auto 0;');
        expect(css).toContain('overflow: hidden;');
    });
});

describe('useLiveAnnouncer', () => {
    it('throws error when used outside provider', () => {
        const consoleSpy = vi.spyOn(console, 'error').mockImplementation(() => {});

        function ThrowingComponent() {
            useLiveAnnouncer();
            return null;
        }

        expect(() => render(<ThrowingComponent />)).toThrow(
            'useLiveAnnouncer must be used within a LiveAnnouncerProvider',
        );

        consoleSpy.mockRestore();
    });
});
