import { render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';

import { Skeleton } from './Skeleton';

describe('Skeleton', () => {
    it('renders with default text variant', () => {
        render(<Skeleton />);
        const skeleton = screen.getByRole('status');
        expect(skeleton).toBeInTheDocument();
        expect(skeleton).toHaveClass('skeleton');
        expect(skeleton).toHaveAttribute('data-variant', 'text');
    });

    it('applies variant classes', () => {
        const variants = ['text', 'circle', 'rect'] as const;

        variants.forEach((variant) => {
            const { unmount } = render(<Skeleton variant={variant} data-testid={variant} />);
            expect(screen.getByTestId(variant)).toHaveAttribute('data-variant', variant);
            unmount();
        });
    });

    it('applies width as number (pixels)', () => {
        render(<Skeleton width={100} />);
        const skeleton = screen.getByRole('status');
        expect(skeleton).toHaveStyle({ width: '100px' });
    });

    it('applies width as string', () => {
        render(<Skeleton width="50%" />);
        const skeleton = screen.getByRole('status');
        expect(skeleton).toHaveStyle({ width: '50%' });
    });

    it('applies height as number (pixels)', () => {
        render(<Skeleton height={24} />);
        const skeleton = screen.getByRole('status');
        expect(skeleton).toHaveStyle({ height: '24px' });
    });

    it('applies height as string', () => {
        render(<Skeleton height="2rem" />);
        const skeleton = screen.getByRole('status');
        expect(skeleton).toHaveStyle({ height: '2rem' });
    });

    it('applies both width and height', () => {
        render(<Skeleton width={48} height={48} variant="circle" />);
        const skeleton = screen.getByRole('status');
        expect(skeleton).toHaveStyle({ width: '48px', height: '48px' });
    });

    it('has accessible role and label', () => {
        render(<Skeleton />);
        const skeleton = screen.getByRole('status');
        expect(skeleton).toHaveAttribute('aria-label', 'Loading');
    });

    it('forwards ref', () => {
        const ref = vi.fn();
        render(<Skeleton ref={ref} />);
        expect(ref).toHaveBeenCalledWith(expect.any(HTMLDivElement));
    });

    it('applies custom className', () => {
        render(<Skeleton className="custom-class" />);
        const skeleton = screen.getByRole('status');
        expect(skeleton).toHaveClass('custom-class');
    });

    it('passes through additional props', () => {
        render(<Skeleton data-testid="my-skeleton" />);
        expect(screen.getByTestId('my-skeleton')).toBeInTheDocument();
    });

    it('merges custom style with dimensions', () => {
        render(<Skeleton width={100} style={{ opacity: 0.5 }} />);
        const skeleton = screen.getByRole('status');
        expect(skeleton).toHaveStyle({ width: '100px', opacity: '0.5' });
    });

    it('renders as a div element', () => {
        render(<Skeleton />);
        const skeleton = screen.getByRole('status');
        expect(skeleton.tagName).toBe('DIV');
    });
});
