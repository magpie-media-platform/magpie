import { describe, expect, it } from 'vitest';

import { safeHttpUrl } from './safeUrl';

describe('safeHttpUrl', () => {
    it('returns the value for an https URL', () => {
        expect(safeHttpUrl('https://indexer.example/release/123')).toBe('https://indexer.example/release/123');
    });

    it('returns the value for an http URL', () => {
        expect(safeHttpUrl('http://indexer.example/release/123')).toBe('http://indexer.example/release/123');
    });

    it('trims and normalizes an http URL', () => {
        expect(safeHttpUrl(' https://indexer.example ')).toBe('https://indexer.example/');
    });

    it('returns null for a javascript: URL', () => {
        expect(safeHttpUrl('javascript:alert(1)')).toBeNull();
    });

    it('returns null for a data: URL', () => {
        expect(safeHttpUrl('data:text/html,<script>alert(1)</script>')).toBeNull();
    });

    it('returns null for a non-absolute string', () => {
        expect(safeHttpUrl('not a url')).toBeNull();
    });

    it('returns null for an empty string', () => {
        expect(safeHttpUrl('')).toBeNull();
    });

    it('returns null for null', () => {
        expect(safeHttpUrl(null)).toBeNull();
    });

    it('returns null for undefined', () => {
        expect(safeHttpUrl(undefined)).toBeNull();
    });
});
