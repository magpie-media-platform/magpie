import { renderHook } from '@testing-library/react';
import { describe, expect, it, afterEach } from 'vitest';

import { useDocumentTitle } from './useDocumentTitle';

describe('useDocumentTitle', () => {
    afterEach(() => {
        document.title = '';
    });

    it('sets the document title with the base suffix', () => {
        renderHook(() => useDocumentTitle('Dashboard'));
        expect(document.title).toBe('Dashboard - Magpie');
    });

    it('sets just the base title when no title is provided', () => {
        renderHook(() => useDocumentTitle());
        expect(document.title).toBe('Magpie');
    });

    it('sets just the base title when title is undefined', () => {
        renderHook(() => useDocumentTitle(undefined));
        expect(document.title).toBe('Magpie');
    });

    it('updates the title when the argument changes', () => {
        const { rerender } = renderHook(({ title }) => useDocumentTitle(title), {
            initialProps: { title: 'Dashboard' },
        });
        expect(document.title).toBe('Dashboard - Magpie');

        rerender({ title: 'Settings' });
        expect(document.title).toBe('Settings - Magpie');
    });

    it('restores the base title on unmount', () => {
        const { unmount } = renderHook(() => useDocumentTitle('Dashboard'));
        expect(document.title).toBe('Dashboard - Magpie');

        unmount();
        expect(document.title).toBe('Magpie');
    });
});
