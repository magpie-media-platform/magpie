import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';

import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue, SelectGroup, SelectLabel } from './Select';

function renderSelect(props: {
    value?: string;
    defaultValue?: string;
    onValueChange?: (value: string) => void;
    placeholder?: string;
    size?: 'sm' | 'md' | 'lg';
    error?: boolean;
    disabled?: boolean;
    'aria-label'?: string;
}) {
    const {
        value,
        defaultValue,
        onValueChange,
        placeholder = 'Select an option',
        size,
        error,
        disabled,
        'aria-label': ariaLabel = 'Test select',
    } = props;

    return render(
        <Select value={value} defaultValue={defaultValue} onValueChange={onValueChange} disabled={disabled}>
            <SelectTrigger size={size} error={error} aria-label={ariaLabel}>
                <SelectValue placeholder={placeholder} />
            </SelectTrigger>
            <SelectContent>
                <SelectItem value="apple">Apple</SelectItem>
                <SelectItem value="banana">Banana</SelectItem>
                <SelectItem value="cherry">Cherry</SelectItem>
            </SelectContent>
        </Select>,
    );
}

describe('Select', () => {
    it.each([
        { keys: '{Escape}', dismissCount: 1, changeCount: 0 },
        { keys: '{ArrowDown}{Enter}', dismissCount: 0, changeCount: 1 },
    ])('distinguishes dismissal from selection: $keys', async ({ keys, dismissCount, changeCount }) => {
        const user = userEvent.setup();
        const onDismiss = vi.fn();
        const onValueChange = vi.fn();
        render(
            <Select value="first" onValueChange={onValueChange} onDismiss={onDismiss}>
                <SelectTrigger aria-label="Choice">
                    <SelectValue />
                </SelectTrigger>
                <SelectContent>
                    <SelectItem value="first">First</SelectItem>
                    <SelectItem value="second">Second</SelectItem>
                </SelectContent>
            </Select>,
        );

        screen.getByRole('combobox', { name: 'Choice' }).focus();
        await user.keyboard('{Enter}');
        await user.keyboard(keys);

        expect(onDismiss).toHaveBeenCalledTimes(dismissCount);
        expect(onValueChange).toHaveBeenCalledTimes(changeCount);
    });

    it('renders a select trigger', () => {
        renderSelect({});
        expect(screen.getByRole('combobox', { name: /test select/i })).toBeInTheDocument();
    });

    it('displays placeholder when no value is selected', () => {
        renderSelect({ placeholder: 'Choose a fruit' });
        expect(screen.getByRole('combobox')).toHaveTextContent('Choose a fruit');
    });

    it('opens dropdown on click', async () => {
        const user = userEvent.setup();
        renderSelect({});

        await user.click(screen.getByRole('combobox'));

        expect(screen.getByRole('listbox')).toBeInTheDocument();
        expect(screen.getByRole('option', { name: 'Apple' })).toBeInTheDocument();
        expect(screen.getByRole('option', { name: 'Banana' })).toBeInTheDocument();
        expect(screen.getByRole('option', { name: 'Cherry' })).toBeInTheDocument();
    });

    it('renders the dropdown on the themed select-content surface', async () => {
        const user = userEvent.setup();
        renderSelect({});

        await user.click(screen.getByRole('combobox'));

        const content = screen.getByRole('listbox').closest('.select-content');
        expect(content).not.toBeNull();
    });

    it('selects an option on click', async () => {
        const user = userEvent.setup();
        const handleValueChange = vi.fn();
        renderSelect({ onValueChange: handleValueChange });

        await user.click(screen.getByRole('combobox'));
        await user.click(screen.getByRole('option', { name: 'Banana' }));

        expect(handleValueChange).toHaveBeenCalledWith('banana');
    });

    it('displays selected value', async () => {
        const user = userEvent.setup();
        renderSelect({ defaultValue: 'apple' });

        expect(screen.getByRole('combobox')).toHaveTextContent('Apple');

        await user.click(screen.getByRole('combobox'));
        await user.click(screen.getByRole('option', { name: 'Cherry' }));

        expect(screen.getByRole('combobox')).toHaveTextContent('Cherry');
    });

    it('applies size classes', () => {
        const { rerender } = render(
            <Select>
                <SelectTrigger size="sm" aria-label="Small">
                    Select
                </SelectTrigger>
                <SelectContent>
                    <SelectItem value="a">A</SelectItem>
                </SelectContent>
            </Select>,
        );
        expect(screen.getByRole('combobox')).toHaveAttribute('data-size', 'sm');

        rerender(
            <Select>
                <SelectTrigger size="md" aria-label="Medium">
                    Select
                </SelectTrigger>
                <SelectContent>
                    <SelectItem value="a">A</SelectItem>
                </SelectContent>
            </Select>,
        );
        expect(screen.getByRole('combobox')).toHaveAttribute('data-size', 'md');

        rerender(
            <Select>
                <SelectTrigger size="lg" aria-label="Large">
                    Select
                </SelectTrigger>
                <SelectContent>
                    <SelectItem value="a">A</SelectItem>
                </SelectContent>
            </Select>,
        );
        expect(screen.getByRole('combobox')).toHaveAttribute('data-size', 'lg');
    });

    it('defaults to md size', () => {
        renderSelect({});
        expect(screen.getByRole('combobox')).toHaveAttribute('data-size', 'md');
    });

    it('applies error state', () => {
        renderSelect({ error: true });
        expect(screen.getByRole('combobox')).toHaveAttribute('data-error', 'true');
    });

    it('applies disabled state', async () => {
        const user = userEvent.setup();
        const handleValueChange = vi.fn();
        renderSelect({ disabled: true, onValueChange: handleValueChange });

        const trigger = screen.getByRole('combobox');
        expect(trigger).toBeDisabled();

        await user.click(trigger);
        expect(screen.queryByRole('listbox')).not.toBeInTheDocument();
        expect(handleValueChange).not.toHaveBeenCalled();
    });

    it('supports keyboard navigation', async () => {
        const user = userEvent.setup();
        const handleValueChange = vi.fn();
        renderSelect({ onValueChange: handleValueChange });

        const trigger = screen.getByRole('combobox');
        trigger.focus();

        await user.keyboard('{Enter}');
        expect(screen.getByRole('listbox')).toBeInTheDocument();

        await user.keyboard('{ArrowDown}');
        await user.keyboard('{Enter}');

        expect(handleValueChange).toHaveBeenCalledWith('banana');
    });

    it('closes on escape', async () => {
        const user = userEvent.setup();
        renderSelect({});

        await user.click(screen.getByRole('combobox'));
        expect(screen.getByRole('listbox')).toBeInTheDocument();

        await user.keyboard('{Escape}');
        expect(screen.queryByRole('listbox')).not.toBeInTheDocument();
    });

    it('supports controlled value', () => {
        const { rerender } = render(
            <Select value="apple">
                <SelectTrigger aria-label="Controlled">
                    <SelectValue />
                </SelectTrigger>
                <SelectContent>
                    <SelectItem value="apple">Apple</SelectItem>
                    <SelectItem value="banana">Banana</SelectItem>
                </SelectContent>
            </Select>,
        );
        expect(screen.getByRole('combobox')).toHaveTextContent('Apple');

        rerender(
            <Select value="banana">
                <SelectTrigger aria-label="Controlled">
                    <SelectValue />
                </SelectTrigger>
                <SelectContent>
                    <SelectItem value="apple">Apple</SelectItem>
                    <SelectItem value="banana">Banana</SelectItem>
                </SelectContent>
            </Select>,
        );
        expect(screen.getByRole('combobox')).toHaveTextContent('Banana');
    });

    it('supports disabled items', async () => {
        const user = userEvent.setup();
        const handleValueChange = vi.fn();

        render(
            <Select onValueChange={handleValueChange}>
                <SelectTrigger aria-label="With disabled">Select</SelectTrigger>
                <SelectContent>
                    <SelectItem value="apple">Apple</SelectItem>
                    <SelectItem value="banana" disabled>
                        Banana
                    </SelectItem>
                    <SelectItem value="cherry">Cherry</SelectItem>
                </SelectContent>
            </Select>,
        );

        await user.click(screen.getByRole('combobox'));
        await user.click(screen.getByRole('option', { name: 'Banana' }));

        expect(handleValueChange).not.toHaveBeenCalled();
    });

    it('renders groups with labels', async () => {
        const user = userEvent.setup();

        render(
            <Select>
                <SelectTrigger aria-label="Grouped">Select</SelectTrigger>
                <SelectContent>
                    <SelectGroup>
                        <SelectLabel>Fruits</SelectLabel>
                        <SelectItem value="apple">Apple</SelectItem>
                        <SelectItem value="banana">Banana</SelectItem>
                    </SelectGroup>
                    <SelectGroup>
                        <SelectLabel>Vegetables</SelectLabel>
                        <SelectItem value="carrot">Carrot</SelectItem>
                    </SelectGroup>
                </SelectContent>
            </Select>,
        );

        await user.click(screen.getByRole('combobox'));

        expect(screen.getByText('Fruits')).toBeInTheDocument();
        expect(screen.getByText('Vegetables')).toBeInTheDocument();
        expect(screen.getByRole('option', { name: 'Apple' })).toBeInTheDocument();
        expect(screen.getByRole('option', { name: 'Carrot' })).toBeInTheDocument();
    });

    it('applies custom className to trigger', () => {
        render(
            <Select>
                <SelectTrigger className="custom-class" aria-label="Custom">
                    Select
                </SelectTrigger>
                <SelectContent>
                    <SelectItem value="a">A</SelectItem>
                </SelectContent>
            </Select>,
        );
        expect(screen.getByRole('combobox')).toHaveClass('custom-class');
    });

    it('passes through additional props', () => {
        render(
            <Select>
                <SelectTrigger data-testid="my-select" aria-label="Props test">
                    Select
                </SelectTrigger>
                <SelectContent>
                    <SelectItem value="a">A</SelectItem>
                </SelectContent>
            </Select>,
        );
        expect(screen.getByTestId('my-select')).toBeInTheDocument();
    });

    it('applies aria-describedby for form hints', () => {
        render(
            <>
                <Select>
                    <SelectTrigger aria-label="Described" aria-describedby="select-hint">
                        Select
                    </SelectTrigger>
                    <SelectContent>
                        <SelectItem value="a">A</SelectItem>
                    </SelectContent>
                </Select>
                <span id="select-hint">Choose an option from the list</span>
            </>,
        );
        expect(screen.getByRole('combobox')).toHaveAttribute('aria-describedby', 'select-hint');
    });

    it('applies aria-invalid when error is true', () => {
        renderSelect({ error: true });
        expect(screen.getByRole('combobox')).toHaveAttribute('aria-invalid', 'true');
    });

    it('does not apply aria-invalid when error is false', () => {
        renderSelect({ error: false });
        expect(screen.getByRole('combobox')).not.toHaveAttribute('aria-invalid');
    });
});
