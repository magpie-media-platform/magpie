import { describe, expect, it } from 'vitest';

import { deriveFileQuality } from './deriveFileQuality';

describe('deriveFileQuality', () => {
    it('maps resolution and video codec from the video track', () => {
        const quality = deriveFileQuality({
            tracks: [
                { type: 'Video', name: 'video', codec: 'h264', width: 1920, height: 1080 },
                { type: 'Audio', name: 'audio', channels: 6 },
            ],
        });
        expect(quality).toEqual({ resolution: '1080p', videoCodec: 'h264' });
    });

    it('maps 4K from a tall video track', () => {
        const quality = deriveFileQuality({
            tracks: [{ type: 'Video', name: 'video', codec: 'hevc', height: 2160 }],
        });
        expect(quality).toEqual({ resolution: '4K', videoCodec: 'hevc' });
    });

    it('returns undefined when there is no video track', () => {
        const quality = deriveFileQuality({
            tracks: [{ type: 'Audio', name: 'audio', channels: 2 }],
        });
        expect(quality).toBeUndefined();
    });

    it('returns undefined when media info is absent', () => {
        expect(deriveFileQuality(null)).toBeUndefined();
        expect(deriveFileQuality(undefined)).toBeUndefined();
    });

    it('still derives codec when the video track has no height', () => {
        const quality = deriveFileQuality({
            tracks: [{ type: 'Video', name: 'video', codec: 'vp9' }],
        });
        expect(quality).toEqual({ resolution: undefined, videoCodec: 'vp9' });
    });
});
