import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { apiPost } from '@/lib/api';
import { RenameTokenReference, type RenameTokenReferenceData } from './RenameTokenReference';

vi.mock('@/lib/api', () => ({ apiPost: vi.fn() }));

const reference: RenameTokenReferenceData = {
    tokens: [
        {
            name: 'Title',
            family: 'Metadata',
            description: 'The title of the content that owns the file.',
            example: 'Example Title',
        },
        {
            name: 'MediaInfo.AudioLanguages',
            family: 'MediaInfo',
            description: 'The languages of the audio tracks, joined with a plus sign.',
            example: 'EN+FR',
        },
        {
            name: 'studio',
            family: 'Relationship',
            description: 'The production studios, separated by spaces.',
            example: 'Example Pictures Other Films',
        },
    ],
    preview: { directory: 'Example Pictures Other Films', fileName: 'Example Title.mkv' },
};

function renderReference(ui: React.ReactNode) {
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    return render(<QueryClientProvider client={client}>{ui}</QueryClientProvider>);
}

describe('RenameTokenReference', () => {
    beforeEach(() => {
        vi.clearAllMocks();
    });

    it('lists tokens by family with meanings and collection examples and inserts a token', async () => {
        // Arrange
        vi.mocked(apiPost).mockResolvedValue(reference);
        const insert = vi.fn();
        const user = userEvent.setup();
        renderReference(
            <RenameTokenReference
                request={{ contentType: 'movie', template: '{studio}/{Title}.{Ext}' }}
                onInsert={insert}
            />,
        );

        // Act
        await user.click(await screen.findByText('Available tokens'));
        const related = screen.getByRole('region', { name: 'Related subjects' });
        await user.click(within(related).getByRole('button', { name: 'Insert {studio}' }));

        // Assert
        expect(apiPost).toHaveBeenCalledWith('/content/rename-tokens', {
            contentType: 'movie',
            contentHierarchyItemId: undefined,
            mediaFileId: undefined,
            template: '{studio}/{Title}.{Ext}',
        });
        expect(within(related).getByText('Example Pictures Other Films')).toBeInTheDocument();
        expect(
            within(screen.getByRole('region', { name: 'Media information' })).getByText('EN+FR'),
        ).toBeInTheDocument();
        expect(screen.getByText('The title of the content that owns the file.')).toBeInTheDocument();
        expect(screen.getByText('Preview with example values')).toBeInTheDocument();
        expect(screen.getByText('Example Title.mkv')).toBeInTheDocument();
        expect(insert).toHaveBeenCalledExactlyOnceWith('{studio}');
    });

    it('shows file values, missing values, and the failure of the preview for a selected file', async () => {
        // Arrange
        vi.mocked(apiPost).mockResolvedValue({
            tokens: [
                { ...reference.tokens[0], value: 'Episode', available: true },
                { ...reference.tokens[2], value: null, available: false },
            ],
            preview: { error: "The rename token 'studio' has no value." },
        });
        const user = userEvent.setup();
        renderReference(<RenameTokenReference request={{ contentHierarchyItemId: 'leaf', mediaFileId: 'file' }} />);

        // Act
        await user.click(await screen.findByText('Available tokens'));

        // Assert
        expect(screen.getByText('Preview for this file')).toBeInTheDocument();
        expect(screen.getByRole('alert')).toHaveTextContent("The file fails: The rename token 'studio' has no value.");
        expect(screen.getByText('Episode')).toBeInTheDocument();
        expect(
            within(screen.getByRole('region', { name: 'Related subjects' })).getByText(
                'No value on this file. A mask that uses this token fails the file.',
            ),
        ).toBeInTheDocument();
        expect(screen.queryByRole('button', { name: /Insert/ })).not.toBeInTheDocument();
    });
});
