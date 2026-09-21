import { describe, expect, it } from 'vitest';

import { displayLineageLeafTitle } from './downloadTitle';

describe('displayLineageLeafTitle', () => {
    it.each([
        [{ contentMetadataTitle: ' Metadata ', contentHierarchyName: 'Hierarchy', title: 'Release' }, 'Metadata'],
        [{ contentMetadataTitle: ' ', contentHierarchyName: ' Hierarchy ', title: 'Release' }, 'Hierarchy'],
        [{ contentHierarchyName: ' ', title: 'Release' }, 'Release'],
    ])('resolves the leaf title for %o', (download, expected) => {
        expect(displayLineageLeafTitle(download)).toBe(expected);
    });
});
