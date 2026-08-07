import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';

import { AddAnotherActions } from './AddAnotherActions';

describe('AddAnotherActions', () => {
    it('renders a capitalized confirmation and labelled actions', () => {
        render(
            <AddAnotherActions label="root folder" action="root-folder" onAddAnother={vi.fn()} onContinue={vi.fn()} />,
        );

        expect(screen.getByText(/root folder added\. add another or continue\./i)).toBeInTheDocument();
        expect(screen.getByRole('button', { name: /add another root folder/i })).toBeInTheDocument();
        expect(screen.getByRole('button', { name: 'Continue' })).toBeInTheDocument();
    });

    it('invokes the matching handler for each action', async () => {
        const onAddAnother = vi.fn();
        const onContinue = vi.fn();
        const user = userEvent.setup();
        render(
            <AddAnotherActions label="indexer" action="indexer" onAddAnother={onAddAnother} onContinue={onContinue} />,
        );

        await user.click(screen.getByRole('button', { name: /add another indexer/i }));
        expect(onAddAnother).toHaveBeenCalledTimes(1);
        expect(onContinue).not.toHaveBeenCalled();

        await user.click(screen.getByRole('button', { name: 'Continue' }));
        expect(onContinue).toHaveBeenCalledTimes(1);
    });
});
