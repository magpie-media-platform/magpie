interface FormatFileSizeOptions {
    units?: string[];
    decimals?: number | ((index: number) => number);
    trimTrailingZeros?: boolean;
}

const DEFAULT_UNITS = ['B', 'KB', 'MB', 'GB', 'TB'];

export function formatFileSize(bytes: number, options: FormatFileSizeOptions = {}): string {
    const units = options.units ?? DEFAULT_UNITS;
    const trimTrailingZeros = options.trimTrailingZeros ?? true;

    if (bytes === 0) {
        return `0 ${units[0]}`;
    }

    const k = 1024;
    const i = Math.min(Math.floor(Math.log(bytes) / Math.log(k)), units.length - 1);
    const value = bytes / Math.pow(k, i);
    const decimals = typeof options.decimals === 'function' ? options.decimals(i) : (options.decimals ?? 2);
    const fixed = value.toFixed(decimals);

    return `${trimTrailingZeros ? parseFloat(fixed) : fixed} ${units[i]}`;
}

export function formatPrimaryMediaSummary(fileCount?: number, size?: number): string {
    const count = fileCount ?? '—';
    const fileLabel = fileCount === 1 ? 'file' : 'files';
    const formattedSize = size === undefined ? '—' : formatFileSize(size);
    return `${count} ${fileLabel} · ${formattedSize}`;
}

export function formatBackupSize(bytes: number): string {
    return formatFileSize(bytes, {
        decimals: (i) => (i === 0 ? 0 : 1),
        trimTrailingZeros: false,
    });
}

export function formatDiskSpaceSize(bytes: number): string {
    return formatFileSize(bytes, {
        units: ['B', 'KB', 'MB', 'GB', 'TB', 'PB'],
        decimals: (i) => (i >= 3 ? 2 : i >= 2 ? 1 : 0),
        trimTrailingZeros: false,
    });
}
