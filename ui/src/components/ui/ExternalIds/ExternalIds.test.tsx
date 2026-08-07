import { render, screen, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';

import { ExternalIdDetails, ExternalIds, type ExternalId } from './ExternalIds';

const mockIds: ExternalId[] = [
    { source: 'Provider-H', id: 'tt0903747', url: 'https://www.provider-h.example/title/tt0903747/' },
    { source: 'Provider-A', id: '1396', url: 'https://www.provider-a.example/tv/1396' },
    { source: 'Provider-J', id: '81189' },
];

describe('ExternalIds', () => {
    describe('rendering', () => {
        it('renders all external IDs', () => {
            render(<ExternalIds ids={mockIds} />);
            expect(screen.getByText('Provider-H')).toBeInTheDocument();
            expect(screen.getByText('Provider-A')).toBeInTheDocument();
            expect(screen.getByText('Provider-J')).toBeInTheDocument();
        });

        it('renders ID values in monospace', () => {
            render(<ExternalIds ids={mockIds} />);
            expect(screen.getByText('tt0903747')).toBeInTheDocument();
            expect(screen.getByText('1396')).toBeInTheDocument();
            expect(screen.getByText('81189')).toBeInTheDocument();
        });

        it('renders linked IDs as anchor elements', () => {
            render(<ExternalIds ids={mockIds} />);
            const providerHLink = screen.getByRole('link', { name: /provider-h/i });
            expect(providerHLink).toHaveAttribute('href', 'https://www.provider-h.example/title/tt0903747/');

            const providerALink = screen.getByRole('link', { name: /provider-a/i });
            expect(providerALink).toHaveAttribute('href', 'https://www.provider-a.example/tv/1396');
        });

        it('renders non-linked IDs as span elements', () => {
            render(<ExternalIds ids={mockIds} />);

            const providerJElements = screen.queryAllByRole('link', { name: /provider-j/i });
            expect(providerJElements).toHaveLength(0);

            expect(screen.getByText('Provider-J')).toBeInTheDocument();
            expect(screen.getByText('81189')).toBeInTheDocument();
        });

        it('returns null for empty IDs array', () => {
            const { container } = render(<ExternalIds ids={[]} />);
            expect(container.firstChild).toBeNull();
        });

        it('renders single ID', () => {
            render(
                <ExternalIds
                    ids={[
                        { source: 'Provider-H', id: 'tt1234567', url: 'https://provider-h.example/title/tt1234567/' },
                    ]}
                />,
            );
            expect(screen.getByText('Provider-H')).toBeInTheDocument();
            expect(screen.getByText('tt1234567')).toBeInTheDocument();
        });

        it('renders IDs without URLs correctly', () => {
            render(
                <ExternalIds
                    ids={[
                        { source: 'CustomDB', id: '12345' },
                        { source: 'OtherDB', id: '67890' },
                    ]}
                />,
            );
            expect(screen.getByText('CustomDB')).toBeInTheDocument();
            expect(screen.getByText('12345')).toBeInTheDocument();
            expect(screen.queryAllByRole('link')).toHaveLength(0);
        });

        it('renders unsafe URLs as non-linked IDs', () => {
            render(<ExternalIds ids={[{ source: 'UnsafeDB', id: '12345', url: 'javascript:alert(1)' }]} />);

            expect(screen.queryAllByRole('link')).toHaveLength(0);
            expect(screen.getByText('UnsafeDB')).toBeInTheDocument();
            expect(screen.getByText('12345')).toBeInTheDocument();
        });
    });

    describe('accessibility', () => {
        it('has correct component data attribute', () => {
            render(<ExternalIds ids={mockIds} />);
            expect(document.querySelector('[data-component="external-ids"]')).toBeInTheDocument();
        });

        it('linked IDs open in new tab', () => {
            render(<ExternalIds ids={mockIds} />);
            const providerHLink = screen.getByRole('link', { name: /provider-h/i });
            expect(providerHLink).toHaveAttribute('target', '_blank');
            expect(providerHLink).toHaveAttribute('rel', 'noopener noreferrer');
        });

        it('sets data-source attribute on items', () => {
            render(<ExternalIds ids={mockIds} />);
            expect(document.querySelector('[data-source="provider-h"]')).toBeInTheDocument();
            expect(document.querySelector('[data-source="provider-a"]')).toBeInTheDocument();
            expect(document.querySelector('[data-source="provider-j"]')).toBeInTheDocument();
        });
    });

    describe('linked state', () => {
        it('applies linked class for items with URLs', () => {
            render(
                <ExternalIds
                    ids={[{ source: 'Provider-H', id: 'tt0903747', url: 'https://provider-h.example/test' }]}
                />,
            );
            const item = document.querySelector('[data-source="provider-h"]');
            expect(item).toHaveAttribute('data-linked');
        });

        it('does not apply linked class for items without URLs', () => {
            render(<ExternalIds ids={[{ source: 'Provider-J', id: '81189' }]} />);
            const item = document.querySelector('[data-source="provider-j"]');
            expect(item).not.toHaveAttribute('data-linked');
        });
    });

    describe('custom props', () => {
        it('forwards className', () => {
            render(<ExternalIds ids={mockIds} className="custom-class" />);
            const container = document.querySelector('[data-component="external-ids"]');
            expect(container).toHaveClass('external-ids');
            expect(container).toHaveClass('custom-class');
        });

        it('forwards additional props', () => {
            render(<ExternalIds ids={mockIds} data-testid="ext-ids" />);
            expect(screen.getByTestId('ext-ids')).toBeInTheDocument();
        });
    });

    describe('chips variant', () => {
        it('renders nothing when ids and leading chips are both empty', () => {
            const { container } = render(<ExternalIds variant="chips" ids={[]} />);
            expect(container.firstChild).toBeNull();
        });

        it('renders the leading label and a linked source pill', () => {
            render(
                <ExternalIds
                    variant="chips"
                    label="View on"
                    ids={[{ source: 'provider-a', id: '603', url: 'https://www.provider-a.example/movie/603' }]}
                    itemTestId={(extId) => `external-source-link-${extId.source.toLowerCase()}`}
                />,
            );
            expect(screen.getByText('View on')).toBeInTheDocument();
            const chip = screen.getByTestId('external-source-link-provider-a');
            expect(chip.tagName).toBe('A');
            expect(chip).toHaveAttribute('href', 'https://www.provider-a.example/movie/603');
            expect(chip).toHaveAttribute('target', '_blank');
            expect(chip).toHaveAttribute('rel', 'noopener noreferrer');
        });

        it('omits a source with no url from the link row (no dead chip)', () => {
            const { container } = render(
                <ExternalIds
                    variant="chips"
                    ids={[{ source: 'custom-provider', id: 'cp-1' }]}
                    itemTestId={(extId) => `external-source-link-${extId.source.toLowerCase()}`}
                />,
            );
            expect(screen.queryByTestId('external-source-link-custom-provider')).not.toBeInTheDocument();
            expect(container.firstChild).toBeNull();
        });

        it('renders only the linked sources, dropping url-less ones', () => {
            render(
                <ExternalIds
                    variant="chips"
                    label="View on"
                    ids={[
                        { source: 'provider-a', id: '603', url: 'https://www.provider-a.example/movie/603' },
                        { source: 'custom-provider', id: 'cp-1' },
                    ]}
                    itemTestId={(extId) => `external-source-link-${extId.source.toLowerCase()}`}
                />,
            );
            const list = screen.getByRole('list');
            expect(within(list).getAllByRole('listitem')).toHaveLength(1);
            expect(screen.getByTestId('external-source-link-provider-a')).toBeInTheDocument();
            expect(screen.queryByTestId('external-source-link-custom-provider')).not.toBeInTheDocument();
        });

        it('drops unsafe chip URLs', () => {
            const { container } = render(
                <ExternalIds
                    variant="chips"
                    ids={[{ source: 'bad', id: '1', url: 'javascript:alert(1)' }]}
                    leadingChips={[
                        {
                            key: 'bad-leading',
                            label: 'Bad leading',
                            url: 'data:text/html,<script>alert(1)</script>',
                        },
                    ]}
                />,
            );

            expect(container.firstChild).toBeNull();
        });

        it('renders leading chips before external ids, with their own icon', () => {
            render(
                <ExternalIds
                    variant="chips"
                    label="View on"
                    leadingChips={[
                        {
                            key: 'media-1',
                            label: 'Media Server',
                            url: 'http://mediaserver-a/details?id=abc',
                            source: 'mediaserver-a',
                            testId: 'media-server-link-mediaserver-a',
                            icon: <svg data-testid="media-icon" />,
                        },
                    ]}
                    ids={[{ source: 'provider-a', id: '603', url: 'https://www.provider-a.example/movie/603' }]}
                    itemTestId={(extId) => `external-source-link-${extId.source.toLowerCase()}`}
                />,
            );

            const list = screen.getByRole('list');
            const items = within(list).getAllByRole('listitem');
            expect(items).toHaveLength(2);
            const mediaChip = within(items[0]!).getByTestId('media-server-link-mediaserver-a');
            expect(mediaChip).toHaveAttribute('href', 'http://mediaserver-a/details?id=abc');
            expect(within(mediaChip).getByTestId('media-icon')).toBeInTheDocument();
            expect(within(items[1]!).getByTestId('external-source-link-provider-a')).toBeInTheDocument();
        });

        it('exposes the chips variant data attribute', () => {
            render(<ExternalIds variant="chips" ids={mockIds} />);
            expect(document.querySelector('[data-variant="chips"]')).toBeInTheDocument();
        });
    });

    describe('detail rows', () => {
        it('renders ordered repeated sources with safe links and plain unsafe values', () => {
            render(
                <ExternalIdDetails
                    ids={[
                        { source: 'Provider-H', id: 'tt1', url: 'https://provider-h.example/title/tt1' },
                        { source: 'Provider-H', id: 'tt2', url: 'javascript:alert(1)' },
                    ]}
                />,
            );

            expect(screen.getAllByRole('term').map((term) => term.textContent)).toEqual(['Provider-H', 'Provider-H']);
            expect(screen.getByRole('link', { name: 'Provider-H tt1' })).toHaveAttribute('rel', 'noopener noreferrer');
            expect(screen.queryByRole('link', { name: /tt2/ })).not.toBeInTheDocument();
            expect(screen.getByText('tt2')).toBeInTheDocument();
        });
    });
});
