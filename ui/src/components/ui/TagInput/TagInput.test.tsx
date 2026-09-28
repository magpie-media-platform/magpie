import { useState } from 'react';

import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';

import { Sheet, SheetContent, SheetDescription, SheetTitle } from '../Sheet';
import { TagInput, type TagInputProps } from './TagInput';

function ControlledTagInput({
    initial = [],
    onChange,
    ...props
}: { initial?: string[]; onChange?: (next: string[]) => void } & Partial<TagInputProps>) {
    const [value, setValue] = useState<string[]>(initial);
    return (
        <TagInput
            value={value}
            onChange={(next) => {
                setValue(next);
                onChange?.(next);
            }}
            aria-label="Tags"
            {...props}
        />
    );
}

function input() {
    return screen.getByRole('combobox', { name: /tags/i });
}

describe('TagInput', () => {
    it('adds a chip via Enter', async () => {
        const user = userEvent.setup();
        const onChange = vi.fn();
        render(<ControlledTagInput onChange={onChange} />);

        await user.type(input(), 'Action');
        await user.keyboard('{Enter}');

        expect(onChange).toHaveBeenLastCalledWith(['Action']);
        expect(screen.getByText('Action')).toBeInTheDocument();
    });

    it('adds a chip via comma', async () => {
        const user = userEvent.setup();
        const onChange = vi.fn();
        render(<ControlledTagInput onChange={onChange} />);

        await user.type(input(), 'Drama,');

        expect(onChange).toHaveBeenLastCalledWith(['Drama']);
    });

    it('removes a chip via its remove button', async () => {
        const user = userEvent.setup();
        const onChange = vi.fn();
        render(<ControlledTagInput initial={['Action', 'Drama']} onChange={onChange} />);

        await user.click(screen.getByRole('button', { name: /remove action/i }));

        expect(onChange).toHaveBeenLastCalledWith(['Drama']);
        expect(screen.queryByText('Action')).not.toBeInTheDocument();
    });

    it('removes the last chip on backspace when the field is empty', async () => {
        const user = userEvent.setup();
        const onChange = vi.fn();
        render(<ControlledTagInput initial={['Action', 'Drama']} onChange={onChange} />);

        await user.click(input());
        await user.keyboard('{Backspace}');

        expect(onChange).toHaveBeenLastCalledWith(['Action']);
    });

    it('picks a value from the suggestions listbox', async () => {
        const user = userEvent.setup();
        const onChange = vi.fn();
        render(<ControlledTagInput suggestions={['Movies', 'Documentary']} onChange={onChange} />);

        await user.click(input());
        await user.type(input(), 'Doc');

        const option = await screen.findByRole('option', { name: 'Documentary' });
        await user.click(option);

        expect(onChange).toHaveBeenLastCalledWith(['Documentary']);
    });

    it('closes only its suggestions on the first Escape inside a sheet, and the sheet on the second', async () => {
        const user = userEvent.setup();
        const onOpenChange = vi.fn();
        render(
            <Sheet open onOpenChange={onOpenChange}>
                <SheetContent>
                    <SheetTitle>Tier list</SheetTitle>
                    <SheetDescription>Edit the tier list.</SheetDescription>
                    <ControlledTagInput suggestions={['video/mp4']} allowCreate />
                </SheetContent>
            </Sheet>,
        );

        await user.type(input(), 'video');
        expect(await screen.findByRole('listbox')).toBeInTheDocument();

        await user.keyboard('{Escape}');
        expect(screen.queryByRole('listbox')).not.toBeInTheDocument();
        expect(onOpenChange).not.toHaveBeenCalled();
        expect(input()).toHaveValue('video');

        await user.keyboard('{Escape}');
        expect(onOpenChange).toHaveBeenCalledWith(false);
    });

    it('closes the dropdown when the input is blurred', async () => {
        const user = userEvent.setup();
        render(
            <>
                <ControlledTagInput suggestions={['Movies', 'Documentary']} />
                <button type="button">outside</button>
            </>,
        );

        await user.click(input());
        expect(await screen.findByRole('listbox')).toBeInTheDocument();

        await user.click(screen.getByRole('button', { name: 'outside' }));

        await waitFor(() => expect(screen.queryByRole('listbox')).not.toBeInTheDocument());
    });

    it('offers an Add affordance for a new value when allowCreate', async () => {
        const user = userEvent.setup();
        const onChange = vi.fn();
        render(<ControlledTagInput suggestions={['Movies']} allowCreate onChange={onChange} />);

        await user.type(input(), 'Brand New');

        const createOption = await screen.findByText(/Add .Brand New./);
        await user.click(createOption);

        expect(onChange).toHaveBeenLastCalledWith(['Brand New']);
    });

    it('does not commit a new value when allowCreate is false', async () => {
        const user = userEvent.setup();
        const onChange = vi.fn();
        render(<ControlledTagInput suggestions={['Movies']} allowCreate={false} onChange={onChange} />);

        await user.type(input(), 'Unknown');
        await user.keyboard('{Enter}');

        expect(onChange).not.toHaveBeenCalled();
        await waitFor(() => expect(screen.queryByText(/Add .Unknown./)).not.toBeInTheDocument());
    });

    it('dedupes a value already present (case-insensitive)', async () => {
        const user = userEvent.setup();
        const onChange = vi.fn();
        render(<ControlledTagInput initial={['Action']} onChange={onChange} />);

        await user.type(input(), 'action');
        await user.keyboard('{Enter}');

        expect(onChange).not.toHaveBeenCalled();
    });

    it('renders the chips and the input inside one box, with the input last', () => {
        render(<ControlledTagInput initial={['Action', 'Drama']} />);

        const box = screen.getByTestId('tag-input__box');
        expect(box).toContainElement(input());
        expect(input().parentElement).toBe(box);
        expect(box.lastElementChild).toBe(input());
    });

    it('focuses the input when the box is clicked', async () => {
        const user = userEvent.setup();
        render(<ControlledTagInput initial={['Action']} />);

        const box = screen.getByTestId('tag-input__box');
        expect(input()).not.toHaveFocus();

        await user.click(box);

        expect(input()).toHaveFocus();
    });

    it('trims whitespace from committed values', async () => {
        const user = userEvent.setup();
        const onChange = vi.fn();
        render(<ControlledTagInput onChange={onChange} />);

        await user.type(input(), '  Spaced  ');
        await user.keyboard('{Enter}');

        expect(onChange).toHaveBeenLastCalledWith(['Spaced']);
    });
});
