import { act, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';

import { MimeTypesEditor, normalizeMimePatterns } from './MimeTypesEditor';

describe('normalizeMimePatterns', () => {
    it('normalizes exact and wildcard patterns and removes duplicates', () => {
        expect(normalizeMimePatterns([' Video/* ', 'video/*', 'APPLICATION/EPUB+ZIP'])).toEqual({
            patterns: ['video/*', 'application/epub+zip'],
            error: null,
        });
    });

    it('rejects invalid patterns', () => {
        expect(normalizeMimePatterns(['video'])).toEqual({
            patterns: [],
            error: 'video is not a valid MIME pattern.',
        });
    });
});

describe('MimeTypesEditor', () => {
    it('shows universal and unknown patterns without edit controls for viewers', () => {
        const { rerender } = render(<MimeTypesEditor value={[]} canEdit={false} onSave={vi.fn()} label="Universal" />);

        expect(screen.getByText('Any MIME type')).toBeInTheDocument();
        expect(screen.getByRole('group', { name: 'MIME types: Any MIME type' })).toBeInTheDocument();
        expect(screen.queryByRole('button', { name: /edit MIME types/i })).not.toBeInTheDocument();

        rerender(
            <MimeTypesEditor
                value={['application/vnd.unavailable-contributor']}
                canEdit={false}
                onSave={vi.fn()}
                label="Unknown"
            />,
        );
        expect(screen.getByText('application/vnd.unavailable-contributor')).toBeInTheDocument();
        expect(screen.queryByText('Needs attention')).not.toBeInTheDocument();
    });

    it('marks preserved invalid patterns as needing attention', () => {
        render(<MimeTypesEditor value={['legacy-video']} canEdit onSave={vi.fn()} label="Legacy" />);

        expect(screen.getByText('legacy-video')).toBeInTheDocument();
        expect(screen.getByText('Needs attention')).toBeInTheDocument();
        expect(screen.getByRole('group', { name: 'MIME types: legacy-video' })).toBeInTheDocument();
    });

    it('announces pending and success states and keeps the saved value visible', async () => {
        const user = userEvent.setup();
        let resolveSave: (() => void) | undefined;
        const onSave = vi.fn(
            () =>
                new Promise<void>((resolve) => {
                    resolveSave = resolve;
                }),
        );
        render(<MimeTypesEditor value={['video/*']} canEdit onSave={onSave} label="Movies" />);

        await user.click(screen.getByRole('button', { name: 'Edit MIME types for Movies' }));
        expect(screen.getByRole('combobox', { name: 'MIME types for Movies' })).toHaveFocus();
        await user.click(screen.getByRole('button', { name: 'Remove video/*' }));
        await user.type(screen.getByRole('combobox'), ' Audio/* {Enter}');
        await user.click(screen.getByRole('button', { name: 'Save MIME changes' }));

        expect(onSave).toHaveBeenCalledWith(['audio/*']);
        expect(screen.getByRole('status')).toHaveTextContent('Saving MIME types.');

        await act(async () => resolveSave?.());

        expect(screen.getByText('audio/*')).toBeInTheDocument();
        expect(screen.getByRole('status')).toHaveTextContent('Saved');
        await waitFor(() => expect(screen.getByRole('button', { name: /edit MIME types/i })).toHaveFocus());
    });

    it('surfaces validation errors as field errors', async () => {
        const user = userEvent.setup();
        const onSave = vi.fn();
        render(<MimeTypesEditor value={[]} canEdit onSave={onSave} label="Invalid" />);

        await user.click(screen.getByRole('button', { name: /edit MIME types/i }));
        await user.type(screen.getByRole('combobox'), 'video{Enter}');
        await user.click(screen.getByRole('button', { name: 'Save MIME changes' }));

        expect(screen.getByRole('alert')).toHaveTextContent('video is not a valid MIME pattern.');
        expect(screen.getByRole('combobox')).toHaveAttribute('aria-invalid', 'true');
        expect(screen.getByRole('combobox')).toHaveAttribute('aria-describedby', screen.getByRole('alert').id);
        expect(onSave).not.toHaveBeenCalled();
    });

    it('keeps the draft after a failed save and rolls back on Escape', async () => {
        const user = userEvent.setup();
        const onSave = vi.fn().mockRejectedValue(new Error('Service unavailable'));
        render(<MimeTypesEditor value={['video/*']} canEdit onSave={onSave} label="Movies" />);

        await user.click(screen.getByRole('button', { name: /edit MIME types/i }));
        await user.click(screen.getByRole('button', { name: 'Remove video/*' }));
        await user.type(screen.getByRole('combobox'), 'audio/*{Enter}');
        await user.click(screen.getByRole('button', { name: 'Save MIME changes' }));

        expect(await screen.findByRole('alert')).toHaveTextContent('Service unavailable');
        expect(screen.getByText('audio/*')).toBeInTheDocument();

        await user.keyboard('{Escape}');

        expect(screen.getByText('video/*')).toBeInTheDocument();
        expect(screen.queryByText('audio/*')).not.toBeInTheDocument();
        await waitFor(() => expect(screen.getByRole('button', { name: /edit MIME types/i })).toHaveFocus());
    });

    it('saves with the documented keyboard shortcut', async () => {
        const user = userEvent.setup();
        const onSave = vi.fn().mockResolvedValue(undefined);
        render(<MimeTypesEditor value={['video/*']} canEdit onSave={onSave} label="Movies" />);

        await user.click(screen.getByRole('button', { name: /edit MIME types/i }));
        await user.click(screen.getByRole('button', { name: 'Remove video/*' }));
        await user.keyboard('{Control>}{Enter}{/Control}');

        await waitFor(() => expect(onSave).toHaveBeenCalledWith([]));
        expect(screen.getByText('Any MIME type')).toBeInTheDocument();
    });
});
