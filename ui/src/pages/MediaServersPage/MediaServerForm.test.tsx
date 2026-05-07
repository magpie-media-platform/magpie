import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { MemoryRouter } from 'react-router-dom';
import type { ReactNode } from 'react';

import { MediaServerForm } from './MediaServerForm';

function TestWrapper({ children }: { children: ReactNode }) {
    return <MemoryRouter>{children}</MemoryRouter>;
}

describe('MediaServerForm', () => {
    it('renders nothing when closed', () => {
        render(
            <TestWrapper>
                <MediaServerForm open={false} onOpenChange={vi.fn()} onSubmit={vi.fn()} />
            </TestWrapper>,
        );

        expect(screen.queryByText('Add Media Server')).not.toBeInTheDocument();
    });

    it('renders the form when open', () => {
        render(
            <TestWrapper>
                <MediaServerForm open={true} onOpenChange={vi.fn()} onSubmit={vi.fn()} />
            </TestWrapper>,
        );

        expect(screen.getByText('Add Media Server')).toBeInTheDocument();
    });

    it('shows "Edit Media Server" when editing', () => {
        render(
            <TestWrapper>
                <MediaServerForm
                    open={true}
                    onOpenChange={vi.fn()}
                    initialValues={{
                        id: 'server-1',
                        name: 'My Server',
                        provider: 'plex',
                        enabled: true,
                        settings: {},
                    }}
                    onSubmit={vi.fn()}
                />
            </TestWrapper>,
        );

        expect(screen.getByText('Edit Media Server')).toBeInTheDocument();
    });

    it('pre-fills form fields when editing', () => {
        render(
            <TestWrapper>
                <MediaServerForm
                    open={true}
                    onOpenChange={vi.fn()}
                    initialValues={{
                        id: 'server-1',
                        name: 'My Test Server',
                        provider: 'plex',
                        enabled: true,
                        settings: {},
                    }}
                    onSubmit={vi.fn()}
                />
            </TestWrapper>,
        );

        expect(screen.getByDisplayValue('My Test Server')).toBeInTheDocument();
        expect(screen.getByDisplayValue('plex')).toBeInTheDocument();
    });

    it('renders name input field', () => {
        render(
            <TestWrapper>
                <MediaServerForm open={true} onOpenChange={vi.fn()} onSubmit={vi.fn()} />
            </TestWrapper>,
        );

        expect(screen.getByLabelText(/name/i)).toBeInTheDocument();
    });

    it('renders provider type input', () => {
        render(
            <TestWrapper>
                <MediaServerForm open={true} onOpenChange={vi.fn()} onSubmit={vi.fn()} />
            </TestWrapper>,
        );

        expect(screen.getByLabelText(/provider type/i)).toBeInTheDocument();
        expect(screen.getByPlaceholderText('plex')).toBeInTheDocument();
        expect(screen.getByText('The media server type (e.g., plex, jellyfin, emby).')).toBeInTheDocument();
    });

    it('renders enabled toggle', () => {
        render(
            <TestWrapper>
                <MediaServerForm open={true} onOpenChange={vi.fn()} onSubmit={vi.fn()} />
            </TestWrapper>,
        );

        expect(screen.getByLabelText(/enabled/i)).toBeInTheDocument();
    });

    it('renders Save and Cancel buttons', () => {
        render(
            <TestWrapper>
                <MediaServerForm open={true} onOpenChange={vi.fn()} onSubmit={vi.fn()} />
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
                <MediaServerForm open={true} onOpenChange={vi.fn()} onSubmit={onSubmit} />
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
                <MediaServerForm open={true} onOpenChange={vi.fn()} onSubmit={onSubmit} />
            </TestWrapper>,
        );

        const nameInput = screen.getByLabelText(/name/i);
        await user.type(nameInput, 'Test Server');

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
                <MediaServerForm open={true} onOpenChange={vi.fn()} onSubmit={onSubmit} />
            </TestWrapper>,
        );

        const nameInput = screen.getByLabelText(/name/i);
        await user.type(nameInput, 'Test Server');

        const providerInput = screen.getByLabelText(/provider type/i);
        await user.type(providerInput, 'plex');

        const saveButton = screen.getByRole('button', { name: /save/i });
        await user.click(saveButton);

        expect(onSubmit).toHaveBeenCalledTimes(1);
        expect(onSubmit).toHaveBeenCalledWith(
            expect.objectContaining({
                name: 'Test Server',
                provider: 'plex',
                enabled: true,
                settings: {},
            }),
        );
    });

    it('disables provider input when editing', () => {
        render(
            <TestWrapper>
                <MediaServerForm
                    open={true}
                    onOpenChange={vi.fn()}
                    initialValues={{
                        id: 'server-1',
                        name: 'My Server',
                        provider: 'plex',
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
                <MediaServerForm open={true} onOpenChange={vi.fn()} onSubmit={vi.fn()} isSubmitting={true} />
            </TestWrapper>,
        );

        const saveButton = screen.getByRole('button', { name: /save/i });
        expect(saveButton).toHaveAttribute('aria-busy', 'true');
        expect(saveButton).toBeDisabled();
    });

    it('disables form fields when isSubmitting is true', () => {
        render(
            <TestWrapper>
                <MediaServerForm open={true} onOpenChange={vi.fn()} onSubmit={vi.fn()} isSubmitting={true} />
            </TestWrapper>,
        );

        const nameInput = screen.getByLabelText(/name/i);
        expect(nameInput).toBeDisabled();

        const providerInput = document.querySelector('[data-field="provider"]');
        expect(providerInput).toBeDisabled();

        const saveButton = screen.getByRole('button', { name: /save/i });
        expect(saveButton).toBeDisabled();
    });

    it('resets form when opened with new values', async () => {
        const user = userEvent.setup();
        const onOpenChange = vi.fn();

        const { rerender } = render(
            <TestWrapper>
                <MediaServerForm open={true} onOpenChange={onOpenChange} onSubmit={vi.fn()} />
            </TestWrapper>,
        );

        const nameInput = screen.getByLabelText(/name/i);
        await user.type(nameInput, 'Test Name');

        rerender(
            <TestWrapper>
                <MediaServerForm open={false} onOpenChange={onOpenChange} onSubmit={vi.fn()} />
            </TestWrapper>,
        );

        rerender(
            <TestWrapper>
                <MediaServerForm
                    open={true}
                    onOpenChange={onOpenChange}
                    initialValues={{
                        id: 'server-2',
                        name: 'Different Server',
                        provider: 'jellyfin',
                        enabled: false,
                        settings: {},
                    }}
                    onSubmit={vi.fn()}
                />
            </TestWrapper>,
        );

        expect(screen.getByDisplayValue('Different Server')).toBeInTheDocument();
    });
});

describe('MediaServerForm accessibility', () => {
    it('has data-component attribute', () => {
        render(
            <TestWrapper>
                <MediaServerForm open={true} onOpenChange={vi.fn()} onSubmit={vi.fn()} />
            </TestWrapper>,
        );

        expect(document.querySelector('[data-component="media-server-form"]')).toBeInTheDocument();
    });

    it('has data-action attributes on buttons', () => {
        render(
            <TestWrapper>
                <MediaServerForm open={true} onOpenChange={vi.fn()} onSubmit={vi.fn()} />
            </TestWrapper>,
        );

        expect(document.querySelector('[data-action="cancel"]')).toBeInTheDocument();
        expect(document.querySelector('[data-action="save"]')).toBeInTheDocument();
    });

    it('has data-field attributes on form inputs', () => {
        render(
            <TestWrapper>
                <MediaServerForm open={true} onOpenChange={vi.fn()} onSubmit={vi.fn()} />
            </TestWrapper>,
        );

        expect(document.querySelector('[data-field="name"]')).toBeInTheDocument();
        expect(document.querySelector('[data-field="provider"]')).toBeInTheDocument();
        expect(document.querySelector('[data-field="enabled"]')).toBeInTheDocument();
    });

    it('displays error messages with role="alert"', async () => {
        const user = userEvent.setup();
        render(
            <TestWrapper>
                <MediaServerForm open={true} onOpenChange={vi.fn()} onSubmit={vi.fn()} />
            </TestWrapper>,
        );

        const saveButton = screen.getByRole('button', { name: /save/i });
        await user.click(saveButton);

        const alerts = screen.getAllByRole('alert');
        expect(alerts.length).toBeGreaterThan(0);
    });
});
