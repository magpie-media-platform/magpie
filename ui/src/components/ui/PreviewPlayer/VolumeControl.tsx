import type { CSSProperties } from 'react';

import { SpeakerHighIcon, SpeakerSimpleXIcon } from '@phosphor-icons/react';

interface VolumeControlProps {
    volume: number;
    muted: boolean;
    disabled?: boolean;
    onVolumeChange: (volume: number) => void;
    onToggleMute: () => void;
}

export function VolumeControl({ volume, muted, disabled = false, onVolumeChange, onToggleMute }: VolumeControlProps) {
    const isMuted = disabled || muted || volume === 0;
    const effective = isMuted ? 0 : volume;

    return (
        <div
            className="preview-player__volume"
            data-testid="preview-player-volume"
            data-muted={isMuted ? '' : undefined}
            data-disabled={disabled ? '' : undefined}
        >
            <button
                type="button"
                className="preview-player__volume-toggle"
                onClick={disabled ? undefined : onToggleMute}
                disabled={disabled}
                aria-disabled={disabled || undefined}
                aria-label={isMuted ? 'Unmute' : 'Mute'}
                data-testid="preview-player-volume-toggle"
            >
                {isMuted ? (
                    <SpeakerSimpleXIcon weight="fill" aria-hidden="true" />
                ) : (
                    <SpeakerHighIcon weight="fill" aria-hidden="true" />
                )}
            </button>
            <input
                type="range"
                className="preview-player__volume-slider"
                min={0}
                max={1}
                step={0.01}
                value={effective}
                disabled={disabled}
                onChange={(event) => onVolumeChange(Number(event.target.value))}
                aria-label="Volume"
                data-testid="preview-player-volume-slider"
                style={{ '--progress': effective } as CSSProperties}
            />
        </div>
    );
}

export default VolumeControl;
