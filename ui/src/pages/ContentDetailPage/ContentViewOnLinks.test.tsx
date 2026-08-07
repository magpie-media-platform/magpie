import { render, screen, within } from '@testing-library/react';
import { describe, expect, it, vi, beforeEach } from 'vitest';

import type { ContentExternalId } from '@/lib/metadata';

import { ContentViewOnLinks } from './ContentViewOnLinks';

const mockUseMediaServerLinks = vi.fn();

vi.mock('@/hooks/useContent', () => ({
    useMediaServerLinks: (id: string | undefined) => mockUseMediaServerLinks(id),
}));

describe('ContentViewOnLinks', () => {
    beforeEach(() => {
        vi.clearAllMocks();
        mockUseMediaServerLinks.mockReturnValue({ data: [], isLoading: false });
    });

    it('renders nothing when both sides are empty', () => {
        const { container } = render(<ContentViewOnLinks contentId="c-1" externalIds={[]} />);
        expect(container).toBeEmptyDOMElement();
    });

    it('renders the row when only external ids exist', () => {
        const ids: ContentExternalId[] = [
            { source: 'provider-a', value: '603', url: 'https://www.provider-a.example/movie/603' },
        ];
        render(<ContentViewOnLinks contentId="c-1" externalIds={ids} />);
        expect(screen.getByText('Links')).toBeInTheDocument();
        expect(screen.getByTestId('external-source-link-provider-a')).toHaveAttribute(
            'href',
            'https://www.provider-a.example/movie/603',
        );
    });

    it('renders the row when only media servers exist', () => {
        mockUseMediaServerLinks.mockReturnValue({
            data: [
                {
                    mediaServerId: 's-1',
                    serverName: 'Media Server',
                    providerType: 'mediaserver-a',
                    url: 'http://mediaserver-a/web/index.html#!/details?id=abc',
                },
            ],
            isLoading: false,
        });
        render(<ContentViewOnLinks contentId="c-1" externalIds={undefined} />);
        const chip = screen.getByTestId('media-server-link-mediaserver-a');
        expect(chip).toHaveTextContent('Media Server');
        expect(chip).toHaveAttribute('href', 'http://mediaserver-a/web/index.html#!/details?id=abc');
        expect(chip).toHaveAttribute('data-media-server-id', 's-1');
        expect(chip.querySelector('svg')).not.toBeNull();
    });

    it('renders media servers BEFORE external links', () => {
        mockUseMediaServerLinks.mockReturnValue({
            data: [
                {
                    mediaServerId: 's-1',
                    serverName: 'Media Server',
                    providerType: 'mediaserver-a',
                    url: 'http://mediaserver-a/details?id=abc',
                },
            ],
            isLoading: false,
        });
        const externalIds: ContentExternalId[] = [
            { source: 'provider-a', value: '603', url: 'https://www.provider-a.example/movie/603' },
        ];
        render(<ContentViewOnLinks contentId="c-1" externalIds={externalIds} />);

        const list = screen.getByRole('list');
        const items = within(list).getAllByRole('listitem');
        expect(items.length).toBe(2);
        expect(within(items[0]!).getByTestId('media-server-link-mediaserver-a')).toBeInTheDocument();
        expect(within(items[1]!).getByTestId('external-source-link-provider-a')).toBeInTheDocument();
    });

    it('omits an external source with no URL from the link row', () => {
        const ids: ContentExternalId[] = [{ source: 'custom-provider', value: 'cp-1' }];
        const { container } = render(<ContentViewOnLinks contentId="c-1" externalIds={ids} />);
        expect(screen.queryByTestId('external-source-link-custom-provider')).not.toBeInTheDocument();
        expect(container).toBeEmptyDOMElement();
    });

    it('hides media-server entries while their query is loading', () => {
        mockUseMediaServerLinks.mockReturnValue({ data: undefined, isLoading: true });
        const ids: ContentExternalId[] = [
            { source: 'provider-a', value: '603', url: 'https://www.provider-a.example/movie/603' },
        ];
        render(<ContentViewOnLinks contentId="c-1" externalIds={ids} />);
        expect(screen.queryByTestId('media-server-link-mediaserver-a')).toBeNull();
        expect(screen.getByTestId('external-source-link-provider-a')).toBeInTheDocument();
    });
});
