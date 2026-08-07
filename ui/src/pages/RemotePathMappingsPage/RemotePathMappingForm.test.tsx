import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { MemoryRouter } from 'react-router-dom';
import type { ReactNode } from 'react';

import { RemotePathMappingForm, type RemotePathMappingFormValues } from './RemotePathMappingForm';

function TestWrapper({ children }: { children: ReactNode }) {
    return <MemoryRouter>{children}</MemoryRouter>;
}

const editValues: RemotePathMappingFormValues = {
    id: 'mapping-1',
    host: 'downloadbox.example.test',
    remotePath: 'E:\\Transmission',
    localPath: '\\\\downloadbox.example.test\\share\\Downloads',
};

describe('RemotePathMappingForm', () => {
    beforeEach(() => {
        vi.clearAllMocks();
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('renders with correct data-component attribute when open', () => {
        render(
            <TestWrapper>
                <RemotePathMappingForm open={true} onOpenChange={() => {}} onSubmit={() => {}} />
            </TestWrapper>,
        );

        expect(document.querySelector('[data-component="remote-path-mapping-form"]')).toBeInTheDocument();
    });

    it('does not render content when closed', () => {
        render(
            <TestWrapper>
                <RemotePathMappingForm open={false} onOpenChange={() => {}} onSubmit={() => {}} />
            </TestWrapper>,
        );

        expect(document.querySelector('[data-component="remote-path-mapping-form"]')).not.toBeInTheDocument();
    });

    it('renders "Add Remote Path Mapping" title when adding new', () => {
        render(
            <TestWrapper>
                <RemotePathMappingForm open={true} onOpenChange={() => {}} onSubmit={() => {}} />
            </TestWrapper>,
        );

        expect(screen.getByText('Add Remote Path Mapping')).toBeInTheDocument();
    });

    it('renders "Edit Remote Path Mapping" title and prefilled values when editing', () => {
        render(
            <TestWrapper>
                <RemotePathMappingForm
                    open={true}
                    onOpenChange={() => {}}
                    initialValues={editValues}
                    onSubmit={() => {}}
                />
            </TestWrapper>,
        );

        expect(screen.getByText('Edit Remote Path Mapping')).toBeInTheDocument();
        expect(screen.getByDisplayValue('downloadbox.example.test')).toBeInTheDocument();
        expect(screen.getByDisplayValue('E:\\Transmission')).toBeInTheDocument();
        expect(screen.getByDisplayValue('\\\\downloadbox.example.test\\share\\Downloads')).toBeInTheDocument();
    });

    it('shows validation errors when required fields are empty', async () => {
        const user = userEvent.setup();
        const onSubmit = vi.fn();

        render(
            <TestWrapper>
                <RemotePathMappingForm open={true} onOpenChange={() => {}} onSubmit={onSubmit} />
            </TestWrapper>,
        );

        await user.click(screen.getByRole('button', { name: /save/i }));

        await waitFor(() => {
            expect(screen.getByText('Host is required')).toBeInTheDocument();
            expect(screen.getByText('Remote path is required')).toBeInTheDocument();
            expect(screen.getByText('Local path is required')).toBeInTheDocument();
        });
        expect(onSubmit).not.toHaveBeenCalled();
    });

    it('submits trimmed values when valid', async () => {
        const user = userEvent.setup();
        const onSubmit = vi.fn();

        render(
            <TestWrapper>
                <RemotePathMappingForm open={true} onOpenChange={() => {}} onSubmit={onSubmit} />
            </TestWrapper>,
        );

        await user.type(document.querySelector('[data-field="host"]')!, '  nas.local  ');
        await user.type(document.querySelector('[data-field="remote-path"]')!, '/volume1/torrents');
        await user.type(document.querySelector('[data-field="local-path"]')!, '/media/torrents');

        await user.click(screen.getByRole('button', { name: /save/i }));

        await waitFor(() => {
            expect(onSubmit).toHaveBeenCalledWith({
                host: 'nas.local',
                remotePath: '/volume1/torrents',
                localPath: '/media/torrents',
            });
        });
    });

    it('normalizes host values before submitting', async () => {
        const user = userEvent.setup();
        const onSubmit = vi.fn();

        render(
            <TestWrapper>
                <RemotePathMappingForm open={true} onOpenChange={() => {}} onSubmit={onSubmit} />
            </TestWrapper>,
        );

        await user.type(document.querySelector('[data-field="host"]')!, 'http://nas.local:9091/qbt');
        await user.type(document.querySelector('[data-field="remote-path"]')!, '/volume1/torrents');
        await user.type(document.querySelector('[data-field="local-path"]')!, '/media/torrents');

        await user.click(screen.getByRole('button', { name: /save/i }));

        await waitFor(() => {
            expect(onSubmit).toHaveBeenCalledWith({
                host: 'nas.local',
                remotePath: '/volume1/torrents',
                localPath: '/media/torrents',
            });
        });
    });

    it('normalizes ipv6 url host values before submitting', async () => {
        const user = userEvent.setup();
        const onSubmit = vi.fn();

        render(
            <TestWrapper>
                <RemotePathMappingForm open={true} onOpenChange={() => {}} onSubmit={onSubmit} />
            </TestWrapper>,
        );

        fireEvent.change(document.querySelector('[data-field="host"]')!, {
            target: { value: 'http://[::1]:9091/qbt' },
        });
        await user.type(document.querySelector('[data-field="remote-path"]')!, '/volume1/torrents');
        await user.type(document.querySelector('[data-field="local-path"]')!, '/media/torrents');

        await user.click(screen.getByRole('button', { name: /save/i }));

        await waitFor(() => {
            expect(onSubmit).toHaveBeenCalledWith({
                host: '::1',
                remotePath: '/volume1/torrents',
                localPath: '/media/torrents',
            });
        });
    });

    it('shows a validation error when a url has no host', async () => {
        const user = userEvent.setup();
        const onSubmit = vi.fn();

        render(
            <TestWrapper>
                <RemotePathMappingForm open={true} onOpenChange={() => {}} onSubmit={onSubmit} />
            </TestWrapper>,
        );

        await user.type(document.querySelector('[data-field="host"]')!, 'http://');
        await user.type(document.querySelector('[data-field="remote-path"]')!, '/volume1/torrents');
        await user.type(document.querySelector('[data-field="local-path"]')!, '/media/torrents');

        await user.click(screen.getByRole('button', { name: /save/i }));

        await waitFor(() => {
            expect(screen.getByText('Host must include a hostname')).toBeInTheDocument();
        });
        expect(onSubmit).not.toHaveBeenCalled();
    });

    it('tests the trimmed local path when test path is clicked', async () => {
        const user = userEvent.setup();
        const onTestLocalPath = vi.fn().mockResolvedValue(undefined);

        render(
            <TestWrapper>
                <RemotePathMappingForm
                    open={true}
                    onOpenChange={() => {}}
                    onSubmit={() => {}}
                    onTestLocalPath={onTestLocalPath}
                />
            </TestWrapper>,
        );

        await user.type(document.querySelector('[data-field="local-path"]')!, '  /media/torrents  ');
        await user.click(screen.getByRole('button', { name: /test path/i }));

        await waitFor(() => {
            expect(onTestLocalPath).toHaveBeenCalledWith('/media/torrents');
        });
    });

    it('shows a validation error when test path is clicked with a blank local path', async () => {
        const user = userEvent.setup();
        const onTestLocalPath = vi.fn().mockResolvedValue(undefined);

        render(
            <TestWrapper>
                <RemotePathMappingForm
                    open={true}
                    onOpenChange={() => {}}
                    onSubmit={() => {}}
                    onTestLocalPath={onTestLocalPath}
                />
            </TestWrapper>,
        );

        await user.click(screen.getByRole('button', { name: /test path/i }));

        await waitFor(() => {
            expect(screen.getByText('Local path is required')).toBeInTheDocument();
        });
        expect(onTestLocalPath).not.toHaveBeenCalled();
    });
});
