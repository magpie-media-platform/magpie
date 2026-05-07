import { describe, expect, it } from 'vitest';
import { render, screen, act } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import { RestartRequiredBanner, RestartRequiredProvider, useRestartRequired } from './index';

function Trigger() {
    const { setRestartRequired } = useRestartRequired();
    return (
        <button type="button" onClick={() => setRestartRequired(true)}>
            mark
        </button>
    );
}

describe('RestartRequiredBanner', () => {
    it('renders nothing when restart is not required', () => {
        render(
            <RestartRequiredProvider>
                <RestartRequiredBanner />
            </RestartRequiredProvider>,
        );

        expect(screen.queryByText(/restart required/i)).not.toBeInTheDocument();
    });

    it('renders the alert when restart is required', async () => {
        const user = userEvent.setup();

        render(
            <RestartRequiredProvider>
                <Trigger />
                <RestartRequiredBanner />
            </RestartRequiredProvider>,
        );

        await user.click(screen.getByRole('button', { name: 'mark' }));

        expect(screen.getByText(/restart required/i)).toBeInTheDocument();
    });

    it('hides the banner when dismissed but reappears on a fresh signal', async () => {
        const user = userEvent.setup();

        function Refresher() {
            const { setRestartRequired } = useRestartRequired();
            return (
                <button type="button" onClick={() => setRestartRequired(true)}>
                    re-mark
                </button>
            );
        }

        render(
            <RestartRequiredProvider>
                <Trigger />
                <Refresher />
                <RestartRequiredBanner />
            </RestartRequiredProvider>,
        );

        await user.click(screen.getByRole('button', { name: 'mark' }));
        expect(screen.getByText(/restart required/i)).toBeInTheDocument();

        await user.click(screen.getByRole('button', { name: /dismiss alert/i }));
        expect(screen.queryByText(/restart required/i)).not.toBeInTheDocument();

        await act(async () => {
            await user.click(screen.getByRole('button', { name: 're-mark' }));
        });

        expect(screen.getByText(/restart required/i)).toBeInTheDocument();
    });

    it('throws when useRestartRequired is used outside its provider', () => {
        function Consumer() {
            useRestartRequired();
            return null;
        }

        // React logs error boundaries here; we just want the throw.
        expect(() => render(<Consumer />)).toThrow(/RestartRequiredProvider/);
    });
});
