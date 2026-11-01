import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';

import { TooltipProvider } from '@/components/ui/Tooltip';
import { ContentDeleteFiles } from './ContentDeleteFiles';

describe('ContentDeleteFiles', () => {
    it('Render_ListsFilesWithSizesAndTotal_WhenFilesAreUnshared', () => {
        // Arrange & Act
        render(
            <ContentDeleteFiles
                preview={{
                    data: [
                        { path: '/media/movies/Film/film.mkv', size: 3124000000, isShared: false },
                        { path: '/media/movies/Film/film.srt', size: 1024, isShared: false },
                    ],
                    error: null,
                    isLoading: false,
                }}
            />,
        );

        // Assert
        const files = within(screen.getByRole('list', { name: 'File deletion preview' })).getAllByRole('listitem');
        expect(files.map((file) => file.textContent)).toEqual([
            '/media/movies/Film/film.mkv2.91 GB',
            '/media/movies/Film/film.srt1 KB',
        ]);
        expect(screen.getByText('Total').parentElement).toHaveTextContent('Total2.91 GB');
    });

    it.each(['hover', 'focus', 'click'] as const)(
        'Render_ExplainsKeptFilesAndExcludesSharedSize_WhenInteractionIs%s',
        async (interaction) => {
            // Arrange
            const user = userEvent.setup();
            render(
                <TooltipProvider>
                    <ContentDeleteFiles
                        preview={{
                            data: [
                                { path: '/media/shared.mkv', size: 1073741824, isShared: true },
                                { path: '/media/unshared.mkv', size: 1024, isShared: false },
                            ],
                            error: null,
                            isLoading: false,
                        }}
                    />
                </TooltipProvider>,
            );
            const sharedPath = screen.getByText('/media/shared.mkv');

            // Act
            if (interaction === 'focus') await user.tab();
            else await user[interaction](sharedPath);

            // Assert
            expect(sharedPath).toHaveClass('content-delete-files__path-text');
            expect(sharedPath.closest('li')).toHaveClass(
                'content-delete-files__file',
                'content-delete-files__file--shared',
            );
            expect(screen.getByText('/media/unshared.mkv').closest('li')).not.toHaveClass(
                'content-delete-files__file--shared',
            );
            expect(screen.getByText('1 GB')).toBeInTheDocument();
            expect(screen.getByText('Total').parentElement).toHaveTextContent('Total1 KB');
            expect(await screen.findByRole('tooltip')).toHaveTextContent(
                'This file is linked to other content. Skipping delete.',
            );
        },
    );

    it('Render_KeepsSharedFilesVisible_WhenDeletedTotalIsZero', () => {
        // Arrange & Act
        render(
            <TooltipProvider>
                <ContentDeleteFiles
                    preview={{
                        data: [{ path: '/media/shared.mkv', size: 1073741824, isShared: true }],
                        error: null,
                        isLoading: false,
                    }}
                />
            </TooltipProvider>,
        );

        // Assert
        expect(screen.getByText('/media/shared.mkv')).toBeInTheDocument();
        expect(screen.getByText('1 GB')).toBeInTheDocument();
        expect(screen.getByText('Total').parentElement).toHaveTextContent('Total0 B');
    });

    it('Render_ShowsOnlyZeroTotal_WhenFileListIsEmpty', () => {
        // Arrange & Act
        render(<ContentDeleteFiles preview={{ data: [], error: null, isLoading: false }} />);

        // Assert
        expect(screen.queryByRole('list')).not.toBeInTheDocument();
        expect(screen.getByText('Total').parentElement).toHaveTextContent('Total0 B');
    });

    it('Render_ShowsLoadingPlaceholder_WhenFilesAreLoading', () => {
        // Arrange & Act
        render(<ContentDeleteFiles preview={{ data: undefined, error: null, isLoading: true }} />);

        // Assert
        expect(screen.getByRole('status', { name: 'Loading' })).toBeInTheDocument();
        expect(screen.queryByText('Total')).not.toBeInTheDocument();
    });

    it('Render_ShowsAlert_WhenFileListRequestFails', () => {
        // Arrange & Act
        render(
            <ContentDeleteFiles
                preview={{ data: undefined, error: new Error('The file list failed.'), isLoading: false }}
            />,
        );

        // Assert
        expect(screen.getByRole('alert')).toHaveTextContent('The file list failed.');
        expect(screen.queryByText('Total')).not.toBeInTheDocument();
    });

    it('Render_RendersNothing_WhenPreviewDataIsUndefined', () => {
        // Arrange & Act
        const { container } = render(
            <ContentDeleteFiles preview={{ data: undefined, error: null, isLoading: false }} />,
        );

        // Assert
        expect(container).toBeEmptyDOMElement();
    });
});
