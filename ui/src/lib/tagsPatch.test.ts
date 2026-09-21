import { describe, expect, it } from 'vitest';

import { tagsPatch } from './tagsPatch';

describe('tagsPatch', () => {
    it('returns undefined when the lists agree', () => {
        // Act
        const patch = tagsPatch(['4k', 'anime'], ['anime', '4k']);

        // Assert
        expect(patch).toBeUndefined();
    });

    it('omits the empty side when only tags were added', () => {
        // Act
        const patch = tagsPatch(['4k', 'anime'], ['4k']);

        // Assert
        expect(patch).toEqual({ add: ['anime'] });
    });

    it('carries both sides when tags were added and removed', () => {
        // Act
        const patch = tagsPatch(['anime'], ['4k']);

        // Assert
        expect(patch).toEqual({ add: ['anime'], remove: ['4k'] });
    });
});
