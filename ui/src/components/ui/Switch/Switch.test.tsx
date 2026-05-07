import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';

import { Switch } from './Switch';

describe('Switch', () => {
    it('renders a switch', () => {
        render(<Switch aria-label="Test switch" />);
        expect(screen.getByRole('switch', { name: /test switch/i })).toBeInTheDocument();
    });

    it('is unchecked by default', () => {
        render(<Switch aria-label="Test switch" />);
        expect(screen.getByRole('switch')).not.toBeChecked();
    });

    it('toggles checked state on click', async () => {
        const user = userEvent.setup();
        const handleCheckedChange = vi.fn();
        render(<Switch aria-label="Test switch" onCheckedChange={handleCheckedChange} />);

        await user.click(screen.getByRole('switch'));

        expect(handleCheckedChange).toHaveBeenCalledWith(true);
    });

    it('toggles from checked to unchecked', async () => {
        const user = userEvent.setup();
        const handleCheckedChange = vi.fn();
        render(<Switch aria-label="Test switch" defaultChecked onCheckedChange={handleCheckedChange} />);

        expect(screen.getByRole('switch')).toBeChecked();

        await user.click(screen.getByRole('switch'));

        expect(handleCheckedChange).toHaveBeenCalledWith(false);
    });

    it('supports controlled checked state', () => {
        const { rerender } = render(<Switch aria-label="Controlled" checked={false} />);
        expect(screen.getByRole('switch')).not.toBeChecked();

        rerender(<Switch aria-label="Controlled" checked={true} />);
        expect(screen.getByRole('switch')).toBeChecked();
    });

    it('applies size classes', () => {
        const { rerender } = render(<Switch aria-label="Small" size="sm" />);
        expect(screen.getByRole('switch')).toHaveAttribute('data-size', 'sm');

        rerender(<Switch aria-label="Medium" size="md" />);
        expect(screen.getByRole('switch')).toHaveAttribute('data-size', 'md');

        rerender(<Switch aria-label="Large" size="lg" />);
        expect(screen.getByRole('switch')).toHaveAttribute('data-size', 'lg');
    });

    it('defaults to md size', () => {
        render(<Switch aria-label="Default size" />);
        expect(screen.getByRole('switch')).toHaveAttribute('data-size', 'md');
    });

    it('applies error state', () => {
        render(<Switch aria-label="Error" error />);
        expect(screen.getByRole('switch')).toHaveAttribute('data-error', 'true');
    });

    it('applies disabled state', async () => {
        const user = userEvent.setup();
        const handleCheckedChange = vi.fn();
        render(<Switch aria-label="Disabled" disabled onCheckedChange={handleCheckedChange} />);

        const switchEl = screen.getByRole('switch');
        expect(switchEl).toBeDisabled();

        await user.click(switchEl);
        expect(handleCheckedChange).not.toHaveBeenCalled();
    });

    it('supports keyboard interaction with Space', async () => {
        const user = userEvent.setup();
        const handleCheckedChange = vi.fn();
        render(<Switch aria-label="Keyboard" onCheckedChange={handleCheckedChange} />);

        const switchEl = screen.getByRole('switch');
        switchEl.focus();

        await user.keyboard(' ');

        expect(handleCheckedChange).toHaveBeenCalledWith(true);
    });

    it('applies custom className', () => {
        render(<Switch aria-label="Custom" className="custom-class" />);
        expect(screen.getByRole('switch')).toHaveClass('custom-class');
    });

    it('passes through additional props', () => {
        render(<Switch aria-label="Props test" data-testid="my-switch" />);
        expect(screen.getByTestId('my-switch')).toBeInTheDocument();
    });

    it('renders with required attribute', () => {
        render(<Switch aria-label="Required" required />);
        expect(screen.getByRole('switch')).toHaveAttribute('aria-required', 'true');
    });

    it('has accessible name', () => {
        render(<Switch aria-label="Accessible switch" />);
        expect(screen.getByRole('switch', { name: 'Accessible switch' })).toBeInTheDocument();
    });

    it('maintains base class with error state', () => {
        render(<Switch aria-label="Error combo" error size="lg" />);
        const switchEl = screen.getByRole('switch');
        expect(switchEl).toHaveClass('switch');
        expect(switchEl).toHaveAttribute('data-size', 'lg');
        expect(switchEl).toHaveAttribute('data-error', 'true');
    });

    it('renders the thumb element', () => {
        render(<Switch aria-label="With thumb" />);
        const switchEl = screen.getByRole('switch');
        const thumb = switchEl.querySelector('.switch__thumb');
        expect(thumb).toBeInTheDocument();
    });

    it('thumb has correct data-state attribute when checked', () => {
        render(<Switch aria-label="Checked thumb" defaultChecked />);
        const switchEl = screen.getByRole('switch');
        const thumb = switchEl.querySelector('.switch__thumb');
        expect(thumb).toHaveAttribute('data-state', 'checked');
    });

    it('thumb has correct data-state attribute when unchecked', () => {
        render(<Switch aria-label="Unchecked thumb" />);
        const switchEl = screen.getByRole('switch');
        const thumb = switchEl.querySelector('.switch__thumb');
        expect(thumb).toHaveAttribute('data-state', 'unchecked');
    });

    it('applies aria-describedby for form hints', () => {
        render(
            <>
                <Switch aria-label="Described" aria-describedby="switch-hint" />
                <span id="switch-hint">Enable to receive notifications</span>
            </>,
        );
        expect(screen.getByRole('switch')).toHaveAttribute('aria-describedby', 'switch-hint');
    });

    it('applies aria-invalid when error is true', () => {
        render(<Switch aria-label="Error" error />);
        expect(screen.getByRole('switch')).toHaveAttribute('aria-invalid', 'true');
    });

    it('does not apply aria-invalid when error is false', () => {
        render(<Switch aria-label="No error" error={false} />);
        expect(screen.getByRole('switch')).not.toHaveAttribute('aria-invalid');
    });
});
