import { describe, expect, it } from 'vitest';

import { fileName } from './importDisplay';

describe('importDisplay', () => {
    it.each([
        ['/archive/example.mkv', 'example.mkv'],
        ['D:\\archive\\example.mkv', 'example.mkv'],
        ['/archive/', '/archive/'],
        ['', ''],
    ])('FileName_ReturnsLastSegment_WhenPathProvided %s', (path, expected) => {
        // Act
        const name = fileName(path);

        // Assert
        expect(name).toBe(expected);
    });
});
