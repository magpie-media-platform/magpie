import { describe, expect, it } from 'vitest';

import { formatBackupSize, formatDiskSpaceSize, formatFileSize } from './formatSize';

describe('formatFileSize', () => {
    it('returns 0 B for zero bytes', () => {
        expect(formatFileSize(0)).toBe('0 B');
    });

    it('formats bytes', () => {
        expect(formatFileSize(500)).toBe('500 B');
    });

    it('formats kilobytes', () => {
        expect(formatFileSize(1024)).toBe('1 KB');
    });

    it('formats megabytes', () => {
        expect(formatFileSize(1048576)).toBe('1 MB');
    });

    it('formats gigabytes', () => {
        expect(formatFileSize(1073741824)).toBe('1 GB');
    });

    it('formats terabytes', () => {
        expect(formatFileSize(1099511627776)).toBe('1 TB');
    });

    it('clamps index for values exceeding TB range', () => {
        const petabyte = 1024 * 1099511627776;
        const result = formatFileSize(petabyte);
        expect(result).toBe('1024 TB');
    });

    it('clamps index for extremely large values', () => {
        const exabyte = 1024 * 1024 * 1099511627776;
        const result = formatFileSize(exabyte);
        expect(result).toContain('TB');
    });
});

describe('formatBackupSize', () => {
    it('returns 0 B for zero bytes', () => {
        expect(formatBackupSize(0)).toBe('0 B');
    });

    it('formats bytes', () => {
        expect(formatBackupSize(500)).toBe('500 B');
    });

    it('formats kilobytes with one decimal place', () => {
        expect(formatBackupSize(1024)).toBe('1.0 KB');
    });

    it('formats megabytes with one decimal place', () => {
        expect(formatBackupSize(1048576)).toBe('1.0 MB');
    });

    it('formats gigabytes with one decimal place', () => {
        expect(formatBackupSize(1073741824)).toBe('1.0 GB');
    });

    it('formats terabytes with one decimal place', () => {
        expect(formatBackupSize(1099511627776)).toBe('1.0 TB');
    });

    it('clamps index for values exceeding TB range', () => {
        const petabyte = 1024 * 1099511627776;
        const result = formatBackupSize(petabyte);
        expect(result).toBe('1024.0 TB');
    });

    it('clamps index for extremely large values', () => {
        const exabyte = 1024 * 1024 * 1099511627776;
        const result = formatBackupSize(exabyte);
        expect(result).toContain('TB');
    });
});

describe('formatDiskSpaceSize', () => {
    it('formats bytes and kilobytes without decimals', () => {
        expect(formatDiskSpaceSize(500)).toBe('500 B');
        expect(formatDiskSpaceSize(1024)).toBe('1 KB');
    });

    it('formats megabytes with one decimal place', () => {
        expect(formatDiskSpaceSize(1048576)).toBe('1.0 MB');
    });

    it('formats gigabytes and larger with two decimal places', () => {
        expect(formatDiskSpaceSize(1073741824)).toBe('1.00 GB');
        expect(formatDiskSpaceSize(1099511627776)).toBe('1.00 TB');
    });

    it('supports petabytes', () => {
        expect(formatDiskSpaceSize(1125899906842624)).toBe('1.00 PB');
    });
});
