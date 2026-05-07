import { render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';

import { Separator } from './Separator';

describe('Separator', () => {
    it('renders as horizontal by default', () => {
        render(<Separator data-testid="separator" />);
        const separator = screen.getByTestId('separator');
        expect(separator).toHaveClass('separator');
        expect(separator).toHaveAttribute('data-orientation', 'horizontal');
    });

    it('renders as vertical when orientation is vertical', () => {
        render(<Separator orientation="vertical" data-testid="separator" />);
        const separator = screen.getByTestId('separator');
        expect(separator).toHaveAttribute('data-orientation', 'vertical');
    });

    it('is decorative by default', () => {
        render(<Separator data-testid="separator" />);
        const separator = screen.getByTestId('separator');

        expect(separator).toHaveAttribute('role', 'none');
        expect(separator).not.toHaveAttribute('aria-orientation');
    });

    it('has separator role when not decorative', () => {
        render(<Separator decorative={false} data-testid="separator" />);
        const separator = screen.getByTestId('separator');
        expect(separator).toHaveAttribute('role', 'separator');
    });

    it('forwards ref', () => {
        const ref = vi.fn();
        render(<Separator ref={ref} />);
        expect(ref).toHaveBeenCalledWith(expect.any(HTMLElement));
    });

    it('applies custom className', () => {
        render(<Separator className="custom-class" data-testid="separator" />);
        const separator = screen.getByTestId('separator');
        expect(separator).toHaveClass('custom-class');
        expect(separator).toHaveClass('separator');
    });

    it('passes through additional props', () => {
        render(<Separator data-testid="my-separator" aria-label="divider" />);
        const separator = screen.getByTestId('my-separator');
        expect(separator).toHaveAttribute('aria-label', 'divider');
    });
});
