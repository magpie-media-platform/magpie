import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';

import { ExternalIds, type ExternalId } from './ExternalIds';

const mockIds: ExternalId[] = [
    { source: 'IMDb', id: 'tt0903747', url: 'https://www.imdb.com/title/tt0903747/' },
    { source: 'TMDb', id: '1396', url: 'https://www.themoviedb.org/tv/1396' },
    { source: 'TVDB', id: '81189' },
];

describe('ExternalIds', () => {
    describe('rendering', () => {
        it('renders all external IDs', () => {
            render(<ExternalIds ids={mockIds} />);
            expect(screen.getByText('IMDb')).toBeInTheDocument();
            expect(screen.getByText('TMDb')).toBeInTheDocument();
            expect(screen.getByText('TVDB')).toBeInTheDocument();
        });

        it('renders ID values in monospace', () => {
            render(<ExternalIds ids={mockIds} />);
            expect(screen.getByText('tt0903747')).toBeInTheDocument();
            expect(screen.getByText('1396')).toBeInTheDocument();
            expect(screen.getByText('81189')).toBeInTheDocument();
        });

        it('renders linked IDs as anchor elements', () => {
            render(<ExternalIds ids={mockIds} />);
            const imdbLink = screen.getByRole('link', { name: /imdb/i });
            expect(imdbLink).toHaveAttribute('href', 'https://www.imdb.com/title/tt0903747/');

            const tmdbLink = screen.getByRole('link', { name: /tmdb/i });
            expect(tmdbLink).toHaveAttribute('href', 'https://www.themoviedb.org/tv/1396');
        });

        it('renders non-linked IDs as span elements', () => {
            render(<ExternalIds ids={mockIds} />);

            const tvdbElements = screen.queryAllByRole('link', { name: /tvdb/i });
            expect(tvdbElements).toHaveLength(0);

            expect(screen.getByText('TVDB')).toBeInTheDocument();
            expect(screen.getByText('81189')).toBeInTheDocument();
        });

        it('returns null for empty IDs array', () => {
            const { container } = render(<ExternalIds ids={[]} />);
            expect(container.firstChild).toBeNull();
        });

        it('renders single ID', () => {
            render(
                <ExternalIds ids={[{ source: 'IMDb', id: 'tt1234567', url: 'https://imdb.com/title/tt1234567/' }]} />,
            );
            expect(screen.getByText('IMDb')).toBeInTheDocument();
            expect(screen.getByText('tt1234567')).toBeInTheDocument();
        });

        it('renders IDs without URLs correctly', () => {
            render(
                <ExternalIds
                    ids={[
                        { source: 'CustomDB', id: '12345' },
                        { source: 'OtherDB', id: '67890' },
                    ]}
                />,
            );
            expect(screen.getByText('CustomDB')).toBeInTheDocument();
            expect(screen.getByText('12345')).toBeInTheDocument();
            expect(screen.queryAllByRole('link')).toHaveLength(0);
        });
    });

    describe('accessibility', () => {
        it('has correct component data attribute', () => {
            render(<ExternalIds ids={mockIds} />);
            expect(document.querySelector('[data-component="external-ids"]')).toBeInTheDocument();
        });

        it('linked IDs open in new tab', () => {
            render(<ExternalIds ids={mockIds} />);
            const imdbLink = screen.getByRole('link', { name: /imdb/i });
            expect(imdbLink).toHaveAttribute('target', '_blank');
            expect(imdbLink).toHaveAttribute('rel', 'noopener noreferrer');
        });

        it('sets data-source attribute on items', () => {
            render(<ExternalIds ids={mockIds} />);
            expect(document.querySelector('[data-source="imdb"]')).toBeInTheDocument();
            expect(document.querySelector('[data-source="tmdb"]')).toBeInTheDocument();
            expect(document.querySelector('[data-source="tvdb"]')).toBeInTheDocument();
        });
    });

    describe('service colors', () => {
        it('applies colored class for known services', () => {
            render(<ExternalIds ids={[{ source: 'IMDb', id: 'tt0903747' }]} />);
            const item = document.querySelector('[data-source="imdb"]');
            expect(item).toHaveAttribute('data-colored');
        });

        it('does not apply colored class for unknown services', () => {
            render(<ExternalIds ids={[{ source: 'UnknownDB', id: '123' }]} />);
            const item = document.querySelector('[data-source="unknowndb"]');
            expect(item).not.toHaveAttribute('data-colored');
        });

        it('applies linked class for items with URLs', () => {
            render(<ExternalIds ids={[{ source: 'IMDb', id: 'tt0903747', url: 'https://imdb.com/test' }]} />);
            const item = document.querySelector('[data-source="imdb"]');
            expect(item).toHaveAttribute('data-linked');
        });

        it('does not apply linked class for items without URLs', () => {
            render(<ExternalIds ids={[{ source: 'TVDB', id: '81189' }]} />);
            const item = document.querySelector('[data-source="tvdb"]');
            expect(item).not.toHaveAttribute('data-linked');
        });
    });

    describe('custom props', () => {
        it('forwards className', () => {
            render(<ExternalIds ids={mockIds} className="custom-class" />);
            const container = document.querySelector('[data-component="external-ids"]');
            expect(container).toHaveClass('external-ids');
            expect(container).toHaveClass('custom-class');
        });

        it('forwards additional props', () => {
            render(<ExternalIds ids={mockIds} data-testid="ext-ids" />);
            expect(screen.getByTestId('ext-ids')).toBeInTheDocument();
        });
    });
});
