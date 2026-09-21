import { fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it, vi, beforeEach } from 'vitest';

import { RelatedSubjectsTab } from './RelatedSubjectsTab';
import type { ContentRelationship } from '@/types';

const useContentRelationships = vi.fn();
vi.mock('@/hooks/useContent', () => ({
    useContentRelationships: (...args: unknown[]) => useContentRelationships(...args),
}));

function rel(partial: Partial<ContentRelationship>): ContentRelationship {
    return {
        id: partial.id ?? 'r1',
        kind: partial.kind ?? 'cast',
        qualifier: partial.qualifier ?? null,
        ordinal: partial.ordinal ?? 0,
        subjectId: partial.subjectId ?? null,
        name: partial.name ?? 'Someone',
        imageUrl: partial.imageUrl ?? null,
    } as ContentRelationship;
}

function renderTab() {
    return render(
        <MemoryRouter>
            <RelatedSubjectsTab contentId="c1" />
        </MemoryRouter>,
    );
}

describe('RelatedSubjectsTab', () => {
    beforeEach(() => {
        useContentRelationships.mockReset();
    });

    it('title-cases each kind heading generically', () => {
        useContentRelationships.mockReturnValue({
            data: [
                rel({ id: 'a', kind: 'cast', name: 'Dane DeHaan' }),
                rel({ id: 'b', kind: 'crew', name: 'Bojan Bazelli' }),
                rel({ id: 'c', kind: 'director', name: 'Gore Verbinski' }),
                rel({ id: 'd', kind: 'writer', name: 'Justin Haythe' }),
            ],
            isLoading: false,
        });
        renderTab();
        expect(screen.getByRole('heading', { name: 'Cast' })).toBeInTheDocument();
        expect(screen.getByRole('heading', { name: 'Crew' })).toBeInTheDocument();
        expect(screen.getByRole('heading', { name: 'Director' })).toBeInTheDocument();
        expect(screen.getByRole('heading', { name: 'Writer' })).toBeInTheDocument();
    });

    it('renders the qualifier without an "as" prefix for any kind', () => {
        useContentRelationships.mockReturnValue({
            data: [rel({ id: 'a', kind: 'cast', name: 'Dane DeHaan', qualifier: 'Lockhart' })],
            isLoading: false,
        });
        renderTab();
        expect(screen.getByText('Lockhart')).toBeInTheDocument();
        expect(screen.queryByText(/as Lockhart/)).not.toBeInTheDocument();
    });

    it('renders a circular subject image with kind-neutral alt text when one is available', () => {
        useContentRelationships.mockReturnValue({
            data: [rel({ id: 'a', kind: 'cast', name: 'Dane DeHaan', imageUrl: 'https://img.example/dehaan.jpg' })],
            isLoading: false,
        });
        renderTab();
        const img = screen.getByAltText('Dane DeHaan') as HTMLImageElement;
        expect(img).toBeInTheDocument();
        expect(img.src).toBe('https://img.example/dehaan.jpg');
        expect(screen.queryByTestId('related-subject-avatar-placeholder')).not.toBeInTheDocument();
        fireEvent.error(img);
        expect(screen.queryByAltText('Dane DeHaan')).not.toBeInTheDocument();
        expect(screen.queryByTestId('related-subject-avatar-placeholder')).toBeInTheDocument();
    });

    it('replaces a broken image with the placeholder', () => {
        // Arrange
        useContentRelationships.mockReturnValue({
            data: [
                {
                    id: 'broken',
                    kind: 'related',
                    name: 'Sample',
                    ordinal: 0,
                    imageUrl: 'https://img.example/broken.png',
                },
            ],
            isLoading: false,
        });
        render(
            <MemoryRouter>
                <RelatedSubjectsTab contentId="c1" />
            </MemoryRouter>,
        );
        // Act
        fireEvent.error(screen.getByAltText('Sample'));
        // Assert
        expect(screen.queryByAltText('Sample')).not.toBeInTheDocument();
        expect(screen.getByTestId('related-subject-avatar-placeholder')).toBeInTheDocument();
    });

    it('shows a skeleton while relationships load', () => {
        // Arrange
        useContentRelationships.mockReturnValue({ data: undefined, isLoading: true });
        // Act
        const { container } = render(
            <MemoryRouter>
                <RelatedSubjectsTab contentId="c1" />
            </MemoryRouter>,
        );
        // Assert
        expect(container.querySelector('[data-component="skeleton"]')).toBeInTheDocument();
        expect(screen.queryByText('No related information')).not.toBeInTheDocument();
    });

    it('does not call a non-person subject image a headshot', () => {
        useContentRelationships.mockReturnValue({
            data: [rel({ id: 'a', kind: 'studio', name: 'Apple Studios', imageUrl: 'https://img.example/apple.png' })],
            isLoading: false,
        });
        renderTab();
        expect(screen.getByAltText('Apple Studios')).toBeInTheDocument();
        expect(screen.queryByAltText(/headshot/i)).not.toBeInTheDocument();
    });

    it('renders a placeholder avatar when no image is available', () => {
        useContentRelationships.mockReturnValue({
            data: [rel({ id: 'a', kind: 'cast', name: 'No Photo', imageUrl: null })],
            isLoading: false,
        });
        renderTab();
        expect(screen.queryByTestId('related-subject-avatar-placeholder')).toBeInTheDocument();
        expect(screen.queryByAltText('No Photo')).not.toBeInTheDocument();
    });

    it('links a materialised subject entry to its detail page', () => {
        useContentRelationships.mockReturnValue({
            data: [rel({ id: 'a', kind: 'cast', name: 'Dane DeHaan', subjectId: 'subj-1' })],
            isLoading: false,
        });
        renderTab();
        const link = screen.getByTestId('related-subject-link-a');
        expect(link).toHaveAttribute('href', '/subjects/subj-1');
    });

    it('renders dangling entries without a link', () => {
        useContentRelationships.mockReturnValue({
            data: [rel({ id: 'a', kind: 'cast', name: 'Dangling', subjectId: null })],
            isLoading: false,
        });
        renderTab();
        expect(screen.getByTestId('related-subject-name-a')).toBeInTheDocument();
        expect(screen.queryByTestId('related-subject-link-a')).not.toBeInTheDocument();
    });

    it('renders a single empty state, and no sections, when there are no relationships', () => {
        useContentRelationships.mockReturnValue({ data: [], isLoading: false });
        renderTab();
        expect(screen.getByRole('heading', { name: 'No related information' })).toBeInTheDocument();
        expect(screen.getByText('This item has no related subjects.')).toBeInTheDocument();
        expect(screen.queryByText(/No cast information/)).not.toBeInTheDocument();
        expect(screen.queryByText(/No crew information/)).not.toBeInTheDocument();
    });

    it('renders only the kinds that have rows, with no empty placeholder sections', () => {
        useContentRelationships.mockReturnValue({
            data: [rel({ id: 'a', kind: 'cast', name: 'Dane DeHaan' })],
            isLoading: false,
        });
        renderTab();
        expect(screen.getByRole('heading', { name: 'Cast' })).toBeInTheDocument();
        expect(screen.getByText('Dane DeHaan')).toBeInTheDocument();
        expect(screen.queryByRole('heading', { name: 'Crew' })).not.toBeInTheDocument();
        expect(screen.queryByRole('heading', { name: 'Writer' })).not.toBeInTheDocument();
        expect(screen.queryByRole('heading', { name: 'Studio' })).not.toBeInTheDocument();
        expect(screen.queryByText('No related information.')).not.toBeInTheDocument();
    });

    it('orders dynamic kinds by their ordinal, in their natural appearance order', () => {
        useContentRelationships.mockReturnValue({
            data: [
                rel({ id: 'c', kind: 'studio', name: 'Studio Co', ordinal: 5 }),
                rel({ id: 'a', kind: 'cast', name: 'Lead Actor', ordinal: 0 }),
                rel({ id: 'b', kind: 'director', name: 'A Director', ordinal: 2 }),
            ],
            isLoading: false,
        });
        renderTab();
        const headings = screen.getAllByRole('heading').map((h) => h.textContent);
        expect(headings).toEqual(['Cast', 'Director', 'Studio']);
    });
});
