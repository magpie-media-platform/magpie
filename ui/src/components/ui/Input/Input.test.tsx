import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';

import { Input } from './Input';

describe('Input', () => {
    it('renders an input element', () => {
        render(<Input aria-label="Test input" />);
        expect(screen.getByRole('textbox')).toBeInTheDocument();
    });

    it('applies size classes', () => {
        const { rerender } = render(<Input size="sm" aria-label="Small" />);
        expect(screen.getByRole('textbox').closest('.input-wrapper')).toHaveAttribute('data-size', 'sm');

        rerender(<Input size="md" aria-label="Medium" />);
        expect(screen.getByRole('textbox').closest('.input-wrapper')).toHaveAttribute('data-size', 'md');

        rerender(<Input size="lg" aria-label="Large" />);
        expect(screen.getByRole('textbox').closest('.input-wrapper')).toHaveAttribute('data-size', 'lg');
    });

    it('defaults to md size', () => {
        render(<Input aria-label="Default" />);
        expect(screen.getByRole('textbox').closest('.input-wrapper')).toHaveAttribute('data-size', 'md');
    });

    it('handles input change', async () => {
        const user = userEvent.setup();
        const handleChange = vi.fn();
        render(<Input aria-label="Test" onChange={handleChange} />);

        await user.type(screen.getByRole('textbox'), 'hello');
        expect(handleChange).toHaveBeenCalledTimes(5);
    });

    it('accepts and displays placeholder', () => {
        render(<Input placeholder="Enter text..." aria-label="With placeholder" />);
        expect(screen.getByPlaceholderText('Enter text...')).toBeInTheDocument();
    });

    it('applies disabled state', () => {
        render(<Input disabled aria-label="Disabled" />);
        const input = screen.getByRole('textbox');
        expect(input).toBeDisabled();
        expect(input.closest('.input-wrapper')).toHaveAttribute('data-disabled', 'true');
    });

    it('does not accept input when disabled', async () => {
        const user = userEvent.setup();
        const handleChange = vi.fn();
        render(<Input disabled onChange={handleChange} aria-label="Disabled" />);

        await user.type(screen.getByRole('textbox'), 'hello');
        expect(handleChange).not.toHaveBeenCalled();
    });

    it('applies error state', () => {
        render(<Input error aria-label="Error" />);
        const input = screen.getByRole('textbox');
        expect(input).toHaveAttribute('aria-invalid', 'true');
        expect(input.closest('.input-wrapper')).toHaveAttribute('data-error', 'true');
    });

    it('displays error message', () => {
        render(<Input id="email" error errorMessage="Invalid email" aria-label="Email" />);
        expect(screen.getByRole('alert')).toHaveTextContent('Invalid email');
    });

    it('connects error message to input via aria-describedby', () => {
        render(<Input id="email" error errorMessage="Invalid email" aria-label="Email" />);
        const input = screen.getByRole('textbox');
        expect(input).toHaveAttribute('aria-describedby', 'email-error');
        expect(screen.getByRole('alert')).toHaveAttribute('id', 'email-error');
    });

    it('preserves existing aria-describedby when adding error', () => {
        render(
            <Input id="email" error errorMessage="Invalid email" aria-describedby="email-hint" aria-label="Email" />,
        );
        const input = screen.getByRole('textbox');
        expect(input).toHaveAttribute('aria-describedby', 'email-hint email-error');
    });

    it('renders leading icon', () => {
        render(<Input leadingIcon={<span data-testid="search-icon">🔍</span>} aria-label="Search" />);
        expect(screen.getByTestId('search-icon')).toBeInTheDocument();
        expect(screen.getByRole('textbox').closest('.input-wrapper')).toHaveAttribute('data-has-leading', 'true');
    });

    it('renders trailing icon', () => {
        render(<Input trailingIcon={<span data-testid="clear-icon">✕</span>} aria-label="Clearable" />);
        expect(screen.getByTestId('clear-icon')).toBeInTheDocument();
        expect(screen.getByRole('textbox').closest('.input-wrapper')).toHaveAttribute('data-has-trailing', 'true');
    });

    it('renders both leading and trailing icons', () => {
        render(
            <Input
                leadingIcon={<span data-testid="search-icon">🔍</span>}
                trailingIcon={<span data-testid="clear-icon">✕</span>}
                aria-label="Search with clear"
            />,
        );
        expect(screen.getByTestId('search-icon')).toBeInTheDocument();
        expect(screen.getByTestId('clear-icon')).toBeInTheDocument();
        const wrapper = screen.getByRole('textbox').closest('.input-wrapper');
        expect(wrapper).toHaveAttribute('data-has-leading', 'true');
        expect(wrapper).toHaveAttribute('data-has-trailing', 'true');
    });

    it('renders an interactive trailing action outside a decorative wrapper', () => {
        render(
            <Input
                trailingAction={
                    <button type="button" aria-label="Unlock">
                        x
                    </button>
                }
                aria-label="With action"
            />,
        );
        const action = screen.getByRole('button', { name: 'Unlock' });
        expect(action).toBeInTheDocument();
        expect(action.closest('[aria-hidden="true"]')).toBeNull();
        expect(screen.getByRole('textbox').closest('.input-wrapper')).toHaveAttribute('data-has-trailing', 'true');
    });

    it('forwards ref', () => {
        const ref = vi.fn();
        render(<Input ref={ref} aria-label="Ref test" />);
        expect(ref).toHaveBeenCalledWith(expect.any(HTMLInputElement));
    });

    it('applies custom className to wrapper', () => {
        render(<Input className="custom-class" aria-label="Custom" />);
        expect(screen.getByRole('textbox').closest('.input-wrapper')).toHaveClass('custom-class');
    });

    it('passes through additional props', () => {
        render(<Input data-testid="my-input" aria-label="Props test" />);
        expect(screen.getByTestId('my-input')).toBeInTheDocument();
    });

    it('supports different input types', () => {
        const { rerender } = render(<Input type="email" aria-label="Email" />);
        expect(screen.getByRole('textbox')).toHaveAttribute('type', 'email');

        rerender(<Input type="password" aria-label="Password" />);
        expect(screen.getByLabelText('Password')).toHaveAttribute('type', 'password');
    });

    it('supports required attribute', () => {
        render(<Input required aria-label="Required field" />);
        expect(screen.getByRole('textbox')).toBeRequired();
    });

    it('supports readonly attribute', () => {
        render(<Input readOnly value="Read only value" aria-label="Readonly" />);
        expect(screen.getByRole('textbox')).toHaveAttribute('readonly');
    });

    it('supports controlled value', () => {
        const { rerender } = render(<Input value="initial" onChange={() => {}} aria-label="Controlled" />);
        expect(screen.getByRole('textbox')).toHaveValue('initial');

        rerender(<Input value="updated" onChange={() => {}} aria-label="Controlled" />);
        expect(screen.getByRole('textbox')).toHaveValue('updated');
    });
});

it('links errors without caller IDs and keeps instances distinct', () => {
    render(
        <>
            <Input aria-label="First" errorMessage="First error" />
            <Input aria-label="Second" errorMessage="Second error" />
        </>,
    );
    expect(screen.getByLabelText('First')).toHaveAccessibleDescription('First error');
    expect(screen.getByLabelText('Second')).toHaveAccessibleDescription('Second error');
    expect(screen.getByLabelText('First').id).not.toBe(screen.getByLabelText('Second').id);
});
