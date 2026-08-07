interface YTPlayerVars {
    controls?: 0 | 1;
    modestbranding?: 0 | 1;
    rel?: 0 | 1;
    playsinline?: 0 | 1;
    autoplay?: 0 | 1;
    disablekb?: 0 | 1;
    fs?: 0 | 1;
    iv_load_policy?: 1 | 3;
}

export interface YTPlayerOptions {
    host?: string;
    videoId: string;
    playerVars?: YTPlayerVars;
    events?: {
        onReady?: (event: { target: YTPlayer }) => void;
        onStateChange?: (event: { data: number; target: YTPlayer }) => void;
    };
}

export interface YTPlayer {
    playVideo(): void;
    stopVideo(): void;
    seekTo(seconds: number, allowSeekAhead: boolean): void;
    getCurrentTime(): number;
    getDuration(): number;
    setVolume(volume: number): void;
    getVolume(): number;
    mute(): void;
    unMute(): void;
    isMuted(): boolean;
    destroy(): void;
}

export interface YTNamespace {
    Player: new (element: HTMLElement | string, options: YTPlayerOptions) => YTPlayer;
    PlayerState: {
        ENDED: number;
        PLAYING: number;
        PAUSED: number;
        BUFFERING: number;
        CUED: number;
        UNSTARTED: number;
    };
}

declare global {
    interface Window {
        YT?: YTNamespace;
        onYouTubeIframeAPIReady?: () => void;
    }
}

const IFRAME_API_SRC = 'https://www.youtube.com/iframe_api';

let readyPromise: Promise<YTNamespace> | null = null;

export function loadYouTubeIframeApi(): Promise<YTNamespace> {
    if (typeof window === 'undefined') {
        return Promise.reject(new Error('YouTube IFrame API unavailable outside the browser'));
    }

    if (window.YT?.Player) {
        return Promise.resolve(window.YT);
    }

    if (readyPromise) {
        return readyPromise;
    }

    readyPromise = new Promise<YTNamespace>((resolve, reject) => {
        const previous = window.onYouTubeIframeAPIReady;
        window.onYouTubeIframeAPIReady = () => {
            previous?.();
            if (window.YT?.Player) {
                resolve(window.YT);
            } else {
                reject(new Error('YouTube IFrame API loaded without a Player constructor'));
            }
        };

        const existing = document.querySelector<HTMLScriptElement>(`script[src="${IFRAME_API_SRC}"]`);
        if (existing) {
            return;
        }

        const script = document.createElement('script');
        script.src = IFRAME_API_SRC;
        script.async = true;
        script.onerror = () => reject(new Error('Failed to load the YouTube IFrame API'));
        const first = document.getElementsByTagName('script')[0];
        if (first?.parentNode) {
            first.parentNode.insertBefore(script, first);
        } else {
            document.head.appendChild(script);
        }
    });

    return readyPromise;
}

export function callPlayer<K extends keyof YTPlayer>(
    player: YTPlayer | null,
    method: K,
    ...args: Parameters<Extract<YTPlayer[K], (...a: never[]) => unknown>>
): ReturnType<Extract<YTPlayer[K], (...a: never[]) => unknown>> | undefined {
    const fn = player?.[method];
    if (typeof fn === 'function') {
        return (fn as (...a: unknown[]) => unknown).apply(player, args) as ReturnType<
            Extract<YTPlayer[K], (...a: never[]) => unknown>
        >;
    }
    return undefined;
}
