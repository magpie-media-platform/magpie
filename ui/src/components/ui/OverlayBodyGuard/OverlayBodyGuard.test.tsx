import { render } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';

import { OverlayBodyGuard } from './OverlayBodyGuard';

describe('OverlayBodyGuard', () => {
    afterEach(() => {
        document.body.style.pointerEvents = '';
        document.querySelectorAll('[data-overlay-content]').forEach((node) => node.remove());
    });

    it('clears a residual body pointer-events lock when it unmounts', async () => {
        const { unmount } = render(<OverlayBodyGuard />);
        document.body.style.pointerEvents = 'none';

        unmount();
        await Promise.resolve();

        expect(document.body.style.pointerEvents).toBe('');
    });

    it('leaves the lock in place when another overlay is still open, even with an overridden data-component', async () => {
        const sibling = document.createElement('div');
        sibling.setAttribute('data-overlay-content', '');
        sibling.setAttribute('data-component', 'indexer-feed-form');
        document.body.appendChild(sibling);

        const { unmount } = render(<OverlayBodyGuard />);
        document.body.style.pointerEvents = 'none';

        unmount();
        await Promise.resolve();

        expect(document.body.style.pointerEvents).toBe('none');
    });

    it('does not disturb a body that is not locked', async () => {
        const { unmount } = render(<OverlayBodyGuard />);
        document.body.style.pointerEvents = 'auto';

        unmount();
        await Promise.resolve();

        expect(document.body.style.pointerEvents).toBe('auto');
    });
});
