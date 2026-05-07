import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Routes, Route, useLocation } from 'react-router-dom';
import { describe, expect, it, vi } from 'vitest';

import { ContentDetailsHeader, type ContentDetailItem, type ParentContentRef } from './ContentDetailsHeader';

function renderWithRouter(ui: React.ReactElement) {
    return render(<MemoryRouter>{ui}</MemoryRouter>);
}

const mockContent: ContentDetailItem = {
    id: 'test-1',
    title: 'Test Movie',
    posterUrl: 'https://example.com/poster.jpg',
    kind: 'movie',
    monitoring: 'active',
    fulfillment: 'fulfilled',
    year: 2024,
    overview: 'A test movie about testing things.',
};

const mockParent: ParentContentRef = {
    id: 'parent-1',
    title: 'Parent Show',
    path: '/library/parent-1',
};

describe('ContentDetailsHeader', () => {
    it('renders the title', () => {
        renderWithRouter(<ContentDetailsHeader content={mockContent} />);
        expect(screen.getByRole('heading', { name: /Test Movie/i })).toBeInTheDocument();
    });

    it('renders the year when provided', () => {
        renderWithRouter(<ContentDetailsHeader content={mockContent} />);
        expect(screen.getByText('(2024)')).toBeInTheDocument();
    });

    it('does not render year when not provided', () => {
        const contentWithoutYear = { ...mockContent, year: undefined };
        renderWithRouter(<ContentDetailsHeader content={contentWithoutYear} />);
        expect(screen.queryByText(/\(\d{4}\)/)).not.toBeInTheDocument();
    });

    it('renders the poster image', () => {
        renderWithRouter(<ContentDetailsHeader content={mockContent} />);
        const poster = screen.getByAltText('Test Movie poster');
        expect(poster).toBeInTheDocument();
        expect(poster).toHaveAttribute('src', mockContent.posterUrl);
    });

    it('renders placeholder when no poster URL provided', () => {
        const contentWithoutPoster = { ...mockContent, posterUrl: undefined };
        renderWithRouter(<ContentDetailsHeader content={contentWithoutPoster} />);
        expect(screen.queryByAltText('Test Movie poster')).not.toBeInTheDocument();

        const posterContainer = document.querySelector('.content-details-header__poster--placeholder');
        expect(posterContainer).toBeInTheDocument();
    });

    it('renders the kind badge', () => {
        renderWithRouter(<ContentDetailsHeader content={mockContent} />);
        expect(screen.getByText('Movie')).toBeInTheDocument();
    });

    it('renders monitored status when monitored', () => {
        renderWithRouter(<ContentDetailsHeader content={mockContent} />);
        expect(screen.getByText('Monitored')).toBeInTheDocument();
    });

    it('renders not monitored status when excluded', () => {
        const excludedContent = { ...mockContent, monitoring: 'excluded' as const };
        renderWithRouter(<ContentDetailsHeader content={excludedContent} />);
        expect(screen.getByText('Not Monitored')).toBeInTheDocument();
    });

    it('renders fulfillment badge with correct variant', () => {
        renderWithRouter(<ContentDetailsHeader content={mockContent} />);
        expect(screen.getByText('Fulfilled')).toBeInTheDocument();
    });

    it('renders different fulfillment statuses', () => {
        const statuses = [
            { status: 'missing', label: 'Missing' },
            { status: 'upgradable', label: 'Upgradable' },
            { status: 'fulfilled', label: 'Fulfilled' },
            { status: 'fulfilledExternally', label: 'External' },
        ] as const;

        statuses.forEach(({ status, label }) => {
            const content = { ...mockContent, fulfillment: status };
            const { unmount } = renderWithRouter(<ContentDetailsHeader content={content} />);
            expect(screen.getByText(label)).toBeInTheDocument();
            unmount();
        });
    });

    it('renders different kind types', () => {
        const kinds = [
            { kind: 'episode', label: 'Episode' },
            { kind: 'movie', label: 'Movie' },
            { kind: 'music', label: 'Music' },
            { kind: 'book', label: 'Book' },
        ] as const;

        kinds.forEach(({ kind, label }) => {
            const content = { ...mockContent, kind };
            const { unmount } = renderWithRouter(<ContentDetailsHeader content={content} />);
            expect(screen.getByText(label)).toBeInTheDocument();
            unmount();
        });
    });

    it('renders overview when provided', () => {
        renderWithRouter(<ContentDetailsHeader content={mockContent} />);
        expect(screen.getByText('A test movie about testing things.')).toBeInTheDocument();
    });

    it('does not render overview when not provided', () => {
        const contentWithoutOverview = { ...mockContent, overview: undefined };
        renderWithRouter(<ContentDetailsHeader content={contentWithoutOverview} />);
        expect(screen.queryByText('A test movie about testing things.')).not.toBeInTheDocument();
    });

    it('renders actions when provided', () => {
        renderWithRouter(<ContentDetailsHeader content={mockContent} actions={<button>Test Action</button>} />);
        expect(screen.getByRole('button', { name: 'Test Action' })).toBeInTheDocument();
    });

    it('does not render actions container when actions not provided', () => {
        renderWithRouter(<ContentDetailsHeader content={mockContent} />);
        const actionsContainer = document.querySelector('.content-details-header__actions');
        expect(actionsContainer).not.toBeInTheDocument();
    });

    it('renders mobile overflow menu when actions are provided', () => {
        renderWithRouter(<ContentDetailsHeader content={mockContent} actions={<button>Test Action</button>} />);
        expect(screen.getByTestId('content-details-header__mobile-menu')).toBeInTheDocument();
    });

    it('does not render mobile overflow menu when actions not provided', () => {
        renderWithRouter(<ContentDetailsHeader content={mockContent} />);
        expect(screen.queryByTestId('content-details-header__mobile-menu')).not.toBeInTheDocument();
    });

    it('applies data attributes for automation', () => {
        renderWithRouter(<ContentDetailsHeader content={mockContent} />);
        const header = document.querySelector('[data-component="content-details-header"]');
        expect(header).toBeInTheDocument();
        expect(header).toHaveAttribute('data-content-id', 'test-1');
    });

    it('forwards ref', () => {
        const ref = vi.fn();
        renderWithRouter(<ContentDetailsHeader ref={ref} content={mockContent} />);
        expect(ref).toHaveBeenCalledWith(expect.any(HTMLElement));
    });

    it('applies custom className', () => {
        renderWithRouter(<ContentDetailsHeader content={mockContent} className="custom-class" />);
        const header = document.querySelector('.content-details-header');
        expect(header).toHaveClass('custom-class');
    });

    it('passes through additional props', () => {
        renderWithRouter(<ContentDetailsHeader content={mockContent} data-testid="my-header" />);
        expect(screen.getByTestId('my-header')).toBeInTheDocument();
    });

    it('renders as a header element', () => {
        renderWithRouter(<ContentDetailsHeader content={mockContent} />);
        const header = screen.getByRole('banner');
        expect(header).toBeInTheDocument();
    });

    describe('parent link', () => {
        it('renders parent link when parent is provided', () => {
            renderWithRouter(<ContentDetailsHeader content={mockContent} parent={mockParent} />);
            const link = screen.getByRole('link', { name: /Parent Show/i });
            expect(link).toBeInTheDocument();
            expect(link).toHaveAttribute('href', '/library/parent-1');
        });

        it('does not render parent link when parent is not provided', () => {
            renderWithRouter(<ContentDetailsHeader content={mockContent} />);
            const link = screen.queryByRole('link', { name: /Parent Show/i });
            expect(link).not.toBeInTheDocument();
        });

        it('applies data attributes to parent link', () => {
            renderWithRouter(<ContentDetailsHeader content={mockContent} parent={mockParent} />);
            const link = screen.getByRole('link', { name: /Parent Show/i });
            expect(link).toHaveAttribute('data-action', 'navigate-parent');
            expect(link).toHaveAttribute('data-parent-id', 'parent-1');
        });

        it('parent link has correct styling class', () => {
            renderWithRouter(<ContentDetailsHeader content={mockContent} parent={mockParent} />);
            const link = screen.getByRole('link', { name: /Parent Show/i });
            expect(link).toHaveClass('content-details-header__parent-link');
        });
    });

    describe('subject link', () => {
        const mockContentWithSubject: ContentDetailItem = {
            ...mockContent,
            subjectId: 'subject-1',
            subjectName: 'Test Subject',
        };

        it('renders subject link when subjectId is provided', () => {
            renderWithRouter(<ContentDetailsHeader content={mockContentWithSubject} />);
            const link = screen.getByRole('link', { name: /Test Subject/i });
            expect(link).toBeInTheDocument();
            expect(link).toHaveAttribute('href', '/subjects/subject-1');
        });

        it('does not render subject link when subjectId is not provided', () => {
            renderWithRouter(<ContentDetailsHeader content={mockContent} />);
            const link = screen.queryByRole('link', { name: /View Metadata/i });
            expect(link).not.toBeInTheDocument();
        });

        it('displays "View Metadata" when subjectName is not provided', () => {
            const contentWithSubjectIdOnly = { ...mockContent, subjectId: 'subject-1' };
            renderWithRouter(<ContentDetailsHeader content={contentWithSubjectIdOnly} />);
            const link = screen.getByRole('link', { name: /View Metadata/i });
            expect(link).toBeInTheDocument();
        });

        it('applies data attributes to subject link', () => {
            renderWithRouter(<ContentDetailsHeader content={mockContentWithSubject} />);
            const link = screen.getByRole('link', { name: /Test Subject/i });
            expect(link).toHaveAttribute('data-action', 'navigate-subject');
            expect(link).toHaveAttribute('data-subject-id', 'subject-1');
        });

        it('subject link has correct styling class', () => {
            renderWithRouter(<ContentDetailsHeader content={mockContentWithSubject} />);
            const link = screen.getByRole('link', { name: /Test Subject/i });
            expect(link).toHaveClass('content-details-header__subject-link');
        });

        it('passes fromContent state to subject link for back navigation', async () => {
            const user = userEvent.setup();
            let capturedState: unknown = null;

            function StateCapture() {
                const location = useLocation();
                capturedState = location.state;
                return <div>Subject Page</div>;
            }

            render(
                <MemoryRouter initialEntries={['/library/test-1']}>
                    <Routes>
                        <Route
                            path="/library/:contentId"
                            element={<ContentDetailsHeader content={mockContentWithSubject} />}
                        />
                        <Route path="/subjects/:subjectId" element={<StateCapture />} />
                    </Routes>
                </MemoryRouter>,
            );

            await user.click(screen.getByRole('link', { name: /Test Subject/i }));

            expect(capturedState).toEqual({
                fromContent: { id: 'test-1', title: 'Test Movie' },
            });
        });
    });
});
