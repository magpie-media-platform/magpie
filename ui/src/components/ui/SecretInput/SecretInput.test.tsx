import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useState, type ChangeEvent } from 'react';
import { describe, expect, it, vi } from 'vitest';

import { SecretInput } from './SecretInput';

function ControlledSecret(props: {
    configured: boolean;
    disabled?: boolean;
    initialValue?: string;
    onChange?: (value: string) => void;
}) {
    const [value, setValue] = useState(props.initialValue ?? '');
    return (
        <SecretInput
            aria-label="API Key"
            fieldLabel="API Key"
            configured={props.configured}
            disabled={props.disabled}
            value={value}
            onChange={(e: ChangeEvent<HTMLInputElement>) => {
                setValue(e.target.value);
                props.onChange?.(e.target.value);
            }}
        />
    );
}

describe('SecretInput', () => {
    it('renders an unconfigured secret as an editable password with no unlock button', () => {
        render(<ControlledSecret configured={false} />);

        const input = screen.getByLabelText('API Key');
        expect(input).toHaveAttribute('type', 'password');
        expect(input).not.toHaveAttribute('readonly');
        expect(screen.queryByRole('button', { name: 'Clear API Key' })).not.toBeInTheDocument();
    });

    it('masks and locks a configured secret with a masked placeholder', () => {
        render(<ControlledSecret configured />);

        const input = screen.getByLabelText('API Key') as HTMLInputElement;
        expect(input.value).toBe('');
        expect(input).toHaveAttribute('readonly');
        expect(input).toHaveAttribute('placeholder', '********');
        const clearButton = screen.getByRole('button', { name: 'Clear API Key' });
        expect(clearButton).toBeInTheDocument();
        expect(clearButton).toHaveTextContent('Clear');
    });

    it('unlocks, focuses, and accepts a replacement when the clear button is clicked', async () => {
        const user = userEvent.setup();
        const onChange = vi.fn();
        render(<ControlledSecret configured onChange={onChange} />);

        await user.click(screen.getByRole('button', { name: 'Clear API Key' }));

        const input = screen.getByLabelText('API Key');
        expect(input).not.toHaveAttribute('readonly');
        expect(input).toHaveFocus();

        await user.type(input, 'new-secret');
        expect(onChange).toHaveBeenLastCalledWith('new-secret');
        expect(screen.queryByRole('button', { name: 'Clear API Key' })).not.toBeInTheDocument();
    });

    it('exposes a keyboard-reachable clear control with a field-specific label', async () => {
        const user = userEvent.setup();
        render(<ControlledSecret configured />);

        await user.tab();
        await user.tab();
        expect(screen.getByRole('button', { name: 'Clear API Key' })).toHaveFocus();
    });

    it('disables the clear control while the form is disabled', () => {
        render(<ControlledSecret configured disabled />);

        expect(screen.getByRole('button', { name: 'Clear API Key' })).toBeDisabled();
    });

    it('re-locks the mask when an unlocked field is left empty', async () => {
        const user = userEvent.setup();
        render(<ControlledSecret configured />);

        await user.click(screen.getByRole('button', { name: 'Clear API Key' }));
        await user.tab();

        expect(screen.getByLabelText('API Key')).toHaveAttribute('readonly');
        expect(screen.getByRole('button', { name: 'Clear API Key' })).toBeInTheDocument();
    });

    it('restores the masked state on remount without exposing a secret', () => {
        const { unmount } = render(<ControlledSecret configured initialValue="" />);

        unmount();
        render(<ControlledSecret configured initialValue="" />);

        const input = screen.getByLabelText('API Key') as HTMLInputElement;
        expect(input.value).toBe('');
        expect(input).toHaveAttribute('readonly');
        expect(screen.getByRole('button', { name: 'Clear API Key' })).toBeInTheDocument();
    });
});
