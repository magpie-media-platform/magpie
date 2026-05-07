import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';

import { Button } from './Button';

describe('Button', () => {
    it('renders with children', () => {
        render(<Button>Click me</Button>);
        expect(screen.getByRole('button', { name: /click me/i })).toBeInTheDocument();
    });

    it('applies variant classes', () => {
        const { rerender } = render(<Button variant="primary">Primary</Button>);
        expect(screen.getByRole('button')).toHaveAttribute('data-variant', 'primary');

        rerender(<Button variant="secondary">Secondary</Button>);
        expect(screen.getByRole('button')).toHaveAttribute('data-variant', 'secondary');

        rerender(<Button variant="ghost">Ghost</Button>);
        expect(screen.getByRole('button')).toHaveAttribute('data-variant', 'ghost');

        rerender(<Button variant="destructive">Destructive</Button>);
        expect(screen.getByRole('button')).toHaveAttribute('data-variant', 'destructive');
    });

    it('applies size classes', () => {
        const { rerender } = render(<Button size="sm">Small</Button>);
        expect(screen.getByRole('button')).toHaveAttribute('data-size', 'sm');

        rerender(<Button size="md">Medium</Button>);
        expect(screen.getByRole('button')).toHaveAttribute('data-size', 'md');

        rerender(<Button size="lg">Large</Button>);
        expect(screen.getByRole('button')).toHaveAttribute('data-size', 'lg');
    });

    it('defaults to secondary variant and md size', () => {
        render(<Button>Default</Button>);
        const button = screen.getByRole('button');
        expect(button).toHaveAttribute('data-variant', 'secondary');
        expect(button).toHaveAttribute('data-size', 'md');
    });

    it('defaults to type="button"', () => {
        render(<Button>Default</Button>);
        expect(screen.getByRole('button')).toHaveAttribute('type', 'button');
    });

    it('allows type override', () => {
        render(<Button type="submit">Submit</Button>);
        expect(screen.getByRole('button')).toHaveAttribute('type', 'submit');
    });

    it('handles click events', async () => {
        const user = userEvent.setup();
        const handleClick = vi.fn();
        render(<Button onClick={handleClick}>Click</Button>);

        await user.click(screen.getByRole('button'));
        expect(handleClick).toHaveBeenCalledTimes(1);
    });

    it('does not fire click when disabled', async () => {
        const user = userEvent.setup();
        const handleClick = vi.fn();
        render(
            <Button disabled onClick={handleClick}>
                Disabled
            </Button>,
        );

        await user.click(screen.getByRole('button'));
        expect(handleClick).not.toHaveBeenCalled();
    });

    it('sets aria-disabled when disabled', () => {
        render(<Button disabled>Disabled</Button>);
        const button = screen.getByRole('button');
        expect(button).toBeDisabled();
        expect(button).toHaveAttribute('aria-disabled', 'true');
    });

    it('shows loading state', () => {
        render(<Button loading>Loading</Button>);
        const button = screen.getByRole('button');
        expect(button).toHaveAttribute('data-loading', 'true');
        expect(button).toHaveAttribute('aria-busy', 'true');
        expect(button).toBeDisabled();
    });

    it('does not fire click when loading', async () => {
        const user = userEvent.setup();
        const handleClick = vi.fn();
        render(
            <Button loading onClick={handleClick}>
                Loading
            </Button>,
        );

        await user.click(screen.getByRole('button'));
        expect(handleClick).not.toHaveBeenCalled();
    });

    it('forwards ref', () => {
        const ref = vi.fn();
        render(<Button ref={ref}>Ref</Button>);
        expect(ref).toHaveBeenCalledWith(expect.any(HTMLButtonElement));
    });

    it('applies custom className', () => {
        render(<Button className="custom-class">Custom</Button>);
        expect(screen.getByRole('button')).toHaveClass('custom-class');
    });

    it('passes through additional props', () => {
        render(<Button data-testid="my-button">Props</Button>);
        expect(screen.getByTestId('my-button')).toBeInTheDocument();
    });

    describe('asChild', () => {
        it('renders child element with button classes', () => {
            render(
                <Button asChild variant="primary">
                    <a href="/">Link</a>
                </Button>,
            );
            const link = screen.getByRole('link', { name: /link/i });
            expect(link).toHaveClass('button');
            expect(link).toHaveAttribute('data-variant', 'primary');
            expect(link).toHaveAttribute('data-size', 'md');
            expect(link).toHaveAttribute('data-component', 'button');
        });

        it('preserves child element type', () => {
            render(
                <Button asChild variant="secondary">
                    <a href="/test">Test</a>
                </Button>,
            );
            expect(screen.getByRole('link')).toBeInTheDocument();
            expect(screen.queryByRole('button')).not.toBeInTheDocument();
        });

        it('merges child className', () => {
            render(
                <Button asChild variant="ghost">
                    <a href="/" className="custom-link">
                        Go
                    </a>
                </Button>,
            );
            const link = screen.getByRole('link');
            expect(link).toHaveClass('button', 'custom-link');
            expect(link).toHaveAttribute('data-variant', 'ghost');
        });

        it('applies size class', () => {
            render(
                <Button asChild size="lg">
                    <a href="/">Large</a>
                </Button>,
            );
            expect(screen.getByRole('link')).toHaveAttribute('data-size', 'lg');
        });

        it('renders nothing for invalid children', () => {
            const { container } = render(
                <Button asChild variant="primary">
                    {null as unknown as React.ReactElement}
                </Button>,
            );
            expect(container.innerHTML).toBe('');
        });
    });
});
