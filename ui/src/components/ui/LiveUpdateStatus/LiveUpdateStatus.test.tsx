import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';

import { LiveUpdateStatus } from './LiveUpdateStatus';
import { connectLiveUpdates, getLiveUpdateStatus } from '@/lib/liveUpdates';

describe('LiveUpdateStatus', () => {
    afterEach(() => vi.unstubAllGlobals());

    it('exposes unsupported details through desktop focus without replacing page content', () => {
        vi.stubGlobal('SharedWorker', undefined);
        connectLiveUpdates('', vi.fn());
        render(<LiveUpdateStatus />);
        const button = screen.getByRole('button', { name: /not supported/ });
        fireEvent.focus(button);
        expect(screen.getByRole('dialog', { name: 'Live updates' })).toBeInTheDocument();
        expect(
            screen.getByText('Live updates are not supported in this browser. Reload the page to see changes.'),
        ).toBeVisible();
        expect(button).toHaveAttribute('data-state', 'unsupported');
    });

    it('uses a modal on mobile and returns focus after dismissal', async () => {
        vi.stubGlobal('innerWidth', 390);
        vi.stubGlobal('SharedWorker', undefined);
        connectLiveUpdates('', vi.fn());
        render(<LiveUpdateStatus />);
        const button = screen.getByRole('button', { name: /not supported/ });
        await userEvent.click(button);
        expect(screen.getByRole('dialog', { name: 'Live updates' })).toHaveAttribute('data-dialog-content');
        await userEvent.click(screen.getByRole('button', { name: 'Close' }));
        expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
        await waitFor(() => expect(button).toHaveFocus());
        expect(getLiveUpdateStatus().state).toBe('unsupported');
    });
});
