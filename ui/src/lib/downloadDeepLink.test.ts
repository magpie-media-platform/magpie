import { describe, expect, it } from 'vitest';

import { getDownloadStateUrl } from './downloadDeepLink';

describe('getDownloadStateUrl', () => {
    it('maps the downloading widget state to the Downloading qstate token', () => {
        expect(getDownloadStateUrl('downloading')).toBe('/downloads?qstate=Downloading');
    });

    it('maps the queued widget state to the Queued qstate token', () => {
        expect(getDownloadStateUrl('queued')).toBe('/downloads?qstate=Queued');
    });

    it('maps the paused widget state to the Paused qstate token', () => {
        expect(getDownloadStateUrl('paused')).toBe('/downloads?qstate=Paused');
    });

    it('maps the stalled widget state to the Stalled qstate token', () => {
        expect(getDownloadStateUrl('stalled')).toBe('/downloads?qstate=Stalled');
    });
});
