import { render, screen, fireEvent } from '@testing-library/react';
import { describe, expect, it, vi, beforeEach } from 'vitest';

import { ExtensionLoadFailureBanner } from './index';

const mockUseExtensions = vi.fn();

vi.mock('@/extensions/useExtensions', () => ({
    useExtensions: () => mockUseExtensions(),
}));

describe('ExtensionLoadFailureBanner', () => {
    beforeEach(() => {
        vi.clearAllMocks();
    });

    it('renders nothing while the loader has not failed terminally', () => {
        mockUseExtensions.mockReturnValue({ isError: false, error: null, retry: vi.fn() });

        render(<ExtensionLoadFailureBanner />);

        expect(screen.queryByRole('alert')).not.toBeInTheDocument();
    });

    it('reports the failure once and retries the loader', () => {
        const retry = vi.fn().mockResolvedValue(undefined);
        mockUseExtensions.mockReturnValue({ isError: true, error: 'Manifest unavailable', retry });

        render(<ExtensionLoadFailureBanner />);
        fireEvent.click(screen.getByRole('button', { name: 'Retry extensions' }));

        expect(screen.getAllByRole('alert')).toHaveLength(1);
        expect(screen.getByRole('alert')).toHaveTextContent('Manifest unavailable');
        expect(retry).toHaveBeenCalledTimes(1);
    });
});
