import type { CSSProperties } from 'react';

interface ScrubberProps {
    currentTime: number;
    duration: number;
    onSeek: (seconds: number) => void;
}

export function Scrubber({ currentTime, duration, onSeek }: ScrubberProps) {
    const max = duration > 0 ? duration : 0;
    const value = Math.min(currentTime, max || currentTime);
    const fraction = max > 0 ? Math.min(Math.max(value / max, 0), 1) : 0;

    return (
        <input
            type="range"
            className="preview-player__scrubber"
            min={0}
            max={max || 0}
            step={0.1}
            value={value}
            onChange={(event) => onSeek(Number(event.target.value))}
            aria-label="Seek"
            data-testid="preview-player-scrubber"
            disabled={max <= 0}
            style={{ '--progress': fraction } as CSSProperties}
        />
    );
}

export default Scrubber;
