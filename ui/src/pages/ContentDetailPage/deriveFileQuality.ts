import type { FileQuality } from '@/components/content/ContentTabs';
import type { ApiMediaInfo } from '@/types/content';

function resolutionLabel(height: number): string {
    if (height >= 2000) {
        return '4K';
    }
    if (height >= 1080) {
        return '1080p';
    }
    if (height >= 720) {
        return '720p';
    }
    if (height >= 480) {
        return '480p';
    }
    return `${height}p`;
}

export function deriveFileQuality(mediaInfo: ApiMediaInfo | null | undefined): FileQuality | undefined {
    const video = mediaInfo?.tracks?.find((t) => t.type?.toLowerCase() === 'video');
    if (!video) {
        return undefined;
    }

    const resolution = video.height != null ? resolutionLabel(video.height) : undefined;
    const videoCodec = video.codec ?? undefined;
    if (!resolution && !videoCodec) {
        return undefined;
    }

    return { resolution, videoCodec };
}
