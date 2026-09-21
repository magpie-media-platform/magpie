import { describe, expect, it } from 'vitest';

import { buildContentLevelPaths } from './contentLevelPaths';

describe('buildContentLevelPaths', () => {
    it('keeps branches, standalone levels and missing-parent levels in sorted paths', () => {
        // Arrange
        const levels = [
            { slug: 'root' },
            { slug: 'b', parentSlug: 'root' },
            { slug: 'a', parentSlug: 'root' },
            { slug: 'solo' },
            { slug: 'orphan', parentSlug: 'missing' },
        ];
        // Act
        const paths = buildContentLevelPaths(levels);
        // Assert
        expect(paths).toEqual([
            [{ slug: 'orphan', parentSlug: 'missing' }],
            [{ slug: 'root' }, { slug: 'a', parentSlug: 'root' }],
            [{ slug: 'root' }, { slug: 'b', parentSlug: 'root' }],
            [{ slug: 'solo' }],
        ]);
    });
});
