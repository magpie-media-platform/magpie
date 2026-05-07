import { describe, expect, it } from 'vitest';

import { formatSize } from './formatSize';

describe('formatSize', () => {
    it('returns 0 B for zero bytes', () => {
        expect(formatSize(0)).toBe('0 B');
    });

    it('formats bytes', () => {
        expect(formatSize(500)).toBe('500 B');
    });

    it('formats kilobytes', () => {
        expect(formatSize(1024)).toBe('1.0 KB');
    });

    it('formats megabytes', () => {
        expect(formatSize(1048576)).toBe('1.0 MB');
    });

    it('formats gigabytes', () => {
        expect(formatSize(1073741824)).toBe('1.0 GB');
    });

    it('formats terabytes', () => {
        expect(formatSize(1099511627776)).toBe('1.0 TB');
    });

    it('clamps index for values exceeding TB range', () => {
        const petabyte = 1024 * 1099511627776;
        const result = formatSize(petabyte);
        expect(result).toBe('1024.0 TB');
    });

    it('clamps index for extremely large values', () => {
        const exabyte = 1024 * 1024 * 1099511627776;
        const result = formatSize(exabyte);
        expect(result).toContain('TB');
    });
});
