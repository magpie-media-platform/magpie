import { type ReactNode } from 'react';

import { safeHttpUrl } from '@/lib/safeUrl';

import { parseYouTubeId } from './parseYouTubeId';
import { PosterPlayer } from './PosterPlayer';

import './PreviewPlayer.css';

export interface PreviewClip {
    url: string;

    kind?: string | null;

    title?: string | null;

    mimeType?: string | null;

    embedKind?: string | null;
}

export type PreviewPlayerFit = 'wide' | 'poster';

export interface PreviewPlayerProps {
    preview: PreviewClip;

    posterUrl?: string;

    fit?: PreviewPlayerFit;

    className?: string;
}

function resolveYouTubeId(preview: PreviewClip): string | null {
    const direct = parseYouTubeId(preview.url);
    if (direct) {
        return direct;
    }
    if (preview.embedKind?.toLowerCase() === 'youtube') {
        return parseYouTubeId(preview.url);
    }
    return null;
}

function previewLabel(preview: PreviewClip): string {
    if (preview.title && preview.title.trim()) {
        return preview.title.trim();
    }
    if (preview.kind && preview.kind.trim()) {
        const kind = preview.kind.trim();
        return kind.charAt(0).toUpperCase() + kind.slice(1);
    }
    return 'Preview';
}

function AspectBox({ children, className, fit }: { children: ReactNode; className?: string; fit: PreviewPlayerFit }) {
    const classes = ['preview-player', fit === 'poster' && 'preview-player--poster', className]
        .filter(Boolean)
        .join(' ');
    return (
        <div className={classes} data-component="preview-player" data-fit={fit}>
            {children}
        </div>
    );
}

export function PreviewPlayer({ preview, posterUrl, fit = 'wide', className }: PreviewPlayerProps) {
    const label = previewLabel(preview);
    const youTubeId = resolveYouTubeId(preview);
    const previewUrl = safeHttpUrl(preview.url);
    const isDirectVideo = !!previewUrl;

    if (youTubeId || isDirectVideo) {
        return (
            <AspectBox className={className} fit={fit}>
                <PosterPlayer
                    directUrl={youTubeId ? null : previewUrl}
                    youTubeId={youTubeId}
                    posterUrl={posterUrl}
                    label={label}
                    fit={fit}
                />
            </AspectBox>
        );
    }

    return (
        <span
            className={['preview-player__external', 'preview-player__external--text', className]
                .filter(Boolean)
                .join(' ')}
            data-testid="preview-player-external"
            data-component="preview-player"
        >
            <span>{label}</span>
        </span>
    );
}

PreviewPlayer.displayName = 'PreviewPlayer';

export default PreviewPlayer;
