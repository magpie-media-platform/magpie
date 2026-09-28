import { describe, expect, it } from 'vitest';

import { canonicalizeDownloadSearchParams } from './downloadUrlState';

describe('canonicalizeDownloadSearchParams', () => {
    it('removes invalid status, sort, page, and tab params', () => {
        const params = new URLSearchParams(
            'tab=nope&qstate=Finished&hstate=Paused&qsort=missing:desc&hsort=title:sideways&qpage=0&hpage=abc',
        );

        const canonical = canonicalizeDownloadSearchParams(params);

        expect(canonical?.toString()).toBe('');
    });

    it('normalizes case and whitespace for supported params', () => {
        const params = new URLSearchParams(
            'tab= HISTORY &qstate= paused &hstate=cancelled&qsort=LASTSTATUS:ASC&hsort=TITLE:DESC&qpage=002',
        );

        const canonical = canonicalizeDownloadSearchParams(params);

        expect(canonical?.toString()).toBe(
            'tab=history&qstate=Paused&hstate=Cancelled&qsort=lastStatus%3Aasc&hsort=title%3Adesc&qpage=2',
        );
    });

    it('keeps the faulted history filter', () => {
        const params = new URLSearchParams('tab=history&hstate=faulted');

        const canonical = canonicalizeDownloadSearchParams(params);

        expect(canonical?.toString()).toBe('tab=history&hstate=Faulted');
    });

    it('removes default tab and sort params', () => {
        const params = new URLSearchParams('tab=queue&qsort=added:desc&hsort=completed:desc');

        const canonical = canonicalizeDownloadSearchParams(params);

        expect(canonical?.toString()).toBe('');
    });
});
