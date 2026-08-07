import { render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';

import { Badge } from './Badge';

describe('Badge', () => {
    it('renders with children', () => {
        render(<Badge>Active</Badge>);
        expect(screen.getByText('Active')).toBeInTheDocument();
    });

    it('applies default variant and md size classes', () => {
        render(<Badge>Default</Badge>);
        const badge = screen.getByText('Default');
        expect(badge).toHaveClass('badge');
        expect(badge).toHaveAttribute('data-variant', 'default');
        expect(badge).toHaveAttribute('data-size', 'md');
    });

    it('applies variant classes', () => {
        const variants = [
            'default',
            'healthy',
            'unhealthy',
            'degraded',
            'unknown',
            'stabilizing',
            'missing',
            'upgradable',
            'fulfilled',
            'fulfilledExternally',
            'primary',
            'warning',
            'info',
            'downloading',
            'queued',
            'grabbed',
            'paused',
            'stalled',
            'downloaded',
            'importing',
            'imported',
            'failed',
            'running',
            'pending',
            'completed',
            'cancelled',
        ] as const;

        variants.forEach((variant) => {
            const { unmount } = render(<Badge variant={variant}>{variant}</Badge>);
            expect(screen.getByText(variant)).toHaveAttribute('data-variant', variant);
            unmount();
        });
    });

    it('applies size classes', () => {
        const { rerender } = render(<Badge size="sm">Small</Badge>);
        expect(screen.getByText('Small')).toHaveAttribute('data-size', 'sm');

        rerender(<Badge size="md">Medium</Badge>);
        expect(screen.getByText('Medium')).toHaveAttribute('data-size', 'md');
    });

    it('renders dot when dot prop is true', () => {
        render(<Badge dot>Status</Badge>);
        const badge = screen.getByText('Status');
        const dot = badge.querySelector('.badge__dot');
        expect(dot).toBeInTheDocument();
        expect(dot).toHaveAttribute('aria-hidden', 'true');
    });

    it('does not render dot by default', () => {
        render(<Badge>Status</Badge>);
        const badge = screen.getByText('Status');
        const dot = badge.querySelector('.badge__dot');
        expect(dot).not.toBeInTheDocument();
    });

    it('forwards ref', () => {
        const ref = vi.fn();
        render(<Badge ref={ref}>Ref</Badge>);
        expect(ref).toHaveBeenCalledWith(expect.any(HTMLSpanElement));
    });

    it('applies custom className', () => {
        render(<Badge className="custom-class">Custom</Badge>);
        expect(screen.getByText('Custom')).toHaveClass('custom-class');
    });

    it('passes through additional props', () => {
        render(<Badge data-testid="my-badge">Props</Badge>);
        expect(screen.getByTestId('my-badge')).toBeInTheDocument();
    });

    it('applies outline class when badgeStyle is outline', () => {
        render(<Badge badgeStyle="outline">Outline</Badge>);
        expect(screen.getByText('Outline')).toHaveAttribute('data-style', 'outline');
    });

    it('does not apply outline class by default', () => {
        render(<Badge>Filled</Badge>);
        expect(screen.getByText('Filled')).not.toHaveAttribute('data-style', 'outline');
    });

    it('renders count when count prop is provided', () => {
        render(<Badge count={5}>Status</Badge>);
        const badge = screen.getByText('Status');
        const count = badge.querySelector('.badge__count');
        expect(count).toBeInTheDocument();
        expect(count).toHaveTextContent('5');
    });

    it('does not render when count is zero', () => {
        const { container } = render(<Badge count={0}>Status</Badge>);
        expect(container.innerHTML).toBe('');
    });

    it('renders normally when count is undefined', () => {
        render(<Badge>Status</Badge>);
        expect(screen.getByText('Status')).toBeInTheDocument();
    });

    it('does not render count by default', () => {
        render(<Badge>Status</Badge>);
        const badge = screen.getByText('Status');
        const count = badge.querySelector('.badge__count');
        expect(count).not.toBeInTheDocument();
    });

    it('renders dot, children, and count together', () => {
        render(
            <Badge dot count={3} badgeStyle="outline" variant="healthy">
                Healthy
            </Badge>,
        );
        const badge = screen.getByText('Healthy');
        expect(badge.querySelector('.badge__dot')).toBeInTheDocument();
        expect(badge.querySelector('.badge__count')).toHaveTextContent('3');
        expect(badge).toHaveAttribute('data-style', 'outline');
        expect(badge).toHaveAttribute('data-variant', 'healthy');
    });

    it('includes data-status attribute matching variant', () => {
        const { rerender } = render(<Badge variant="healthy">Healthy</Badge>);
        expect(screen.getByText('Healthy')).toHaveAttribute('data-status', 'healthy');

        rerender(<Badge variant="unhealthy">Unhealthy</Badge>);
        expect(screen.getByText('Unhealthy')).toHaveAttribute('data-status', 'unhealthy');

        rerender(<Badge>Default</Badge>);
        expect(screen.getByText('Default')).toHaveAttribute('data-status', 'default');
    });
});
