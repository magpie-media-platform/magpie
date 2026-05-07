import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { MemoryRouter } from 'react-router-dom';
import type { ReactNode } from 'react';

import { DownloadClientForm } from './DownloadClientForm';

function TestWrapper({ children }: { children: ReactNode }) {
    return <MemoryRouter>{children}</MemoryRouter>;
}

describe('DownloadClientForm', () => {
    it('renders nothing when closed', () => {
        render(
            <TestWrapper>
                <DownloadClientForm open={false} onOpenChange={vi.fn()} onSubmit={vi.fn()} />
            </TestWrapper>,
        );

        expect(screen.queryByText('Add Download Client')).not.toBeInTheDocument();
    });

    it('renders the form when open', () => {
        render(
            <TestWrapper>
                <DownloadClientForm open={true} onOpenChange={vi.fn()} onSubmit={vi.fn()} />
            </TestWrapper>,
        );

        expect(screen.getByText('Add Download Client')).toBeInTheDocument();
    });

    it('shows "Edit Download Client" when editing', () => {
        render(
            <TestWrapper>
                <DownloadClientForm
                    open={true}
                    onOpenChange={vi.fn()}
                    initialValues={{
                        id: 'client-1',
                        name: 'My Client',
                        provider: 'sabnzbd',
                        enabled: true,
                        settings: {},
                    }}
                    onSubmit={vi.fn()}
                />
            </TestWrapper>,
        );

        expect(screen.getByText('Edit Download Client')).toBeInTheDocument();
    });

    it('pre-fills form fields when editing', () => {
        render(
            <TestWrapper>
                <DownloadClientForm
                    open={true}
                    onOpenChange={vi.fn()}
                    initialValues={{
                        id: 'client-1',
                        name: 'My Test Client',
                        provider: 'sabnzbd',
                        enabled: true,
                        settings: {},
                    }}
                    onSubmit={vi.fn()}
                />
            </TestWrapper>,
        );

        expect(screen.getByDisplayValue('My Test Client')).toBeInTheDocument();
        expect(screen.getByDisplayValue('sabnzbd')).toBeInTheDocument();
    });

    it('renders name input field', () => {
        render(
            <TestWrapper>
                <DownloadClientForm open={true} onOpenChange={vi.fn()} onSubmit={vi.fn()} />
            </TestWrapper>,
        );

        expect(screen.getByLabelText(/name/i)).toBeInTheDocument();
    });

    it('renders provider type input', () => {
        render(
            <TestWrapper>
                <DownloadClientForm open={true} onOpenChange={vi.fn()} onSubmit={vi.fn()} />
            </TestWrapper>,
        );

        expect(screen.getByLabelText(/provider type/i)).toBeInTheDocument();
        expect(screen.getByPlaceholderText('sabnzbd')).toBeInTheDocument();
        expect(screen.getByText('The download client type (e.g., sabnzbd, qbittorrent).')).toBeInTheDocument();
    });

    it('renders enabled toggle', () => {
        render(
            <TestWrapper>
                <DownloadClientForm open={true} onOpenChange={vi.fn()} onSubmit={vi.fn()} />
            </TestWrapper>,
        );

        expect(screen.getByLabelText(/enabled/i)).toBeInTheDocument();
    });

    it('renders Save and Cancel buttons', () => {
        render(
            <TestWrapper>
                <DownloadClientForm open={true} onOpenChange={vi.fn()} onSubmit={vi.fn()} />
            </TestWrapper>,
        );

        expect(screen.getByRole('button', { name: /save/i })).toBeInTheDocument();
        expect(screen.getByRole('button', { name: /cancel/i })).toBeInTheDocument();
    });

    it('shows validation error when name is empty on submit', async () => {
        const user = userEvent.setup();
        const onSubmit = vi.fn();
        render(
            <TestWrapper>
                <DownloadClientForm open={true} onOpenChange={vi.fn()} onSubmit={onSubmit} />
            </TestWrapper>,
        );

        const saveButton = screen.getByRole('button', { name: /save/i });
        await user.click(saveButton);

        expect(screen.getByText('Name is required')).toBeInTheDocument();
        expect(onSubmit).not.toHaveBeenCalled();
    });

    it('shows validation error when provider type is empty on submit', async () => {
        const user = userEvent.setup();
        const onSubmit = vi.fn();
        render(
            <TestWrapper>
                <DownloadClientForm open={true} onOpenChange={vi.fn()} onSubmit={onSubmit} />
            </TestWrapper>,
        );

        const nameInput = screen.getByLabelText(/name/i);
        await user.type(nameInput, 'Test Client');

        const saveButton = screen.getByRole('button', { name: /save/i });
        await user.click(saveButton);

        expect(screen.getByText('Provider type is required')).toBeInTheDocument();
        expect(onSubmit).not.toHaveBeenCalled();
    });

    it('calls onSubmit with form values when valid', async () => {
        const user = userEvent.setup();
        const onSubmit = vi.fn();
        render(
            <TestWrapper>
                <DownloadClientForm open={true} onOpenChange={vi.fn()} onSubmit={onSubmit} />
            </TestWrapper>,
        );

        const nameInput = screen.getByLabelText(/name/i);
        await user.type(nameInput, 'Test Client');

        const providerInput = screen.getByLabelText(/provider type/i);
        await user.type(providerInput, 'sabnzbd');

        const saveButton = screen.getByRole('button', { name: /save/i });
        await user.click(saveButton);

        expect(onSubmit).toHaveBeenCalledTimes(1);
        expect(onSubmit).toHaveBeenCalledWith(
            expect.objectContaining({
                name: 'Test Client',
                provider: 'sabnzbd',
                enabled: true,
                settings: {},
            }),
        );
    });

    it('disables provider input when editing', () => {
        render(
            <TestWrapper>
                <DownloadClientForm
                    open={true}
                    onOpenChange={vi.fn()}
                    initialValues={{
                        id: 'client-1',
                        name: 'My Client',
                        provider: 'sabnzbd',
                        enabled: true,
                        settings: {},
                    }}
                    onSubmit={vi.fn()}
                />
            </TestWrapper>,
        );

        const providerInput = document.querySelector('[data-field="provider"]');
        expect(providerInput).toBeDisabled();
    });

    it('shows loading state when isSubmitting is true', () => {
        render(
            <TestWrapper>
                <DownloadClientForm open={true} onOpenChange={vi.fn()} onSubmit={vi.fn()} isSubmitting={true} />
            </TestWrapper>,
        );

        const saveButton = screen.getByRole('button', { name: /save/i });
        expect(saveButton).toHaveAttribute('aria-busy', 'true');
        expect(saveButton).toBeDisabled();
    });

    it('disables form fields when isSubmitting is true', () => {
        render(
            <TestWrapper>
                <DownloadClientForm open={true} onOpenChange={vi.fn()} onSubmit={vi.fn()} isSubmitting={true} />
            </TestWrapper>,
        );

        const nameInput = screen.getByLabelText(/name/i);
        expect(nameInput).toBeDisabled();

        const providerInput = document.querySelector('[data-field="provider"]');
        expect(providerInput).toBeDisabled();

        const saveButton = screen.getByRole('button', { name: /save/i });
        expect(saveButton).toBeDisabled();
    });
});
