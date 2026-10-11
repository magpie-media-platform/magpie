import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';

import { EventContextFilterRow } from './EventContextFilterRow';

describe('EventContextFilterRow', () => {
    it('Inputs_ShowKeyAndPattern_WhenFilterIsGiven', () => {
        // Arrange
        const filter = { key: 'contentType', pattern: 'movie', negate: true };

        // Act
        render(
            <EventContextFilterRow
                index={1}
                filter={filter}
                onKeyChange={vi.fn()}
                onPatternChange={vi.fn()}
                onNegateChange={vi.fn()}
                onRemove={vi.fn()}
            />,
        );

        // Assert
        expect(screen.getByLabelText('Filter 2 key')).toHaveValue('contentType');
        expect(screen.getByLabelText('Filter 2 pattern')).toHaveValue('movie');
        expect(screen.getByRole('switch', { name: 'Filter 2 negate' })).toBeChecked();
    });

    it('Inputs_ShowErrors_WhenKeyAndPatternErrorsAreGiven', () => {
        // Arrange
        const filter = { key: '', pattern: '(', negate: false };

        // Act
        render(
            <EventContextFilterRow
                index={1}
                filter={filter}
                keyError="Key is required"
                patternError="Invalid regex"
                onKeyChange={vi.fn()}
                onPatternChange={vi.fn()}
                onNegateChange={vi.fn()}
                onRemove={vi.fn()}
            />,
        );

        // Assert
        expect(screen.getByLabelText('Filter 2 key')).toBeInvalid();
        expect(screen.getByLabelText('Filter 2 pattern')).toBeInvalid();
        expect(screen.getByText('Key is required')).toBeInTheDocument();
        expect(screen.getByText('Invalid regex')).toBeInTheDocument();
    });

    it('Handlers_ReceiveRowIndex_WhenControlsAreUsed', async () => {
        // Arrange
        const user = userEvent.setup();
        const onKeyChange = vi.fn();
        const onPatternChange = vi.fn();
        const onNegateChange = vi.fn();
        const onRemove = vi.fn();
        render(
            <EventContextFilterRow
                index={1}
                filter={{ key: '', pattern: '', negate: false }}
                onKeyChange={onKeyChange}
                onPatternChange={onPatternChange}
                onNegateChange={onNegateChange}
                onRemove={onRemove}
            />,
        );

        // Act
        await user.type(screen.getByLabelText('Filter 2 key'), 'a');
        await user.type(screen.getByLabelText('Filter 2 pattern'), 'b');
        await user.click(screen.getByRole('switch', { name: 'Filter 2 negate' }));
        await user.click(screen.getByRole('button', { name: 'Remove filter 2' }));

        // Assert
        expect(onKeyChange).toHaveBeenCalledWith(1, 'a');
        expect(onPatternChange).toHaveBeenCalledWith(1, 'b');
        expect(onNegateChange).toHaveBeenCalledWith(1, true);
        expect(onRemove).toHaveBeenCalledWith(1);
    });

    it('Controls_AreDisabled_WhenRowIsDisabled', () => {
        // Arrange
        const filter = { key: 'contentType', pattern: 'movie', negate: false };

        // Act
        render(
            <EventContextFilterRow
                index={1}
                filter={filter}
                disabled
                onKeyChange={vi.fn()}
                onPatternChange={vi.fn()}
                onNegateChange={vi.fn()}
                onRemove={vi.fn()}
            />,
        );

        // Assert
        expect(screen.getByLabelText('Filter 2 key')).toBeDisabled();
        expect(screen.getByLabelText('Filter 2 pattern')).toBeDisabled();
        expect(screen.getByRole('switch', { name: 'Filter 2 negate' })).toBeDisabled();
        expect(screen.getByRole('button', { name: 'Remove filter 2' })).toBeDisabled();
    });
});
