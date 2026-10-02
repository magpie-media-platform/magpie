import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, useLocation } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { useState } from 'react';
import userEvent from '@testing-library/user-event';

import { apiGet, apiPost, ApiError } from '@/lib/api';
import { LocalContentLink, LocalContentPreviewProvider } from './LocalContentPreview';
import { Dialog, DialogContent, DialogTitle, DialogDescription } from '@/components/ui/Dialog';
import { TooltipProvider } from '@/components/ui/Tooltip';

vi.mock('@/hooks/useLibraryTypes', () => ({ useLibraryTypes: () => ({ subjectTypes: [] }) }));
vi.mock('@/lib/api', async (original) => ({
    ...(await original<typeof import('@/lib/api')>()),
    apiGet: vi.fn(),
    apiPost: vi.fn(),
}));

function LocationLabel() {
    const location = useLocation();
    return (
        <>
            <output data-testid="location">
                {location.pathname}
                {location.search}
            </output>
            <output data-testid="location-state">{JSON.stringify(location.state)}</output>
        </>
    );
}

function PendingDownload() {
    const [open, setOpen] = useState(true);
    return (
        <Dialog open={open} onOpenChange={setOpen}>
            <DialogContent>
                <DialogTitle>Download</DialogTitle>
                <DialogDescription>Pending download work</DialogDescription>
                <input aria-label="Pending value" defaultValue="keep" />
                <LocalContentLink contentId="local">Inspect item</LocalContentLink>
            </DialogContent>
        </Dialog>
    );
}

describe('local preview workflow', () => {
    beforeEach(() => vi.resetAllMocks());

    it('does not display the previous item when an earlier request resolves after switching targets', async () => {
        // Arrange
        let completeFirst!: (value: unknown) => void;
        const first = new Promise((resolve) => {
            completeFirst = resolve;
        });
        vi.mocked(apiGet)
            .mockReturnValueOnce(first)
            .mockResolvedValueOnce({ title: 'First title' })
            .mockResolvedValueOnce({ id: 'second', name: 'Second', contentType: 'movie', level: 'movie' })
            .mockResolvedValueOnce({ title: 'Second title' });
        render(
            <QueryClientProvider client={new QueryClient()}>
                <MemoryRouter>
                    <LocalContentPreviewProvider>
                        <LocalContentLink contentId="first">First</LocalContentLink>
                        <LocalContentLink contentId="second">Second</LocalContentLink>
                    </LocalContentPreviewProvider>
                </MemoryRouter>
            </QueryClientProvider>,
            { wrapper: TooltipProvider },
        );
        const second = screen.getByRole('link', { name: 'Second' });
        // Act
        fireEvent.click(screen.getByRole('link', { name: 'First' }));
        expect(screen.getByTestId('preview-drawer-loading')).toBeInTheDocument();
        fireEvent.click(second);
        await screen.findByText('Second title');
        completeFirst({ id: 'first', name: 'First', contentType: 'movie', level: 'movie' });
        // Assert
        await waitFor(() => expect(screen.queryByText('First title')).not.toBeInTheDocument());
        expect(screen.getByText('Second title')).toBeInTheDocument();
    });

    it('restores the invoking link and pending work in an existing dialog', async () => {
        // Arrange
        const user = userEvent.setup();
        vi.mocked(apiGet).mockImplementation((url) =>
            Promise.resolve(
                url.endsWith('/metadata')
                    ? { title: 'Local title' }
                    : { id: 'local', name: 'Local', contentType: 'movie', level: 'movie' },
            ),
        );
        render(
            <QueryClientProvider client={new QueryClient()}>
                <MemoryRouter initialEntries={['/downloads?status=active']}>
                    <LocalContentPreviewProvider>
                        <PendingDownload />
                        <LocationLabel />
                    </LocalContentPreviewProvider>
                </MemoryRouter>
            </QueryClientProvider>,
            { wrapper: TooltipProvider },
        );
        const trigger = screen.getByRole('link', { name: 'Inspect item' });
        // Act
        await user.type(screen.getByRole('textbox', { name: 'Pending value' }), ' this input');
        await user.click(trigger);
        await screen.findByText('Local title');
        await user.click(screen.getByRole('button', { name: 'Close' }));
        // Assert
        await waitFor(() => expect(trigger).toHaveFocus());
        expect(screen.getByRole('dialog', { name: 'Download' })).toBeInTheDocument();
        expect(screen.getByRole('textbox', { name: 'Pending value' })).toHaveValue('keep this input');
        expect(screen.getByTestId('location')).toHaveTextContent('/downloads?status=active');
        // Act
        await user.click(trigger);
        await user.click(await screen.findByRole('button', { name: 'Open Local title' }));
        // Assert
        await waitFor(() => expect(screen.getByTestId('location')).toHaveTextContent('/library/local'));
        expect(screen.getByTestId('location-state')).toHaveTextContent(
            '{"contentOrigin":{"pathname":"/downloads","search":"?status=active","hash":"","state":null}}',
        );
    });
    it('preserves the source query and input, restores focus, and navigates only from the explicit action', async () => {
        // Arrange
        vi.mocked(apiGet)
            .mockResolvedValueOnce({ id: 'local', name: 'Fallback', contentType: 'movie', level: 'movie' })
            .mockResolvedValueOnce({ title: 'Local title', overview: 'Local overview' });
        const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
        render(
            <QueryClientProvider client={client}>
                <MemoryRouter initialEntries={['/wanted?page=3&filter=missing']}>
                    <LocalContentPreviewProvider>
                        <input aria-label="Pending work" defaultValue="keep me" />
                        <LocalContentLink contentId="local">Inspect item</LocalContentLink>
                        <LocationLabel />
                    </LocalContentPreviewProvider>
                </MemoryRouter>
            </QueryClientProvider>,
            { wrapper: TooltipProvider },
        );
        // Act
        const trigger = screen.getByRole('link', { name: 'Inspect item' });
        fireEvent.click(trigger);
        await screen.findByText('Local overview');
        // Assert
        expect(screen.getByTestId('location')).toHaveTextContent('/wanted?page=3&filter=missing');
        expect(apiPost).not.toHaveBeenCalled();
        // Act
        fireEvent.click(screen.getByRole('button', { name: 'Close' }));
        // Assert
        await waitFor(() => expect(trigger).toHaveFocus());
        expect(screen.getByRole('textbox', { name: 'Pending work' })).toHaveValue('keep me');
        // Arrange
        vi.mocked(apiGet)
            .mockResolvedValueOnce({ id: 'local', name: 'Fallback', contentType: 'movie', level: 'movie' })
            .mockResolvedValueOnce({ title: 'Local title' });
        // Act
        fireEvent.click(trigger);
        fireEvent.click(await screen.findByRole('button', { name: 'Open Local title' }));
        // Assert
        await waitFor(() => expect(screen.getByTestId('location')).toHaveTextContent('/library/local'));
        expect(screen.getByTestId('location-state')).toHaveTextContent(
            '{"contentOrigin":{"pathname":"/wanted","search":"?page=3&filter=missing","hash":"","state":null}}',
        );
    });

    it('shows the full open label in a tooltip when the ellipsis hides part of it', async () => {
        // Arrange
        vi.mocked(apiGet)
            .mockResolvedValueOnce({ id: 'local', name: 'Fallback', contentType: 'movie', level: 'movie' })
            .mockResolvedValueOnce({ title: 'A Very Long Title That Does Not Fit On One Line Of The Drawer Footer' });
        vi.spyOn(HTMLElement.prototype, 'scrollWidth', 'get').mockReturnValue(300);
        vi.spyOn(HTMLElement.prototype, 'clientWidth', 'get').mockReturnValue(120);
        const user = userEvent.setup();
        render(
            <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
                <MemoryRouter>
                    <LocalContentPreviewProvider>
                        <LocalContentLink contentId="local">Inspect item</LocalContentLink>
                    </LocalContentPreviewProvider>
                </MemoryRouter>
            </QueryClientProvider>,
            { wrapper: TooltipProvider },
        );
        fireEvent.click(screen.getByRole('link', { name: 'Inspect item' }));
        const open = await screen.findByRole('button', {
            name: 'Open A Very Long Title That Does Not Fit On One Line Of The Drawer Footer',
        });

        // Act
        await user.hover(open);

        // Assert
        expect(open.querySelector('.preview-drawer__open-label')).toHaveTextContent(
            'Open A Very Long Title That Does Not Fit On One Line Of The Drawer Footer',
        );
        expect(await screen.findByRole('tooltip')).toHaveTextContent(
            'Open A Very Long Title That Does Not Fit On One Line Of The Drawer Footer',
        );
        vi.restoreAllMocks();
    });

    it('shows no tooltip for the open label when the ellipsis hides nothing', async () => {
        // Arrange
        vi.useFakeTimers({ shouldAdvanceTime: true });
        vi.mocked(apiGet)
            .mockResolvedValueOnce({ id: 'local', name: 'Fallback', contentType: 'movie', level: 'movie' })
            .mockResolvedValueOnce({ title: 'A Very Long Title That Does Not Fit On One Line Of The Drawer Footer' });
        vi.spyOn(HTMLElement.prototype, 'scrollWidth', 'get').mockReturnValue(120);
        vi.spyOn(HTMLElement.prototype, 'clientWidth', 'get').mockReturnValue(120);
        const user = userEvent.setup({ advanceTimers: vi.advanceTimersByTime });
        render(
            <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
                <MemoryRouter>
                    <LocalContentPreviewProvider>
                        <LocalContentLink contentId="local">Inspect item</LocalContentLink>
                    </LocalContentPreviewProvider>
                </MemoryRouter>
            </QueryClientProvider>,
            { wrapper: TooltipProvider },
        );
        fireEvent.click(screen.getByRole('link', { name: 'Inspect item' }));
        const open = await screen.findByRole('button', {
            name: 'Open A Very Long Title That Does Not Fit On One Line Of The Drawer Footer',
        });

        // Act
        await user.hover(open);
        await vi.advanceTimersByTimeAsync(300);

        // Assert
        expect(open.querySelector('.preview-drawer__open-label')).toHaveTextContent(
            'Open A Very Long Title That Does Not Fit On One Line Of The Drawer Footer',
        );
        expect(screen.queryByRole('tooltip')).not.toBeInTheDocument();
        vi.useRealTimers();
        vi.restoreAllMocks();
    });

    it('shows an unavailable item without a provider fallback or enabled detail navigation', async () => {
        // Arrange
        vi.mocked(apiGet).mockRejectedValue(new ApiError(404, 'Missing'));
        render(
            <QueryClientProvider client={new QueryClient()}>
                <MemoryRouter>
                    <LocalContentPreviewProvider>
                        <LocalContentLink contentId="missing">Inspect missing</LocalContentLink>
                    </LocalContentPreviewProvider>
                </MemoryRouter>
            </QueryClientProvider>,
            { wrapper: TooltipProvider },
        );
        // Act
        fireEvent.click(screen.getByRole('link'));
        // Assert
        expect(await screen.findByText('Item unavailable')).toBeInTheDocument();
        expect(document.querySelector('[data-action="open-content"]')).toBeDisabled();
        expect(apiPost).not.toHaveBeenCalled();
    });
});
