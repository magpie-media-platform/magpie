import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi, beforeEach } from 'vitest';

import type { ApiIndexer } from '@/hooks/useIndexers';

import { IndexerManagement } from './IndexerManagement';

const mockIndexers: ApiIndexer[] = [
    {
        id: 'idx-1',
        name: 'NZBgeek',
        providerType: 'Newznab',
        description: 'A great indexer',
        availability: 1, // Public
        categories: [],
        languages: [],
        createdAt: '2024-01-01T00:00:00Z',
        createdByUserId: null,
        modifiedAt: '2024-01-15T12:00:00Z',
        modifiedByUserId: null,
    },
    {
        id: 'idx-2',
        name: 'DOGnzb',
        providerType: 'Newznab',
        description: null,
        availability: 2, // Private
        categories: [],
        languages: [],
        createdAt: '2024-01-01T00:00:00Z',
        createdByUserId: null,
        modifiedAt: null,
        modifiedByUserId: null,
    },
    {
        id: 'idx-3',
        name: 'Torznab Test',
        providerType: 'Torznab',
        description: null,
        availability: 3, // Semi-Private
        categories: [],
        languages: [],
        createdAt: '2024-01-05T00:00:00Z',
        createdByUserId: null,
        modifiedAt: null,
        modifiedByUserId: null,
    },
];

const mockCreateIndexer = vi.fn();
const mockUpdateIndexer = vi.fn();
const mockDeleteIndexer = vi.fn();

vi.mock('@/hooks/useIndexers', () => ({
    useIndexers: () => ({
        indexers: mockIndexers,
        isLoading: false,
        createIndexer: mockCreateIndexer,
        updateIndexer: mockUpdateIndexer,
        deleteIndexer: mockDeleteIndexer,
        isCreatingIndexer: false,
        isUpdatingIndexer: false,
    }),
}));

const mockToast = vi.fn();
vi.mock('@/components/ui/Toast', () => ({
    useToast: () => ({ toast: mockToast }),
}));

describe('IndexerManagement', () => {
    beforeEach(() => {
        vi.clearAllMocks();
    });

    it('renders the indexer list', () => {
        render(<IndexerManagement />);
        expect(screen.getByText('NZBgeek')).toBeInTheDocument();
        expect(screen.getByText('DOGnzb')).toBeInTheDocument();
        expect(screen.getByText('Torznab Test')).toBeInTheDocument();
    });

    it('shows availability summary badges', () => {
        render(<IndexerManagement />);
        const summary = screen.getByLabelText('Availability summary');
        expect(summary).toBeInTheDocument();
        expect(screen.getByText('Public')).toBeInTheDocument();
        expect(screen.getByText('Semi-Private')).toBeInTheDocument();
        expect(screen.getByText('Private')).toBeInTheDocument();
    });

    it('renders correct number of indexer items', () => {
        render(<IndexerManagement />);
        const items = screen.getAllByTestId('indexers-page__item');
        expect(items).toHaveLength(3);
    });

    it('shows provider type on each indexer row', () => {
        render(<IndexerManagement />);
        expect(screen.getAllByText('Newznab')).toHaveLength(2);
        expect(screen.getByText('Torznab')).toBeInTheDocument();
    });

    it('renders edit and delete buttons for each indexer', () => {
        render(<IndexerManagement />);
        const editButtons = screen.getAllByTestId('indexers-page__action-btn').filter(
            (btn) => btn.getAttribute('data-action') === 'edit',
        );
        const deleteButtons = screen.getAllByTestId('indexers-page__action-btn').filter(
            (btn) => btn.getAttribute('data-action') === 'delete',
        );
        expect(editButtons).toHaveLength(3);
        expect(deleteButtons).toHaveLength(3);
    });

    it('sets data attributes on indexer rows', () => {
        render(<IndexerManagement />);
        const firstItem = screen.getAllByTestId('indexers-page__item')[0];
        expect(firstItem).toHaveAttribute('data-indexer-id', 'idx-1');
        expect(firstItem).toHaveAttribute('data-availability', 'healthy');
    });

    it('has accessible labels on action buttons', () => {
        render(<IndexerManagement />);
        expect(screen.getByLabelText('Edit NZBgeek')).toBeInTheDocument();
        expect(screen.getByLabelText('Delete NZBgeek')).toBeInTheDocument();
    });
});
