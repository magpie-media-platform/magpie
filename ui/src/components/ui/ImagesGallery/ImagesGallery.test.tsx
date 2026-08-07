import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';

import { ImagesGallery, type GalleryImage } from './ImagesGallery';

const mockImages: GalleryImage[] = [
    { id: 'img-1', url: 'https://example.com/poster.jpg', type: 'poster', width: 300, height: 450 },
    { id: 'img-2', url: 'https://example.com/backdrop.jpg', type: 'backdrop', width: 780, height: 439 },
    { id: 'img-3', url: 'https://example.com/banner.jpg', type: 'banner' },
    { id: 'img-4', url: 'https://example.com/screenshot.jpg', type: 'screenshot', width: 1920, height: 1080 },
    { id: 'img-5', url: 'https://example.com/logo.png', type: 'logo', width: 400, height: 200 },
];

describe('ImagesGallery', () => {
    it('renders images-gallery class', () => {
        render(<ImagesGallery images={mockImages} data-testid="gallery" />);
        expect(screen.getByTestId('gallery')).toHaveClass('images-gallery');
    });

    it('has data-component attribute', () => {
        render(<ImagesGallery images={mockImages} data-testid="gallery" />);
        expect(screen.getByTestId('gallery')).toHaveAttribute('data-component', 'images-gallery');
    });

    it('renders all images', () => {
        render(<ImagesGallery images={mockImages} altPrefix="Test" />);
        const images = screen.getAllByRole('img');
        expect(images).toHaveLength(5);
    });

    it('generates alt text from altPrefix and image type', () => {
        render(<ImagesGallery images={[mockImages[0]]} altPrefix="Breaking Bad" />);
        expect(screen.getByAltText('Breaking Bad poster')).toBeInTheDocument();
    });

    it('uses default altPrefix when not provided', () => {
        render(<ImagesGallery images={[mockImages[0]]} />);
        expect(screen.getByAltText('Image poster')).toBeInTheDocument();
    });

    it('renders images as links by default', () => {
        render(<ImagesGallery images={[mockImages[0]]} />);
        const link = screen.getByRole('link');
        expect(link).toHaveAttribute('href', 'https://example.com/poster.jpg');
        expect(link).toHaveAttribute('target', '_blank');
        expect(link).toHaveAttribute('rel', 'noopener noreferrer');
    });

    it('renders images without links when linkToOriginal is false', () => {
        render(<ImagesGallery images={[mockImages[0]]} linkToOriginal={false} />);
        expect(screen.queryByRole('link')).not.toBeInTheDocument();
        expect(screen.getByRole('img')).toBeInTheDocument();
    });

    it('does not link unsafe image urls', () => {
        render(<ImagesGallery images={[{ ...mockImages[0], url: 'javascript:alert(1)' }]} />);
        expect(screen.queryByRole('link')).not.toBeInTheDocument();
        expect(screen.getByRole('img', { name: /unavailable/i })).toBeInTheDocument();
    });

    it('applies type-specific class to images', () => {
        render(<ImagesGallery images={mockImages} />);
        const images = screen.getAllByRole('img');
        expect(images[0]).toHaveAttribute('data-image-type', 'poster');
        expect(images[1]).toHaveAttribute('data-image-type', 'backdrop');
        expect(images[2]).toHaveAttribute('data-image-type', 'banner');
        expect(images[3]).toHaveAttribute('data-image-type', 'screenshot');
        expect(images[4]).toHaveAttribute('data-image-type', 'logo');
    });

    it('applies base image class to all images', () => {
        render(<ImagesGallery images={mockImages} />);
        const images = screen.getAllByRole('img');
        images.forEach((img) => {
            expect(img).toHaveClass('images-gallery__image');
        });
    });

    it('sets lazy loading on images', () => {
        render(<ImagesGallery images={[mockImages[0]]} />);
        expect(screen.getByRole('img')).toHaveAttribute('loading', 'lazy');
    });

    it('displays image type as badge', () => {
        render(<ImagesGallery images={[mockImages[0]]} />);
        expect(screen.getByText('poster')).toBeInTheDocument();
    });

    it('displays dimensions when width and height are provided', () => {
        render(<ImagesGallery images={[mockImages[0]]} />);
        expect(screen.getByText('300 x 450')).toBeInTheDocument();
    });

    it('does not display dimensions when missing', () => {
        render(<ImagesGallery images={[mockImages[2]]} />);
        expect(screen.getByText('banner')).toBeInTheDocument();
        expect(screen.queryByText(/\d+ x \d+/)).not.toBeInTheDocument();
    });

    it('shows empty state when images array is empty', () => {
        render(<ImagesGallery images={[]} data-testid="gallery" />);
        expect(screen.getByText('No images')).toBeInTheDocument();
        expect(screen.getByText('No images are available for this item.')).toBeInTheDocument();
    });

    it('does not render grid when images array is empty', () => {
        render(<ImagesGallery images={[]} data-testid="gallery" />);
        expect(screen.getByTestId('gallery').querySelector('.images-gallery__grid')).not.toBeInTheDocument();
    });

    it('forwards ref', () => {
        const ref = vi.fn();
        render(<ImagesGallery ref={ref} images={mockImages} />);
        expect(ref).toHaveBeenCalledWith(expect.any(HTMLDivElement));
    });

    it('applies custom className', () => {
        render(<ImagesGallery images={mockImages} className="my-gallery" data-testid="gallery" />);
        expect(screen.getByTestId('gallery')).toHaveClass('my-gallery');
        expect(screen.getByTestId('gallery')).toHaveClass('images-gallery');
    });

    it('passes through additional props', () => {
        render(<ImagesGallery images={mockImages} data-testid="my-gallery" />);
        expect(screen.getByTestId('my-gallery')).toBeInTheDocument();
    });

    it('renders correct number of gallery items', () => {
        render(<ImagesGallery images={mockImages} data-testid="gallery" />);
        const items = screen.getByTestId('gallery').querySelectorAll('.images-gallery__item');
        expect(items).toHaveLength(5);
    });

    it('renders all image types with badges', () => {
        render(<ImagesGallery images={mockImages} />);
        expect(screen.getByText('poster')).toBeInTheDocument();
        expect(screen.getByText('backdrop')).toBeInTheDocument();
        expect(screen.getByText('banner')).toBeInTheDocument();
        expect(screen.getByText('screenshot')).toBeInTheDocument();
        expect(screen.getByText('logo')).toBeInTheDocument();
    });

    it('renders multiple dimension labels for images with sizes', () => {
        render(<ImagesGallery images={mockImages} />);
        expect(screen.getByText('300 x 450')).toBeInTheDocument();
        expect(screen.getByText('780 x 439')).toBeInTheDocument();
        expect(screen.getByText('1920 x 1080')).toBeInTheDocument();
        expect(screen.getByText('400 x 200')).toBeInTheDocument();
    });

    it('preserves data-component on empty state', () => {
        render(<ImagesGallery images={[]} data-testid="gallery" />);
        expect(screen.getByTestId('gallery')).toHaveAttribute('data-component', 'images-gallery');
    });

    it('renders a Primary badge on the current primary image', () => {
        const images: GalleryImage[] = [{ ...mockImages[0], isPrimary: true }, mockImages[1]];
        render(<ImagesGallery images={images} />);
        expect(screen.getByText('Primary')).toBeInTheDocument();
    });

    it('shows a set-primary action on non-primary images when onSetPrimary is provided', () => {
        const onSetPrimary = vi.fn();
        render(<ImagesGallery images={[mockImages[0]]} onSetPrimary={onSetPrimary} />);
        const button = screen.getByRole('button', { name: /set as primary/i });
        fireEvent.click(button);
        expect(onSetPrimary).toHaveBeenCalledWith(mockImages[0]);
    });

    it('does not render a set-primary action on the primary image', () => {
        const onSetPrimary = vi.fn();
        render(<ImagesGallery images={[{ ...mockImages[0], isPrimary: true }]} onSetPrimary={onSetPrimary} />);
        expect(screen.queryByRole('button', { name: /set as primary/i })).not.toBeInTheDocument();
    });

    it('omits the set-primary action when no onSetPrimary handler is provided', () => {
        render(<ImagesGallery images={[mockImages[0]]} />);
        expect(screen.queryByRole('button', { name: /set as primary/i })).not.toBeInTheDocument();
    });

    it('disables the set-primary action while a set is pending', () => {
        render(<ImagesGallery images={[mockImages[0]]} onSetPrimary={vi.fn()} settingPrimary />);
        expect(screen.getByRole('button', { name: /set as primary/i })).toBeDisabled();
    });
});
