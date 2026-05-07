import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it } from 'vitest';

import { ServiceHealthBar, type ServiceHealthEntry } from './ServiceHealthBar';

const renderWithRouter = (ui: React.ReactElement) => {
    return render(<MemoryRouter>{ui}</MemoryRouter>);
};

const defaultServices: ServiceHealthEntry[] = [
    {
        name: 'Indexers',
        counts: { healthy: 3, unhealthy: 1, unknown: 0, stale: 0 },
        getStatusUrl: (status) => `/indexers?status=${status}`,
    },
    {
        name: 'Download Clients',
        counts: { healthy: 2, unhealthy: 0, unknown: 1, stale: 0 },
        getStatusUrl: (status) => `/download-clients?status=${status}`,
    },
    {
        name: 'Media Servers',
        counts: { healthy: 1, unhealthy: 0, unknown: 0, stale: 0 },
        getStatusUrl: (status) => `/media-servers?status=${status}`,
    },
];

describe('ServiceHealthBar', () => {
    it('renders service names', () => {
        renderWithRouter(<ServiceHealthBar services={defaultServices} />);

        expect(screen.getByText('Indexers')).toBeInTheDocument();
        expect(screen.getByText('Download Clients')).toBeInTheDocument();
        expect(screen.getByText('Media Servers')).toBeInTheDocument();
    });

    it('renders accessible heading', () => {
        renderWithRouter(<ServiceHealthBar services={defaultServices} />);

        expect(screen.getByRole('heading', { level: 2, name: 'Service Health' })).toBeInTheDocument();
    });

    it('uses custom heading level', () => {
        renderWithRouter(<ServiceHealthBar services={defaultServices} headingLevel={3} />);

        expect(screen.getByRole('heading', { level: 3, name: 'Service Health' })).toBeInTheDocument();
    });

    it('links unhealthy service to filtered page', () => {
        renderWithRouter(<ServiceHealthBar services={defaultServices} />);

        const link = screen.getByLabelText(/indexers: 1 unhealthy/i);
        expect(link.tagName).toBe('A');
        expect(link).toHaveAttribute('href', '/indexers?status=unhealthy');
    });

    it('shows summary label for healthy services', () => {
        renderWithRouter(<ServiceHealthBar services={defaultServices} />);

        expect(screen.getByText('1 OK')).toBeInTheDocument();
    });

    it('renders data-component attribute', () => {
        renderWithRouter(<ServiceHealthBar services={defaultServices} />);

        expect(document.querySelector('[data-component="service-health-bar"]')).toBeInTheDocument();
    });
});

describe('ServiceHealthBar zero-count state', () => {
    const zeroServices: ServiceHealthEntry[] = [
        {
            name: 'Indexers',
            counts: { healthy: 0, unhealthy: 0, unknown: 0, stale: 0 },
            addUrl: '/admin/indexers',
        },
        {
            name: 'Download Clients',
            counts: { healthy: 0, unhealthy: 0, unknown: 0, stale: 0 },
        },
    ];

    it('shows "Add first" label when count is zero and addUrl is set', () => {
        renderWithRouter(<ServiceHealthBar services={zeroServices} />);

        const indexerSegment = screen.getByLabelText(/indexers: add first/i);
        expect(indexerSegment).toBeInTheDocument();
        expect(screen.getByText('Add first')).toBeInTheDocument();
    });

    it('links to addUrl when count is zero and addUrl is set', () => {
        renderWithRouter(<ServiceHealthBar services={zeroServices} />);

        const indexerSegment = screen.getByLabelText(/indexers: add first/i);
        expect(indexerSegment.tagName).toBe('A');
        expect(indexerSegment).toHaveAttribute('href', '/admin/indexers');
    });

    it('shows "0 OK" when count is zero and no addUrl is set', () => {
        renderWithRouter(<ServiceHealthBar services={zeroServices} />);

        const clientSegment = screen.getByLabelText(/download clients: 0 ok/i);
        expect(clientSegment).toBeInTheDocument();
    });
});

describe('ServiceHealthBar loading state', () => {
    it('shows skeleton segments when loading', () => {
        renderWithRouter(<ServiceHealthBar services={defaultServices} loading />);

        const skeletons = document.querySelectorAll('.service-health-bar__segment--skeleton');
        expect(skeletons.length).toBe(3);
    });

    it('does not show service names when loading', () => {
        renderWithRouter(<ServiceHealthBar services={defaultServices} loading />);

        expect(screen.queryByText('Indexers')).not.toBeInTheDocument();
        expect(screen.queryByText('Download Clients')).not.toBeInTheDocument();
        expect(screen.queryByText('Media Servers')).not.toBeInTheDocument();
    });

    it('does not render links when loading', () => {
        renderWithRouter(<ServiceHealthBar services={defaultServices} loading />);

        const links = document.querySelectorAll('a.service-health-bar__segment');
        expect(links.length).toBe(0);
    });

    it('shows service names when not loading', () => {
        renderWithRouter(<ServiceHealthBar services={defaultServices} loading={false} />);

        expect(screen.getByText('Indexers')).toBeInTheDocument();
        expect(screen.getByText('Download Clients')).toBeInTheDocument();
        expect(screen.getByText('Media Servers')).toBeInTheDocument();
    });
});
