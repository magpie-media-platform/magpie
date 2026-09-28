import { describe, expect, it } from 'vitest';

import { describeAffectedEntity } from './affectedEntity';

describe('describeAffectedEntity', () => {
    it('links a hierarchy item token to its detail route with a shortened reference', () => {
        const guid = 'a1fc4c2b-1234-4def-89ab-0123456789ab';

        const display = describeAffectedEntity(`ContentHierarchyItem:${guid}`);

        expect(display).toEqual({ label: 'Hierarchy item', reference: 'a1fc4c2b', to: `/library/${guid}` });
    });

    it('links indexer and download client tokens to their admin pages', () => {
        expect(describeAffectedEntity('IndexerConnection:99f4c2b3-1111-2222-3333-444455556666')?.to).toBe(
            '/admin/indexers',
        );
        expect(describeAffectedEntity('DownloadClient:5cfcabcd-7777-8888-9999-aaaabbbbcccc')?.to).toBe(
            '/admin/download-clients',
        );
    });

    it('renders a readable label without a route for an unknown type', () => {
        const display = describeAffectedEntity('WebhookSubscription:abc123');

        expect(display).toEqual({ label: 'Webhook subscription', reference: 'abc123', to: null });
    });

    it('keeps a non-guid value intact as the reference', () => {
        const display = describeAffectedEntity('Job:sync');

        expect(display).toEqual({ label: 'Job', reference: 'sync', to: '/admin/jobs' });
    });

    it('treats a token without a type prefix as a plain readable label', () => {
        expect(describeAffectedEntity('justavalue')).toEqual({ label: 'justavalue', reference: null, to: null });
    });

    it('returns null for null, undefined, or blank tokens', () => {
        expect(describeAffectedEntity(null)).toBeNull();
        expect(describeAffectedEntity(undefined)).toBeNull();
        expect(describeAffectedEntity('   ')).toBeNull();
    });
});
