import { describe, it, expect, vi, beforeEach } from 'vitest';

import { apiGet } from '@/lib/api';
import { fetchLocalContentPreview } from './useLocalContentPreview';

vi.mock('@/lib/api', async (original) => ({ ...(await original<typeof import('@/lib/api')>()), apiGet: vi.fn() }));

describe('fetchLocalContentPreview', () => {
    beforeEach(() => vi.resetAllMocks());
    it('resolves a content item identity before requesting hierarchy metadata', async () => {
        // Arrange
        vi.mocked(apiGet)
            .mockResolvedValueOnce({ totalItems: 1, items: [{ id: 'hierarchy-a', contentItem: { id: 'item-b' } }] })
            .mockResolvedValueOnce({ id: 'hierarchy-a', name: 'Local target', contentType: 'movie', level: 'movie' })
            .mockResolvedValueOnce({ title: 'Target metadata' });
        // Act
        const preview = await fetchLocalContentPreview('item-b', undefined, 'item');
        // Assert
        expect(apiGet).toHaveBeenNthCalledWith(1, '/content?contentItemId=item-b&pageSize=2', undefined);
        expect(apiGet).toHaveBeenNthCalledWith(2, '/content/hierarchy-a', undefined);
        expect(apiGet).toHaveBeenNthCalledWith(3, '/content/hierarchy-a/metadata', undefined);
        expect(preview.localContentId).toBe('hierarchy-a');
        expect(preview.title).toBe('Target metadata');
    });
    it.each([
        { totalItems: 0, items: [] },
        {
            totalItems: 2,
            items: [
                { id: 'hierarchy-a', contentItem: { id: 'item-b' } },
                { id: 'hierarchy-c', contentItem: { id: 'item-b' } },
            ],
        },
        { totalItems: 1, items: [{ id: 'item-b', contentItem: { id: 'different' } }] },
    ])('does not guess a hierarchy for a missing, ambiguous, or conflicting item identity', async (page) => {
        // Arrange
        vi.mocked(apiGet).mockResolvedValueOnce(page);
        // Act / Assert
        await expect(fetchLocalContentPreview('item-b', undefined, 'item')).rejects.toMatchObject({ status: 404 });
        expect(apiGet).toHaveBeenCalledTimes(1);
    });
    it('reads local materialized values and preserves subject grouping without provider requests', async () => {
        // Arrange
        vi.mocked(apiGet)
            .mockResolvedValueOnce({ id: 'local', name: 'Fallback', contentType: 'tv', level: 'episode' })
            .mockResolvedValueOnce({
                title: 'Local title',
                duration: '00:45:00',
                genres: [{ name: 'Drama' }],
                relationships: [
                    { relatedMetadataContainerId: 'subject-meta', kind: 'actor', qualifier: 'Captain', ordinal: 2 },
                ],
                previews: [{ url: 'https://example.com/local.mp4', kind: 'clip' }],
            })
            .mockResolvedValueOnce({
                items: [
                    {
                        id: 'person-1',
                        metadataContainerId: 'subject-meta',
                        kind: 'person',
                        metadataContainer: { contributions: [{ ordinal: 0, title: 'Local actor' }] },
                    },
                ],
                totalItems: 1,
            });
        // Act
        const preview = await fetchLocalContentPreview('local');
        // Assert
        expect(preview.title).toBe('Local title');
        expect(preview.duration).toBe('00:45:00');
        expect(preview.relatedSubjects).toEqual([
            {
                name: 'Local actor',
                kind: 'person',
                relationshipKind: 'actor',
                relationshipQualifier: 'Captain',
                ordinal: 2,
                imageUrl: undefined,
            },
        ]);
        expect(apiGet).toHaveBeenNthCalledWith(1, '/content/local', undefined);
        expect(apiGet).toHaveBeenNthCalledWith(2, '/content/local/metadata', undefined);
        expect(apiGet).toHaveBeenNthCalledWith(
            3,
            '/subjects?relatedContentId=local&pageNumber=1&pageSize=100',
            undefined,
        );
        expect(apiGet).toHaveBeenCalledTimes(3);
    });
    it('accepts missing optional metadata and does not load subjects or detail tabs', async () => {
        // Arrange
        vi.mocked(apiGet)
            .mockResolvedValueOnce({ id: 'local', name: 'Fallback', contentType: 'music', level: 'track' })
            .mockResolvedValueOnce({});
        // Act
        const preview = await fetchLocalContentPreview('local');
        // Assert
        expect(preview.title).toBe('Fallback');
        expect(preview.genres).toEqual([]);
        expect(preview.relatedSubjects).toEqual([]);
        expect(apiGet).toHaveBeenCalledTimes(2);
    });
});
