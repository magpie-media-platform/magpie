import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';

import { Checkbox } from './Checkbox';

describe('Checkbox', () => {
    it('renders a checkbox', () => {
        render(<Checkbox aria-label="Test checkbox" />);
        expect(screen.getByRole('checkbox', { name: /test checkbox/i })).toBeInTheDocument();
    });

    it('is unchecked by default', () => {
        render(<Checkbox aria-label="Test checkbox" />);
        expect(screen.getByRole('checkbox')).not.toBeChecked();
    });

    it('toggles checked state on click', async () => {
        const user = userEvent.setup();
        const handleCheckedChange = vi.fn();
        render(<Checkbox aria-label="Test checkbox" onCheckedChange={handleCheckedChange} />);

        await user.click(screen.getByRole('checkbox'));

        expect(handleCheckedChange).toHaveBeenCalledWith(true);
    });

    it('toggles from checked to unchecked', async () => {
        const user = userEvent.setup();
        const handleCheckedChange = vi.fn();
        render(<Checkbox aria-label="Test checkbox" defaultChecked onCheckedChange={handleCheckedChange} />);

        expect(screen.getByRole('checkbox')).toBeChecked();

        await user.click(screen.getByRole('checkbox'));

        expect(handleCheckedChange).toHaveBeenCalledWith(false);
    });

    it('supports controlled checked state', () => {
        const { rerender } = render(<Checkbox aria-label="Controlled" checked={false} />);
        expect(screen.getByRole('checkbox')).not.toBeChecked();

        rerender(<Checkbox aria-label="Controlled" checked={true} />);
        expect(screen.getByRole('checkbox')).toBeChecked();
    });

    it('applies size classes', () => {
        const { rerender } = render(<Checkbox aria-label="Small" size="sm" />);
        expect(screen.getByRole('checkbox')).toHaveAttribute('data-size', 'sm');

        rerender(<Checkbox aria-label="Medium" size="md" />);
        expect(screen.getByRole('checkbox')).toHaveAttribute('data-size', 'md');

        rerender(<Checkbox aria-label="Large" size="lg" />);
        expect(screen.getByRole('checkbox')).toHaveAttribute('data-size', 'lg');
    });

    it('defaults to md size', () => {
        render(<Checkbox aria-label="Default size" />);
        expect(screen.getByRole('checkbox')).toHaveAttribute('data-size', 'md');
    });

    it('applies error state', () => {
        render(<Checkbox aria-label="Error" error />);
        expect(screen.getByRole('checkbox')).toHaveAttribute('data-error', 'true');
    });

    it('applies disabled state', async () => {
        const user = userEvent.setup();
        const handleCheckedChange = vi.fn();
        render(<Checkbox aria-label="Disabled" disabled onCheckedChange={handleCheckedChange} />);

        const checkbox = screen.getByRole('checkbox');
        expect(checkbox).toBeDisabled();

        await user.click(checkbox);
        expect(handleCheckedChange).not.toHaveBeenCalled();
    });

    it('supports keyboard interaction with Space', async () => {
        const user = userEvent.setup();
        const handleCheckedChange = vi.fn();
        render(<Checkbox aria-label="Keyboard" onCheckedChange={handleCheckedChange} />);

        const checkbox = screen.getByRole('checkbox');
        checkbox.focus();

        await user.keyboard(' ');

        expect(handleCheckedChange).toHaveBeenCalledWith(true);
    });

    it('applies custom className', () => {
        render(<Checkbox aria-label="Custom" className="custom-class" />);
        expect(screen.getByRole('checkbox')).toHaveClass('custom-class');
    });

    it('passes through additional props', () => {
        render(<Checkbox aria-label="Props test" data-testid="my-checkbox" />);
        expect(screen.getByTestId('my-checkbox')).toBeInTheDocument();
    });

    it('supports indeterminate state', () => {
        render(<Checkbox aria-label="Indeterminate" checked="indeterminate" />);
        const checkbox = screen.getByRole('checkbox');
        expect(checkbox).toHaveAttribute('data-state', 'indeterminate');
    });

    it('renders with required attribute', () => {
        render(<Checkbox aria-label="Required" required />);
        expect(screen.getByRole('checkbox')).toBeRequired();
    });

    it('has accessible name', () => {
        render(<Checkbox aria-label="Accessible checkbox" />);
        expect(screen.getByRole('checkbox', { name: 'Accessible checkbox' })).toBeInTheDocument();
    });

    it('maintains base class with error state', () => {
        render(<Checkbox aria-label="Error combo" error size="lg" />);
        const checkbox = screen.getByRole('checkbox');
        expect(checkbox).toHaveClass('checkbox');
        expect(checkbox).toHaveAttribute('data-size', 'lg');
        expect(checkbox).toHaveAttribute('data-error', 'true');
    });

    it('applies aria-describedby for form hints', () => {
        render(
            <>
                <Checkbox aria-label="Described" aria-describedby="checkbox-hint" />
                <span id="checkbox-hint">Select to enable notifications</span>
            </>,
        );
        expect(screen.getByRole('checkbox')).toHaveAttribute('aria-describedby', 'checkbox-hint');
    });

    it('applies aria-invalid when error is true', () => {
        render(<Checkbox aria-label="Error" error />);
        expect(screen.getByRole('checkbox')).toHaveAttribute('aria-invalid', 'true');
    });

    it('does not apply aria-invalid when error is false', () => {
        render(<Checkbox aria-label="No error" error={false} />);
        expect(screen.getByRole('checkbox')).not.toHaveAttribute('aria-invalid');
    });
});
