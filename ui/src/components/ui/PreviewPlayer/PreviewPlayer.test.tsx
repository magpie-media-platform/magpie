import { render, screen, waitFor, act } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { PreviewPlayer } from './PreviewPlayer';
import { parseYouTubeId } from './parseYouTubeId';
import type { YTNamespace, YTPlayer, YTPlayerOptions } from './youtubeApi';

vi.mock('./youtubeApi', async (importOriginal) => {
    const actual = await importOriginal<typeof import('./youtubeApi')>();
    return {
        ...actual,
        loadYouTubeIframeApi: () => Promise.resolve(fakeYT),
    };
});

let lastPlayer: FakePlayer | null = null;
let lastOptions: YTPlayerOptions | null = null;

let deferReady = false;

class FakePlayer implements YTPlayer {
    options: YTPlayerOptions;
    volume = 50;
    muted = false;
    destroyed = false;
    stopped = false;
    playCalled = false;
    ready = false;
    getCurrentTimeCalls = 0;

    constructor(_element: HTMLElement | string, options: YTPlayerOptions) {
        this.options = options;
        lastOptions = options;
        if (deferReady) {
            this.removeGetters();
            return;
        }
        queueMicrotask(() => this.fireReady());
    }

    private removeGetters() {
        (this as unknown as Record<string, unknown>).getCurrentTime = undefined;
        (this as unknown as Record<string, unknown>).getDuration = undefined;
        (this as unknown as Record<string, unknown>).getVolume = undefined;
    }

    fireReady() {
        this.ready = true;
        delete (this as unknown as Record<string, unknown>).getCurrentTime;
        delete (this as unknown as Record<string, unknown>).getDuration;
        delete (this as unknown as Record<string, unknown>).getVolume;
        this.options.events?.onReady?.({ target: this });
    }

    playVideo() {
        this.playCalled = true;
    }
    stopVideo() {
        this.stopped = true;
    }
    seekTo = vi.fn();
    getCurrentTime() {
        this.getCurrentTimeCalls += 1;
        return 0;
    }
    getDuration() {
        return 120;
    }
    setVolume = vi.fn((v: number) => {
        this.volume = v;
    });
    getVolume() {
        return this.volume;
    }
    mute() {
        this.muted = true;
    }
    unMute = vi.fn(() => {
        this.muted = false;
    });
    isMuted() {
        return this.muted;
    }
    destroy() {
        this.destroyed = true;
        (this as unknown as Record<string, unknown>).getCurrentTime = undefined;
        (this as unknown as Record<string, unknown>).getDuration = undefined;
    }

    fireEnded() {
        this.options.events?.onStateChange?.({ data: fakeYT.PlayerState.ENDED, target: this });
    }
}

const fakeYT: YTNamespace = {
    Player: function (this: unknown, element: HTMLElement | string, options: YTPlayerOptions) {
        const player = new FakePlayer(element, options);
        lastPlayer = player;
        return player;
    } as unknown as YTNamespace['Player'],
    PlayerState: {
        ENDED: 0,
        PLAYING: 1,
        PAUSED: 2,
        BUFFERING: 3,
        CUED: 5,
        UNSTARTED: -1,
    },
};

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
    beforeEach(() => {
        lastPlayer = null;
        lastOptions = null;
        deferReady = false;
    });

    afterEach(() => {
        deferReady = false;
        vi.restoreAllMocks();
    });

    it('wide fit: youtube renders a lazy facade and mounts the interactive player on click', async () => {
        const user = userEvent.setup();
        render(<PreviewPlayer preview={{ url: 'https://www.youtube.com/watch?v=KEY123', kind: 'trailer' }} />);

        const facade = screen.getByTestId('preview-player-facade');
        expect(facade).toBeInTheDocument();
        expect(screen.queryByTestId('preview-player-yt-mount')).not.toBeInTheDocument();
        expect(facade.querySelector('img')).toHaveAttribute('src', 'https://i.ytimg.com/vi/KEY123/hqdefault.jpg');

        await user.click(facade);

        await waitFor(() => expect(lastPlayer).not.toBeNull());
        expect(lastOptions?.host).toBe('https://www.youtube-nocookie.com');
        expect(lastOptions?.videoId).toBe('KEY123');
        expect(lastOptions?.playerVars?.controls).toBe(0);
        expect(screen.getByTestId('preview-player-yt-mount')).toBeInTheDocument();
        expect(screen.getByTestId('preview-player-scrubber')).toBeInTheDocument();
        expect(screen.getByTestId('preview-player-volume')).toBeInTheDocument();
        expect(screen.getByTestId('preview-player-fullscreen')).toBeInTheDocument();
        expect(screen.queryByRole('button', { name: /pause/i })).not.toBeInTheDocument();
    });

    it('resolves a youtube player from an embedKind:"youtube" preview', async () => {
        const user = userEvent.setup();
        render(<PreviewPlayer preview={{ url: 'https://youtu.be/ABC987', embedKind: 'youtube', kind: 'teaser' }} />);
        await user.click(screen.getByTestId('preview-player-facade'));
        await waitFor(() => expect(lastPlayer).not.toBeNull());
        expect(lastOptions?.videoId).toBe('ABC987');
    });

    it('renders plain text for a non-http preview with no embedKind', () => {
        render(<PreviewPlayer preview={{ url: 'magnet:?xt=urn:zzz', kind: 'clip', title: 'Raw clip' }} />);
        const label = screen.getByTestId('preview-player-external');
        expect(label.tagName).toBe('SPAN');
        expect(label).not.toHaveAttribute('href');
        expect(label).toHaveTextContent('Raw clip');
        expect(screen.queryByTestId('preview-player-facade')).not.toBeInTheDocument();
    });

    it('wide fit: youtube mute is reflected as a dimmed volume control', async () => {
        const user = userEvent.setup();
        render(<PreviewPlayer preview={{ url: 'https://www.youtube.com/watch?v=KEY123', kind: 'trailer' }} />);
        await user.click(screen.getByTestId('preview-player-facade'));
        await waitFor(() => expect(lastPlayer).not.toBeNull());
        await waitFor(() => expect(lastPlayer?.ready).toBe(true));

        act(() => {
            lastPlayer!.muted = true;
        });

        const volume = screen.getByTestId('preview-player-volume');
        await waitFor(() => expect(volume).toHaveAttribute('data-muted'));
        expect(screen.getByTestId('preview-player-volume-toggle')).toHaveAttribute('aria-label', 'Unmute');
    });

    it('renders plain text for an unparseable url', () => {
        render(<PreviewPlayer preview={{ url: 'magnet:?xt=urn:foo', kind: 'clip', title: 'Behind the scenes' }} />);
        const label = screen.getByTestId('preview-player-external');
        expect(label.tagName).toBe('SPAN');
        expect(label).not.toHaveAttribute('href');
        expect(label).toHaveTextContent('Behind the scenes');
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

    describe('poster fit -- youtube (IFrame API)', () => {
        beforeEach(() => {
            lastPlayer = null;
            lastOptions = null;
            deferReady = false;
        });

        afterEach(() => {
            deferReady = false;
            vi.restoreAllMocks();
        });

        const renderYt = () =>
            render(
                <PreviewPlayer
                    preview={{ url: 'https://www.youtube.com/watch?v=KEY123', kind: 'trailer' }}
                    posterUrl="https://example.com/poster.jpg"
                    fit="poster"
                />,
            );

        it('idle shows the poster + play button and no iframe/video', () => {
            renderYt();
            expect(screen.getByTestId('preview-player-facade')).toBeInTheDocument();
            expect(screen.queryByTestId('preview-player-iframe')).not.toBeInTheDocument();
            expect(screen.queryByTestId('preview-player-video')).not.toBeInTheDocument();
        });

        it('clicking play creates the IFrame player (controls:0) and renders scrubber + volume but no play/pause', async () => {
            const user = userEvent.setup();
            renderYt();
            await user.click(screen.getByTestId('preview-player-facade'));

            await waitFor(() => expect(lastPlayer).not.toBeNull());
            expect(lastOptions?.host).toBe('https://www.youtube-nocookie.com');
            expect(lastOptions?.videoId).toBe('KEY123');
            expect(lastOptions?.playerVars?.controls).toBe(0);
            expect(lastOptions?.playerVars?.autoplay).toBe(1);
            expect(lastPlayer?.playCalled).toBe(true);

            expect(screen.getByTestId('preview-player-scrubber')).toBeInTheDocument();
            expect(screen.getByTestId('preview-player-volume')).toBeInTheDocument();
            expect(screen.queryByRole('button', { name: /pause/i })).not.toBeInTheDocument();
            expect(screen.queryByRole('button', { name: /^play/i })).not.toBeInTheDocument();
            expect(screen.queryByTestId('preview-player-facade')).not.toBeInTheDocument();
        });

        it('firing ENDED returns to idle (poster + play button)', async () => {
            const user = userEvent.setup();
            renderYt();
            await user.click(screen.getByTestId('preview-player-facade'));
            await waitFor(() => expect(lastPlayer).not.toBeNull());

            act(() => lastPlayer!.fireEnded());

            await waitFor(() => expect(screen.getByTestId('preview-player-facade')).toBeInTheDocument());
            expect(lastPlayer?.destroyed).toBe(true);
        });

        it('clicking the stop target stops + destroys the player and returns to idle', async () => {
            const user = userEvent.setup();
            renderYt();
            await user.click(screen.getByTestId('preview-player-facade'));
            await waitFor(() => expect(lastPlayer).not.toBeNull());

            await user.click(screen.getByTestId('preview-player-stop-target'));

            expect(lastPlayer?.stopped).toBe(true);
            expect(lastPlayer?.destroyed).toBe(true);
            await waitFor(() => expect(screen.getByTestId('preview-player-facade')).toBeInTheDocument());
        });

        it('adjusting the controls does not stop playback (stopPropagation)', async () => {
            const user = userEvent.setup();
            renderYt();
            await user.click(screen.getByTestId('preview-player-facade'));
            await waitFor(() => expect(lastPlayer).not.toBeNull());

            await user.click(screen.getByTestId('preview-player-controls'));
            expect(screen.getByTestId('preview-player-controls')).toBeInTheDocument();
            expect(lastPlayer?.destroyed).toBe(false);
        });

        it('seeking calls seekTo and volume calls unMute + setVolume', async () => {
            const user = userEvent.setup();
            renderYt();
            await user.click(screen.getByTestId('preview-player-facade'));
            await waitFor(() => expect(lastPlayer).not.toBeNull());

            const scrubber = screen.getByTestId('preview-player-scrubber') as HTMLInputElement;
            fireRangeChange(scrubber, '30');
            expect(lastPlayer?.seekTo).toHaveBeenCalledWith(30, true);

            const volume = screen.getByTestId('preview-player-volume-slider') as HTMLInputElement;
            fireRangeChange(volume, '0.8');
            expect(lastPlayer?.unMute).toHaveBeenCalled();
            expect(lastPlayer?.setVolume).toHaveBeenCalledWith(80);
        });

        it('unmount destroys the player and clears the poll interval', async () => {
            const clearSpy = vi.spyOn(globalThis, 'clearInterval');
            const user = userEvent.setup();
            const { unmount } = renderYt();
            await user.click(screen.getByTestId('preview-player-facade'));
            await waitFor(() => expect(lastPlayer).not.toBeNull());

            unmount();
            expect(lastPlayer?.destroyed).toBe(true);
            expect(clearSpy).toHaveBeenCalled();
        });

        it('on ready, unmutes and sets a default volume so the trailer has sound', async () => {
            const user = userEvent.setup();
            renderYt();
            await user.click(screen.getByTestId('preview-player-facade'));
            await waitFor(() => expect(lastPlayer).not.toBeNull());
            await waitFor(() => expect(lastPlayer?.ready).toBe(true));

            expect(lastPlayer?.unMute).toHaveBeenCalled();
            expect(lastPlayer?.setVolume).toHaveBeenCalledWith(100);
            expect(lastPlayer?.muted).toBe(false);
            const slider = screen.getByTestId('preview-player-volume-slider') as HTMLInputElement;
            expect(slider.value).toBe('1');
        });

        it('reflects an autoplay-forced mute in a dimmed control and unmutes on click', async () => {
            const user = userEvent.setup();
            renderYt();
            await user.click(screen.getByTestId('preview-player-facade'));
            await waitFor(() => expect(lastPlayer).not.toBeNull());
            await waitFor(() => expect(lastPlayer?.ready).toBe(true));

            act(() => {
                lastPlayer!.muted = true;
            });

            const volume = screen.getByTestId('preview-player-volume');
            await waitFor(() => expect(volume).toHaveAttribute('data-muted'));
            expect(screen.getByTestId('preview-player-volume-toggle')).toHaveAttribute('aria-label', 'Unmute');

            lastPlayer!.unMute.mockClear();
            await user.click(screen.getByTestId('preview-player-volume-toggle'));
            expect(lastPlayer?.unMute).toHaveBeenCalled();
            await waitFor(() => expect(volume).not.toHaveAttribute('data-muted'));
        });

        it('does not poll player methods until onReady has fired', async () => {
            deferReady = true;
            const user = userEvent.setup();
            vi.useFakeTimers({ shouldAdvanceTime: true });
            try {
                renderYt();
                await user.click(screen.getByTestId('preview-player-facade'));
                await vi.waitFor(() => expect(lastPlayer).not.toBeNull());
                expect(lastPlayer?.ready).toBe(false);

                await act(async () => {
                    await vi.advanceTimersByTimeAsync(1000);
                });
                expect(lastPlayer?.getCurrentTimeCalls).toBe(0);

                act(() => lastPlayer!.fireReady());
                await act(async () => {
                    await vi.advanceTimersByTimeAsync(600);
                });
                expect(lastPlayer!.getCurrentTimeCalls).toBeGreaterThan(0);
            } finally {
                vi.useRealTimers();
            }
        });

        it('a poll tick after destroy is a no-op (does not throw)', async () => {
            const user = userEvent.setup();
            vi.useFakeTimers({ shouldAdvanceTime: true });
            try {
                renderYt();
                await user.click(screen.getByTestId('preview-player-facade'));
                await vi.waitFor(() => expect(lastPlayer).not.toBeNull());
                await vi.waitFor(() => expect(lastPlayer?.ready).toBe(true));

                act(() => lastPlayer!.destroy());
                const before = lastPlayer!.getCurrentTimeCalls;

                await act(async () => {
                    await vi.advanceTimersByTimeAsync(1000);
                });
                expect(lastPlayer!.getCurrentTimeCalls).toBe(before);
            } finally {
                vi.useRealTimers();
            }
        });
    });
});

function fireRangeChange(input: HTMLInputElement, value: string) {
    act(() => {
        const setter = Object.getOwnPropertyDescriptor(window.HTMLInputElement.prototype, 'value')?.set;
        setter?.call(input, value);
        input.dispatchEvent(new Event('change', { bubbles: true }));
    });
}
