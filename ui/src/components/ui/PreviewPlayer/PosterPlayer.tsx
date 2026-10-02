import { useCallback, useEffect, useRef, useState } from 'react';
import { flushSync } from 'react-dom';
import { PlayIcon, CornersOutIcon, StopIcon } from '@phosphor-icons/react';

import { Scrubber } from './Scrubber';
import { VolumeControl } from './VolumeControl';
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

export interface PosterPlayerProps {
    mimeType?: string;
    directUrl: string | null;
    youTubeId: string | null;
    posterUrl?: string;
    label: string;
    fit?: 'wide' | 'poster';
}

export function PosterPlayer({ mimeType, directUrl, youTubeId, posterUrl, label, fit = 'wide' }: PosterPlayerProps) {
    if (fit === 'poster') {
        return (
            <PlayablePoster
                key={youTubeId ?? directUrl ?? ''}
                mimeType={mimeType}
                directUrl={directUrl}
                youTubeId={youTubeId}
                posterUrl={posterUrl}
                label={label}
            />
        );
    }

    return (
        <WidePosterPlayer
            mimeType={mimeType}
            directUrl={directUrl}
            youTubeId={youTubeId}
            posterUrl={posterUrl}
            label={label}
        />
    );
}

function PlayablePoster({ mimeType, directUrl, youTubeId, posterUrl, label }: Omit<PosterPlayerProps, 'fit'>) {
    const [playing, setPlaying] = useState(false);
    const [failed, setFailed] = useState(false);
    const handleEnded = useCallback(() => setPlaying(false), []);
    const handleFailed = useCallback(() => {
        setPlaying(false);
        setFailed(true);
    }, []);
    // iOS starts a video only from play() inside the click, so the video mounts and starts before the click returns.
    const handlePlay = useCallback(() => flushSync(() => setPlaying(true)), []);

    return (
        <div className="preview-player__poster-stage" data-testid="preview-player-poster-stage">
            {posterUrl ? (
                <img className="preview-player__thumbnail" src={posterUrl} alt="" loading="lazy" aria-hidden="true" />
            ) : null}
            {playing && youTubeId ? (
                <div className="preview-player__yt-mount">
                    {/* eslint-disable-next-line react/iframe-missing-sandbox -- allow-same-origin keeps the YouTube player on its own origin, which it needs for playback; the frame is cross-origin, so it still cannot reach Magpie. */}
                    <iframe
                        title={label}
                        src={`https://www.youtube-nocookie.com/embed/${encodeURIComponent(youTubeId)}?autoplay=1&mute=1&controls=0&disablekb=1&fs=0&playsinline=1&rel=0`}
                        sandbox="allow-scripts allow-same-origin allow-presentation"
                        allow="autoplay; encrypted-media"
                        tabIndex={-1}
                        aria-hidden="true"
                        referrerPolicy="strict-origin-when-cross-origin"
                        data-testid="preview-player-iframe"
                    />
                </div>
            ) : null}
            {playing && !youTubeId ? (
                <MutedPosterVideo
                    url={directUrl ?? ''}
                    mimeType={mimeType}
                    onEnded={handleEnded}
                    onFailed={handleFailed}
                />
            ) : null}
            {failed ? null : (
                <button
                    type="button"
                    className="preview-player__facade"
                    onClick={handlePlay}
                    aria-label={`Play ${label}`}
                    aria-pressed={playing}
                    data-testid="preview-player-facade"
                >
                    <span className="preview-player__play preview-player__play--hero" aria-hidden="true">
                        <PlayIcon weight="fill" />
                    </span>
                </button>
            )}
        </div>
    );
}

function MutedPosterVideo({
    url,
    mimeType,
    onEnded,
    onFailed,
}: {
    url: string;
    mimeType?: string;
    onEnded: () => void;
    onFailed: () => void;
}) {
    const start = useCallback(
        (video: HTMLVideoElement | null) => {
            if (!video) {
                return;
            }
            video.defaultMuted = true;
            video.muted = true;
            video.volume = 0;
            video.play().catch(onFailed);
        },
        [onFailed],
    );

    return (
        <video
            ref={start}
            className="preview-player__video preview-player__video--cover"
            muted
            autoPlay
            playsInline
            controls={false}
            tabIndex={-1}
            aria-hidden="true"
            disablePictureInPicture
            disableRemotePlayback
            data-testid="preview-player-video"
            onVolumeChange={(event) => {
                const video = event.currentTarget;
                if (!video.muted) {
                    video.muted = true;
                }
                if (video.volume !== 0) {
                    video.volume = 0;
                }
            }}
            onEnded={onEnded}
            onError={onFailed}
        >
            <source src={url} type={mimeType} onError={onFailed} />
        </video>
    );
}

function WidePosterPlayer({ mimeType, directUrl, youTubeId, posterUrl, label }: Omit<PosterPlayerProps, 'fit'>) {
    const [playing, setPlaying] = useState(false);
    const facadeRef = useRef<HTMLButtonElement>(null);
    const startedRef = useRef(false);
    useEffect(() => {
        if (playing) {
            startedRef.current = true;
        } else if (startedRef.current) {
            facadeRef.current?.focus();
        }
    }, [playing]);

    const handlePlay = useCallback(() => setPlaying(true), []);
    const handleStop = useCallback(() => setPlaying(false), []);

    if (playing) {
        return (
            <div
                className="preview-player__poster-stage"
                data-testid="preview-player-poster-stage"
                onKeyDown={(event) => {
                    if (event.key === 'Escape') {
                        event.stopPropagation();
                        handleStop();
                    }
                }}
            >
                {youTubeId ? (
                    <YouTubePosterPlayer videoId={youTubeId} label={label} onStop={handleStop} />
                ) : (
                    <DirectVideoPosterPlayer mimeType={mimeType} url={directUrl ?? ''} onStop={handleStop} />
                )}
            </div>
        );
    }

    const thumbnail = posterUrl ?? (youTubeId ? `https://i.ytimg.com/vi/${youTubeId}/hqdefault.jpg` : undefined);

    return (
        <div className="preview-player__poster-stage" data-testid="preview-player-poster-stage">
            <button
                ref={facadeRef}
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
                <span className="preview-player__play" aria-hidden="true">
                    <PlayIcon weight="fill" />
                </span>
                <span className="preview-player__label">{label}</span>
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
    onStop,
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
    onStop: () => void;
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
                <button
                    type="button"
                    className="preview-player__stop"
                    onClick={onStop}
                    aria-label="Stop preview"
                    autoFocus
                >
                    <StopIcon weight="bold" aria-hidden="true" />
                </button>
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

function DirectVideoPosterPlayer({ url, mimeType, onStop }: { url: string; mimeType?: string; onStop: () => void }) {
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
        <div className="preview-player__stop-target">
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
                <source src={url} type={mimeType} />
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
                onStop={handleStop}
                onFullscreen={handleFullscreen}
            />
        </div>
    );
}

function YouTubePosterPlayer({ videoId, label, onStop }: { videoId: string; label: string; onStop: () => void }) {
    return (
        <>
            <div className="preview-player__yt-mount">
                {/* eslint-disable-next-line react/iframe-missing-sandbox -- allow-same-origin keeps the YouTube player on its own origin, which it needs for playback; the frame is cross-origin, so it still cannot reach Magpie. */}
                <iframe
                    title={label}
                    src={`https://www.youtube-nocookie.com/embed/${encodeURIComponent(videoId)}?autoplay=1&playsinline=1&rel=0`}
                    sandbox="allow-scripts allow-same-origin allow-presentation"
                    allow="autoplay; encrypted-media; fullscreen"
                    allowFullScreen
                    referrerPolicy="strict-origin-when-cross-origin"
                    data-testid="preview-player-iframe"
                />
            </div>
            <button
                type="button"
                className="preview-player__fullscreen preview-player__youtube-stop"
                onClick={onStop}
                aria-label="Stop preview"
                autoFocus
            >
                <StopIcon weight="fill" aria-hidden="true" />
            </button>
        </>
    );
}

export default PosterPlayer;
