import { render, screen, fireEvent } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';

import { PreviewPlayer } from './PreviewPlayer';
import { parseYouTubeId } from './parseYouTubeId';

describe('parseYouTubeId', () => {
    it('parses watch?v= urls', () => {
        expect(parseYouTubeId('https://www.youtube.com/watch?v=KEY123')).toBe('KEY123');
    });

    it('parses youtu.be short urls', () => {
        expect(parseYouTubeId('https://youtu.be/KEY123')).toBe('KEY123');
    });

    it('parses /embed/ urls', () => {
        expect(parseYouTubeId('https://www.youtube-nocookie.com/embed/KEY123')).toBe('KEY123');
    });

    it('returns null for unparseable input', () => {
        expect(parseYouTubeId('not a url')).toBeNull();
    });
});

describe('PreviewPlayer', () => {
    afterEach(() => vi.restoreAllMocks());

    it('shows a play facade and no iframe until the viewer starts the preview', () => {
        render(<PreviewPlayer preview={{ url: 'https://www.youtube.com/watch?v=KEY123', kind: 'trailer' }} />);

        expect(screen.getByRole('button', { name: 'Play Trailer' })).toBeInTheDocument();
        expect(screen.queryByTestId('preview-player-iframe')).not.toBeInTheDocument();
    });

    it('embeds YouTube in a sandboxed nocookie iframe with native controls', async () => {
        const user = userEvent.setup();
        render(<PreviewPlayer preview={{ url: 'https://www.youtube.com/watch?v=KEY123', kind: 'trailer' }} />);

        await user.click(screen.getByRole('button', { name: 'Play Trailer' }));

        const frame = screen.getByTitle('Trailer');
        expect(frame).toHaveAttribute(
            'src',
            'https://www.youtube-nocookie.com/embed/KEY123?autoplay=1&playsinline=1&rel=0',
        );
        expect(frame).toHaveAttribute('sandbox', 'allow-scripts allow-same-origin allow-presentation');
        expect(frame).toHaveAttribute('allow', 'autoplay; encrypted-media; fullscreen');
        expect(frame).toHaveAttribute('allowfullscreen');
        expect(document.querySelector('script[src="https://www.youtube.com/iframe_api"]')).toBeNull();
        expect(screen.queryByTestId('preview-player-controls')).not.toBeInTheDocument();
    });

    it('moves focus to the stop control once the preview starts', async () => {
        const user = userEvent.setup();
        render(<PreviewPlayer preview={{ url: 'https://www.youtube.com/watch?v=KEY123', kind: 'trailer' }} />);

        await user.click(screen.getByRole('button', { name: 'Play Trailer' }));

        expect(screen.getByRole('button', { name: 'Stop preview' })).toHaveFocus();
    });

    it('stops the preview on Escape and returns focus to the facade', async () => {
        const user = userEvent.setup();
        render(<PreviewPlayer preview={{ url: 'https://www.youtube.com/watch?v=KEY123', kind: 'trailer' }} />);
        await user.click(screen.getByRole('button', { name: 'Play Trailer' }));
        const frame = screen.getByTitle('Trailer');

        await user.keyboard('{Escape}');

        expect(frame).not.toBeInTheDocument();
        expect(screen.getByRole('button', { name: 'Play Trailer' })).toHaveFocus();
    });

    it('starts the preview from the keyboard and stops it from the stop button', async () => {
        const user = userEvent.setup();
        render(<PreviewPlayer preview={{ url: 'https://www.youtube.com/watch?v=KEY123', kind: 'trailer' }} />);
        screen.getByRole('button', { name: 'Play Trailer' }).focus();

        await user.keyboard('{Enter}');
        await user.click(screen.getByRole('button', { name: 'Stop preview' }));

        expect(screen.queryByTestId('preview-player-iframe')).not.toBeInTheDocument();
    });

    it('tears down a playing frame when the player unmounts', async () => {
        const user = userEvent.setup();
        const { unmount } = render(
            <PreviewPlayer preview={{ url: 'https://www.youtube.com/watch?v=KEY123', kind: 'trailer' }} />,
        );
        await user.click(screen.getByRole('button', { name: 'Play Trailer' }));
        const playingFrame = screen.getByTitle('Trailer');

        unmount();

        expect(playingFrame).not.toBeInTheDocument();
    });

    it('resolves a YouTube embed kind and retains the facade thumbnail', async () => {
        const user = userEvent.setup();
        render(<PreviewPlayer preview={{ url: 'https://youtu.be/ABC987', embedKind: 'youtube', kind: 'teaser' }} />);
        expect(screen.getByTestId('preview-player-facade').querySelector('img')).toHaveAttribute(
            'src',
            'https://i.ytimg.com/vi/ABC987/hqdefault.jpg',
        );
        await user.click(screen.getByTestId('preview-player-facade'));
        expect(screen.getByTitle('Teaser')).toHaveAttribute(
            'src',
            'https://www.youtube-nocookie.com/embed/ABC987?autoplay=1&playsinline=1&rel=0',
        );
    });

    it('retains direct video seeking, volume and keyboard stop', async () => {
        const user = userEvent.setup();
        const pause = vi.spyOn(HTMLMediaElement.prototype, 'pause').mockImplementation(() => undefined);
        render(<PreviewPlayer preview={{ url: 'https://example.com/clip.mp4', title: 'Clip' }} />);
        await user.click(screen.getByRole('button', { name: 'Play Clip' }));
        const video = screen.getByTestId('preview-player-video') as HTMLVideoElement;
        Object.defineProperty(video, 'duration', { value: 120 });
        Object.defineProperty(video, 'mozHasAudio', { value: true });
        fireEvent.loadedMetadata(video);
        fireEvent.playing(video);
        fireEvent.change(screen.getByTestId('preview-player-scrubber'), { target: { value: '30' } });
        expect(video.currentTime).toBe(30);
        fireEvent.change(screen.getByTestId('preview-player-volume-slider'), { target: { value: '0.8' } });
        expect(video.volume).toBe(0.8);
        await user.click(screen.getByTestId('preview-player-volume-toggle'));
        expect(video.muted).toBe(true);
        await user.click(screen.getByRole('button', { name: 'Stop preview' }));
        expect(pause).toHaveBeenCalledOnce();
        expect(video).not.toBeInTheDocument();
        expect(screen.getByRole('button', { name: 'Play Clip' })).toHaveFocus();
    });

    it.each([
        ['magnet:?xt=urn:zzz', 'Raw clip'],
        ['magnet:?xt=urn:foo', 'Behind the scenes'],
    ])('renders plain text for unsupported preview %s', (url, title) => {
        render(<PreviewPlayer preview={{ url, kind: 'clip', title }} />);
        const label = screen.getByTestId('preview-player-external');
        expect(label.tagName).toBe('SPAN');
        expect(label).not.toHaveAttribute('href');
        expect(screen.queryByTestId('preview-player-facade')).not.toBeInTheDocument();
    });
    describe('fit', () => {
        it('defaults to the wide (16:9) fit', () => {
            render(<PreviewPlayer preview={{ url: 'https://www.youtube.com/watch?v=KEY123', kind: 'trailer' }} />);
            const box = screen.getByTestId('preview-player-facade').closest('[data-component="preview-player"]');
            expect(box).toHaveAttribute('data-fit', 'wide');
            expect(box).not.toHaveClass('preview-player--poster');
        });

        it('fills the parent container and shows the poster + play overlay (no media) in poster fit', () => {
            render(
                <PreviewPlayer
                    preview={{ url: 'https://www.youtube.com/watch?v=KEY123', kind: 'trailer' }}
                    posterUrl="https://example.com/poster.jpg"
                    fit="poster"
                />,
            );
            const facade = screen.getByTestId('preview-player-facade');
            const box = facade.closest('[data-component="preview-player"]');
            expect(box).toHaveAttribute('data-fit', 'poster');
            expect(box).toHaveClass('preview-player--poster');
            expect(facade.querySelector('img')).toHaveAttribute('src', 'https://example.com/poster.jpg');
            expect(facade).toHaveAttribute('aria-label', 'Play Trailer');
            expect(screen.queryByTestId('preview-player-iframe')).not.toBeInTheDocument();
            expect(screen.queryByTestId('preview-player-video')).not.toBeInTheDocument();
            expect(screen.queryByTestId('preview-player-controls')).not.toBeInTheDocument();
        });
    });
});

it('stops direct playback with the keyboard', async () => {
    const user = userEvent.setup();
    vi.spyOn(HTMLMediaElement.prototype, 'pause').mockImplementation(() => {});
    render(<PreviewPlayer preview={{ url: 'https://example.com/preview.mp4', mimeType: 'video/mp4' }} />);
    await user.tab();
    await user.keyboard('{Enter}');
    const stop = screen.getByRole('button', { name: 'Stop preview' });
    expect(stop).toHaveClass('preview-player__stop');
    expect(stop).not.toHaveClass('preview-player__fullscreen');
    expect(stop).toHaveFocus();
    await user.keyboard('{Enter}');
    expect(screen.getByRole('button', { name: 'Play Preview' })).toBeInTheDocument();
    expect(screen.queryByTestId('preview-player-controls')).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Play Preview' })).toHaveFocus();
    await user.keyboard('{Enter}');
    await user.keyboard('{Escape}');
    expect(screen.getByRole('button', { name: 'Play Preview' })).toHaveFocus();
});

it('passes the declared MIME type to the direct video source', async () => {
    const user = userEvent.setup();
    render(<PreviewPlayer preview={{ url: 'https://example.com/preview.webm', mimeType: 'video/webm' }} />);
    await user.click(screen.getByRole('button', { name: 'Play Preview' }));
    expect(screen.getByTestId('preview-player-video').querySelector('source')).toHaveAttribute('type', 'video/webm');
});

it.each(['youtube', 'YouTube'])('shows unavailable for an unrecognized %s embed URL', (embedKind) => {
    render(<PreviewPlayer preview={{ url: 'https://example.com/embed/123', embedKind }} />);
    expect(screen.getByText('Preview unavailable')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Play Preview' })).not.toBeInTheDocument();
});

it.each([
    [undefined, undefined, 'Preview unavailable'],
    [' ', ' ', 'Preview unavailable'],
    ['Preview', undefined, 'Preview unavailable'],
    [undefined, 'preview', 'Preview unavailable'],
    ['Behind the scenes', 'clip', 'Behind the scenes unavailable'],
    [undefined, 'trailer', 'Trailer unavailable'],
])('shows unavailable copy for title %s and kind %s', (title, kind, expected) => {
    render(<PreviewPlayer preview={{ url: 'magnet:?xt=urn:foo', title, kind }} />);
    expect(screen.getByTestId('preview-player-external')).toHaveTextContent(expected);
});

it.each([undefined, '', 'custom'])('plays direct media with embedKind %s', async (embedKind) => {
    const user = userEvent.setup();
    render(<PreviewPlayer preview={{ url: 'https://example.com/preview.webm', mimeType: 'video/webm', embedKind }} />);
    await user.click(screen.getByRole('button', { name: 'Play Preview' }));
    const source = screen.getByTestId('preview-player-video').querySelector('source');
    expect(source).toHaveAttribute('src', 'https://example.com/preview.webm');
    expect(source).toHaveAttribute('type', 'video/webm');
});
