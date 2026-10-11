import { render, screen, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';

import { ContentDeleteFiles } from './ContentDeleteFiles';

describe('ContentDeleteFiles', () => {
    it('lists each file with its size and the total size', () => {
        render(
            <ContentDeleteFiles
                preview={{
                    data: [
                        { path: '/media/movies/Film/film.mkv', size: 3124000000 },
                        { path: '/media/movies/Film/film.srt', size: 1024 },
                    ],
                    error: null,
                    isLoading: false,
                }}
            />,
        );

        const files = within(screen.getByRole('list', { name: 'Files to delete' })).getAllByRole('listitem');
        expect(files.map((file) => file.textContent)).toEqual([
            '/media/movies/Film/film.mkv2.91 GB',
            '/media/movies/Film/film.srt1 KB',
        ]);
        expect(screen.getByText('Total').parentElement).toHaveTextContent('Total2.91 GB');
    });

    it('shows only the total when no file is deleted', () => {
        render(<ContentDeleteFiles preview={{ data: [], error: null, isLoading: false }} />);

        expect(screen.queryByRole('list')).not.toBeInTheDocument();
        expect(screen.getByText('Total').parentElement).toHaveTextContent('Total0 B');
    });

    it('shows a loading placeholder while the files load', () => {
        render(<ContentDeleteFiles preview={{ data: undefined, error: null, isLoading: true }} />);

        expect(screen.getByRole('status', { name: 'Loading' })).toBeInTheDocument();
        expect(screen.queryByText('Total')).not.toBeInTheDocument();
    });

    it('shows the error when the files cannot load', () => {
        render(
            <ContentDeleteFiles
                preview={{ data: undefined, error: new Error('The file list failed.'), isLoading: false }}
            />,
        );

        expect(screen.getByRole('alert')).toHaveTextContent('The file list failed.');
        expect(screen.queryByText('Total')).not.toBeInTheDocument();
    });

    it('renders nothing before a request exists', () => {
        const { container } = render(
            <ContentDeleteFiles preview={{ data: undefined, error: null, isLoading: false }} />,
        );

        expect(container).toBeEmptyDOMElement();
    });
});
