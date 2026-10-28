import { render, screen } from '@testing-library/react';
import { describe, expect, it, vi, beforeEach } from 'vitest';
import { MemoryRouter } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import type { ReactNode } from 'react';

import { AboutPage } from './AboutPage';

const mockUseExtensions = vi.fn();
const mockUseSlotExtensions = vi.fn();

vi.mock('@/extensions/useExtensions', () => ({
    useExtensions: () => mockUseExtensions(),
    useSlotExtensions: (slot: string) => mockUseSlotExtensions(slot),
}));

const mockUseSystemInfo = vi.fn();

vi.mock('@/hooks/useSystemStatus', () => ({
    useSystemInfo: () => mockUseSystemInfo(),
}));

vi.mock('@/lib/formatTime', () => ({
    formatUptime: (startedAt?: string | null) => (startedAt ? '2d 5h 30m' : '-'),
}));

const mockSystemInfo = {
    version: '0.1.0',
    runtime: '.NET 10.0.0',
    operatingSystem: 'Windows 11',
    architecture: 'x64',
    systemRoot: 'C:\\ProgramData\\Magpie',
    nodeId: 'node-1',
    startedAt: '2025-01-13T05:00:00Z',
};

function createWrapper() {
    const queryClient = new QueryClient({
        defaultOptions: {
            queries: {
                retry: false,
                gcTime: 0,
            },
        },
    });

    return function Wrapper({ children }: { children: ReactNode }) {
        return (
            <QueryClientProvider client={queryClient}>
                <MemoryRouter>{children}</MemoryRouter>
            </QueryClientProvider>
        );
    };
}

describe('AboutPage', () => {
    const defaultContext = {
        apiBaseUrl: '/api',
        userId: 'user-123',
        username: 'testuser',
        role: 'Admin',
    };

    const noExtensions = {
        extensions: [],
        isReady: true,
        isLoading: false,
    };

    beforeEach(() => {
        vi.clearAllMocks();
        mockUseExtensions.mockReturnValue(defaultContext);
        mockUseSlotExtensions.mockReturnValue(noExtensions);
        mockUseSystemInfo.mockReturnValue({
            data: mockSystemInfo,
            isLoading: false,
            error: null,
        });
    });

    function renderAboutPage() {
        return render(<AboutPage />, { wrapper: createWrapper() });
    }

    it('renders the about page with correct data-page attribute', () => {
        renderAboutPage();

        expect(document.querySelector('[data-page="about"]')).toBeInTheDocument();
    });

    it('renders the page title and tagline', () => {
        renderAboutPage();

        expect(screen.getByRole('heading', { name: 'Magpie', level: 1 })).toBeInTheDocument();
        expect(screen.getByText('Unified media automation platform')).toBeInTheDocument();
    });

    it('renders version badge', () => {
        renderAboutPage();

        expect(screen.getByText(/v0\.1\.0/)).toBeInTheDocument();
    });

    it('shows loading skeletons when data is loading', () => {
        mockUseSystemInfo.mockReturnValue({
            data: undefined,
            isLoading: true,
            error: null,
        });

        renderAboutPage();

        expect(screen.queryByText(/v0\.1\.0/)).not.toBeInTheDocument();
        expect(screen.getByText('Uptime').querySelector('svg')).toBeInTheDocument();
    });

    describe('System Information Section', () => {
        it('renders section heading', () => {
            renderAboutPage();

            expect(screen.getByRole('heading', { name: 'System Information', level: 2 })).toBeInTheDocument();
        });

        it('displays runtime information', () => {
            renderAboutPage();

            expect(screen.getByText('Runtime')).toBeInTheDocument();
            expect(screen.getByText('.NET 10.0.0')).toBeInTheDocument();
        });

        it('displays operating system', () => {
            renderAboutPage();

            expect(screen.getByText('Operating System')).toBeInTheDocument();
            expect(screen.getByText('Windows 11')).toBeInTheDocument();
        });

        it('displays architecture', () => {
            renderAboutPage();

            expect(screen.getByText('Architecture')).toBeInTheDocument();
            expect(screen.getByText('x64')).toBeInTheDocument();
        });

        it('displays uptime', () => {
            renderAboutPage();

            expect(screen.getByText('Uptime')).toBeInTheDocument();
            expect(screen.getByText('2d 5h 30m')).toBeInTheDocument();
        });
    });

    describe('Links Section', () => {
        it('renders section heading', () => {
            renderAboutPage();

            expect(screen.getByRole('heading', { name: /resources/i, level: 2 })).toBeInTheDocument();
        });

        it('renders GitHub link', () => {
            renderAboutPage();

            const githubLink = screen.getByRole('link', { name: /github/i });
            expect(githubLink).toBeInTheDocument();
            expect(githubLink).toHaveAttribute('href', 'https://github.com/magpie-media-platform/magpie');
            expect(githubLink).toHaveAttribute('target', '_blank');
            expect(githubLink).toHaveAttribute('rel', 'noopener noreferrer');
        });

        it('renders Documentation link', () => {
            renderAboutPage();

            const docsLink = screen.getByRole('link', { name: /documentation/i });
            expect(docsLink).toBeInTheDocument();
            expect(docsLink).toHaveAttribute(
                'href',
                'https://github.com/magpie-media-platform/magpie/blob/main/docs/README.md',
            );
        });

        it('renders Report a Bug link', () => {
            renderAboutPage();

            const bugLink = screen.getByRole('link', { name: /report a bug/i });
            expect(bugLink).toBeInTheDocument();
            expect(bugLink).toHaveAttribute('href', 'https://github.com/magpie-media-platform/magpie/issues/new');
        });

        it('displays link descriptions', () => {
            renderAboutPage();

            expect(screen.getByText('Source code, issues, and releases')).toBeInTheDocument();
            expect(screen.getByText('Installation guides and API reference')).toBeInTheDocument();
            expect(screen.getByText('Report issues or request features')).toBeInTheDocument();
        });
    });

    describe('License Section', () => {
        it('renders section heading', () => {
            renderAboutPage();

            expect(screen.getByRole('heading', { name: 'License', level: 2 })).toBeInTheDocument();
        });

        it('displays MIT License link', () => {
            renderAboutPage();

            const licenseLink = screen.getByRole('link', { name: /mit license/i });
            expect(licenseLink).toBeInTheDocument();
            expect(licenseLink).toHaveAttribute('href', 'https://opensource.org/licenses/MIT');
        });

        it('renders the license sentence with no space before the trailing period', () => {
            renderAboutPage();

            const licenseLink = screen.getByRole('link', { name: /mit license/i });
            const sentence = licenseLink.parentElement?.textContent ?? '';
            expect(sentence).toContain('MIT License.');
            expect(sentence).not.toContain('MIT License .');
        });

        it('displays copyright information with a dynamic end year', () => {
            renderAboutPage();

            const currentYear = new Date().getFullYear();
            expect(screen.getByText(new RegExp(`copyright 2024-${currentYear}`, 'i'))).toBeInTheDocument();
        });
    });

    describe('Accessibility', () => {
        it('has properly labeled sections', () => {
            renderAboutPage();

            expect(screen.getByRole('region', { name: /system information/i })).toBeInTheDocument();
            expect(screen.getByRole('region', { name: /resources/i })).toBeInTheDocument();
            expect(screen.getByRole('region', { name: /license/i })).toBeInTheDocument();
        });

        it('has decorative icons marked as aria-hidden', () => {
            renderAboutPage();

            const logoIcon = screen.getByTestId('about-page__logo-icon');
            expect(logoIcon).toHaveAttribute('aria-hidden', 'true');

            const linkIcons = screen.getAllByTestId('about-page__link-icon');
            linkIcons.forEach((icon) => {
                expect(icon).toHaveAttribute('aria-hidden', 'true');
            });
        });

        it('external links have proper attributes', () => {
            renderAboutPage();

            const externalLinks = screen.getAllByTestId('about-page__link-card');
            externalLinks.forEach((link) => {
                expect(link).toHaveAttribute('target', '_blank');
                expect(link).toHaveAttribute('rel', 'noopener noreferrer');
            });
        });
    });
});
