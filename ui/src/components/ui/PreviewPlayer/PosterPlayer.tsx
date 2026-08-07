import { useCallback, useEffect, useRef, useState } from 'react';
import { PlayIcon, CornersOutIcon } from '@phosphor-icons/react';

import { Scrubber } from './Scrubber';
import { VolumeControl } from './VolumeControl';
import { callPlayer, loadYouTubeIframeApi, type YTPlayer } from './youtubeApi';
import { formatPlaybackTime } from '@/lib/formatTime';

function requestFullscreenSafe(element: Element | null): void {
    if (!element) {
        return;
    }
    const el = element as Element & { requestFullscreen?: () => Promise<void> };
    if (typeof el.requestFullscreen === 'function') {
        void Promise.resolve(el.requestFullscreen()).catch(() => {});
    }
}

const YT_POLL_INTERVAL_MS = 250;

const DEFAULT_VOLUME = 100;

export interface PosterPlayerProps {
    directUrl: string | null;
    youTubeId: string | null;
    posterUrl?: string;
    label: string;
    fit?: 'wide' | 'poster';
}

export function PosterPlayer({ directUrl, youTubeId, posterUrl, label, fit = 'wide' }: PosterPlayerProps) {
    const [playing, setPlaying] = useState(false);

    const handlePlay = useCallback(() => setPlaying(true), []);
    const handleStop = useCallback(() => setPlaying(false), []);

    if (playing) {
        return (
            <div className="preview-player__poster-stage" data-testid="preview-player-poster-stage">
                {youTubeId ? (
                    <YouTubePosterPlayer videoId={youTubeId} label={label} onStop={handleStop} />
                ) : (
                    <DirectVideoPosterPlayer url={directUrl ?? ''} onStop={handleStop} />
                )}
            </div>
        );
    }

    const thumbnail =
        posterUrl ?? (fit === 'wide' && youTubeId ? `https://i.ytimg.com/vi/${youTubeId}/hqdefault.jpg` : undefined);

    return (
        <div className="preview-player__poster-stage" data-testid="preview-player-poster-stage">
            <button
                type="button"
                className="preview-player__facade"
                onClick={handlePlay}
                aria-label={`Play ${label}`}
                data-testid="preview-player-facade"
            >
                {thumbnail ? (
                    <img
                        className="preview-player__thumbnail"
                        src={thumbnail}
                        alt=""
                        loading="lazy"
                        aria-hidden="true"
                    />
                ) : null}
                {fit === 'poster' ? (
                    <span className="preview-player__play preview-player__play--hero" aria-hidden="true">
                        <PlayIcon weight="fill" />
                    </span>
                ) : (
                    <>
                        <span className="preview-player__play" aria-hidden="true">
                            <PlayIcon weight="fill" />
                        </span>
                        <span className="preview-player__label">{label}</span>
                    </>
                )}
            </button>
        </div>
    );
}

function PosterControls({
    currentTime,
    duration,
    volume,
    muted,
    noAudio = false,
    onSeek,
    onVolumeChange,
    onToggleMute,
    onFullscreen,
}: {
    currentTime: number;
    duration: number;
    volume: number;
    muted: boolean;
    noAudio?: boolean;
    onSeek: (seconds: number) => void;
    onVolumeChange: (volume: number) => void;
    onToggleMute: () => void;
    onFullscreen: () => void;
}) {
    const showTime = duration > 0;

    return (
        <div
            className="preview-player__controls"
            data-testid="preview-player-controls"
            onClick={(event) => event.stopPropagation()}
            onPointerDown={(event) => event.stopPropagation()}
            onMouseDown={(event) => event.stopPropagation()}
        >
            <Scrubber currentTime={currentTime} duration={duration} onSeek={onSeek} />
            <div className="preview-player__controls-row">
                <VolumeControl
                    volume={volume}
                    muted={muted}
                    disabled={noAudio}
                    onVolumeChange={onVolumeChange}
                    onToggleMute={onToggleMute}
                />
                <div className="preview-player__controls-end">
                    {showTime ? (
                        <span className="preview-player__time" data-testid="preview-player-time">
                            {formatPlaybackTime(currentTime)} / {formatPlaybackTime(duration)}
                        </span>
                    ) : null}
                    <button
                        type="button"
                        className="preview-player__fullscreen"
                        onClick={onFullscreen}
                        aria-label="Fullscreen"
                        data-testid="preview-player-fullscreen"
                    >
                        <CornersOutIcon weight="bold" aria-hidden="true" />
                    </button>
                </div>
            </div>
        </div>
    );
}

function DirectVideoPosterPlayer({ url, onStop }: { url: string; onStop: () => void }) {
    const videoRef = useRef<HTMLVideoElement>(null);
    const [currentTime, setCurrentTime] = useState(0);
    const [duration, setDuration] = useState(0);
    const [volume, setVolume] = useState(1);
    const [muted, setMuted] = useState(false);
    const [noAudio, setNoAudio] = useState(true);
    const audioDecidedRef = useRef(false);

    const syncAudioState = useCallback((video: HTMLVideoElement) => {
        setVolume(video.volume);
        setMuted(video.muted || video.volume === 0);
    }, []);

    const evaluateAudio = useCallback((video: HTMLVideoElement) => {
        if (audioDecidedRef.current) {
            return;
        }
        const v = video as HTMLVideoElement & {
            mozHasAudio?: boolean;
            webkitAudioDecodedByteCount?: number;
            audioTracks?: { length: number };
        };
        if (typeof v.mozHasAudio === 'boolean') {
            audioDecidedRef.current = true;
            setNoAudio(!v.mozHasAudio);
            return;
        }
        if (v.audioTracks && v.audioTracks.length > 0) {
            audioDecidedRef.current = true;
            setNoAudio(false);
            return;
        }
        if (typeof v.webkitAudioDecodedByteCount === 'number') {
            if (v.webkitAudioDecodedByteCount > 0) {
                audioDecidedRef.current = true;
                setNoAudio(false);
                return;
            }
            if (!video.muted && video.currentTime > 0.4) {
                audioDecidedRef.current = true;
                setNoAudio(true);
            }
            return;
        }
        if (video.currentTime > 0.5) {
            audioDecidedRef.current = true;
            setNoAudio(false);
        }
    }, []);

    const handleSeek = useCallback((seconds: number) => {
        const video = videoRef.current;
        if (video) {
            video.currentTime = seconds;
            setCurrentTime(seconds);
        }
    }, []);

    const handleVolumeChange = useCallback((next: number) => {
        const video = videoRef.current;
        if (video) {
            video.volume = next;
            video.muted = next === 0;
        }
        setVolume(next);
        setMuted(next === 0);
    }, []);

    const handleToggleMute = useCallback(() => {
        const video = videoRef.current;
        setMuted((prev) => {
            const next = !prev;
            if (video) {
                video.muted = next;
            }
            return next;
        });
    }, []);

    const handleStop = useCallback(() => {
        const video = videoRef.current;
        if (video) {
            video.pause();
        }
        onStop();
    }, [onStop]);

    const handleFullscreen = useCallback(() => {
        requestFullscreenSafe(videoRef.current);
    }, []);

    return (
        <div className="preview-player__stop-target" onClick={handleStop} data-testid="preview-player-stop-target">
            <video
                ref={videoRef}
                className="preview-player__video"
                controls={false}
                autoPlay
                playsInline
                data-testid="preview-player-video"
                onLoadedMetadata={(event) => {
                    setDuration(event.currentTarget.duration || 0);
                    syncAudioState(event.currentTarget);
                }}
                onLoadedData={(event) => evaluateAudio(event.currentTarget)}
                onPlaying={(event) => evaluateAudio(event.currentTarget)}
                onVolumeChange={(event) => syncAudioState(event.currentTarget)}
                onDurationChange={(event) => setDuration(event.currentTarget.duration || 0)}
                onTimeUpdate={(event) => {
                    setCurrentTime(event.currentTarget.currentTime);
                    evaluateAudio(event.currentTarget);
                }}
                onEnded={onStop}
            >
                <source src={url} />
            </video>
            <PosterControls
                currentTime={currentTime}
                duration={duration}
                volume={volume}
                muted={muted}
                noAudio={noAudio}
                onSeek={handleSeek}
                onVolumeChange={handleVolumeChange}
                onToggleMute={handleToggleMute}
                onFullscreen={handleFullscreen}
            />
        </div>
    );
}

function YouTubePosterPlayer({ videoId, label, onStop }: { videoId: string; label: string; onStop: () => void }) {
    const mountRef = useRef<HTMLDivElement>(null);
    const containerRef = useRef<HTMLDivElement>(null);
    const playerRef = useRef<YTPlayer | null>(null);
    const readyRef = useRef(false);
    const intervalRef = useRef<ReturnType<typeof setInterval> | null>(null);
    const onStopRef = useRef(onStop);
    onStopRef.current = onStop;

    const [currentTime, setCurrentTime] = useState(0);
    const [duration, setDuration] = useState(0);
    const [volume, setVolume] = useState(1);
    const [muted, setMuted] = useState(false);

    const clearPoll = useCallback(() => {
        if (intervalRef.current !== null) {
            clearInterval(intervalRef.current);
            intervalRef.current = null;
        }
    }, []);

    const startPoll = useCallback(() => {
        clearPoll();
        intervalRef.current = setInterval(() => {
            if (!readyRef.current) {
                return;
            }
            const p = playerRef.current;
            const t = callPlayer(p, 'getCurrentTime');
            if (typeof t === 'number') {
                setCurrentTime(t);
            }
            const d = callPlayer(p, 'getDuration');
            if (typeof d === 'number' && d > 0) {
                setDuration(d);
            }
            const m = callPlayer(p, 'isMuted');
            const vol = callPlayer(p, 'getVolume');
            if (typeof vol === 'number') {
                setVolume(vol / 100);
            }
            if (typeof m === 'boolean') {
                setMuted(m || vol === 0);
            }
        }, YT_POLL_INTERVAL_MS);
    }, [clearPoll]);

    const teardown = useCallback(() => {
        clearPoll();
        readyRef.current = false;
        const player = playerRef.current;
        playerRef.current = null;
        callPlayer(player, 'destroy');
    }, [clearPoll]);

    useEffect(() => {
        let cancelled = false;

        loadYouTubeIframeApi()
            .then((YT) => {
                if (cancelled || !mountRef.current) {
                    return;
                }
                const player = new YT.Player(mountRef.current, {
                    host: 'https://www.youtube-nocookie.com',
                    videoId,
                    playerVars: {
                        controls: 0,
                        modestbranding: 1,
                        rel: 0,
                        playsinline: 1,
                        autoplay: 1,
                        disablekb: 1,
                        fs: 1,
                        iv_load_policy: 3,
                    },
                    events: {
                        onReady: ({ target }) => {
                            if (cancelled) {
                                return;
                            }
                            readyRef.current = true;
                            callPlayer(target, 'unMute');
                            callPlayer(target, 'setVolume', DEFAULT_VOLUME);
                            const d = callPlayer(target, 'getDuration');
                            if (typeof d === 'number') {
                                setDuration(d);
                            }
                            setVolume(DEFAULT_VOLUME / 100);
                            setMuted(false);
                            callPlayer(target, 'playVideo');
                            startPoll();
                        },
                        onStateChange: ({ data }) => {
                            if (data === YT.PlayerState.ENDED) {
                                teardown();
                                onStopRef.current();
                            }
                        },
                    },
                });
                playerRef.current = player;
            })
            .catch(() => {
                if (!cancelled) {
                    onStopRef.current();
                }
            });

        return () => {
            cancelled = true;
            teardown();
        };
    }, [videoId, teardown, startPoll]);

    const handleSeek = useCallback((seconds: number) => {
        callPlayer(playerRef.current, 'seekTo', seconds, true);
        setCurrentTime(seconds);
    }, []);

    const handleVolumeChange = useCallback((next: number) => {
        const player = playerRef.current;
        callPlayer(player, 'unMute');
        callPlayer(player, 'setVolume', Math.round(next * 100));
        setVolume(next);
        setMuted(next === 0);
    }, []);

    const handleToggleMute = useCallback(() => {
        const player = playerRef.current;
        setMuted((prev) => {
            const next = !prev;
            callPlayer(player, next ? 'mute' : 'unMute');
            return next;
        });
    }, []);

    const handleStop = useCallback(() => {
        callPlayer(playerRef.current, 'stopVideo');
        teardown();
        onStop();
    }, [teardown, onStop]);

    const handleFullscreen = useCallback(() => {
        requestFullscreenSafe(containerRef.current);
    }, []);

    return (
        <div className="preview-player__stop-target" onClick={handleStop} data-testid="preview-player-stop-target">
            <div ref={containerRef} className="preview-player__yt-mount" aria-label={label}>
                <div ref={mountRef} data-testid="preview-player-yt-mount" />
            </div>
            <PosterControls
                currentTime={currentTime}
                duration={duration}
                volume={volume}
                muted={muted}
                onSeek={handleSeek}
                onVolumeChange={handleVolumeChange}
                onToggleMute={handleToggleMute}
                onFullscreen={handleFullscreen}
            />
        </div>
    );
}

export default PosterPlayer;
