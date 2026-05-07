import { forwardRef, type HTMLAttributes } from 'react';
import { ImageBroken } from '@phosphor-icons/react';

import { Badge } from '@/components/ui/Badge';
import { EmptyState } from '@/components/ui/EmptyState';

import './ImagesGallery.css';

export type GalleryImageType = 'poster' | 'backdrop' | 'banner' | 'screenshot' | 'logo';

export interface GalleryImage {
    id: string;
    url: string;
    type: GalleryImageType;
    width?: number;
    height?: number;
}

export interface ImagesGalleryProps extends HTMLAttributes<HTMLDivElement> {
    images: GalleryImage[];

    altPrefix?: string;

    linkToOriginal?: boolean;
}

export const ImagesGallery = forwardRef<HTMLDivElement, ImagesGalleryProps>(
    ({ images, altPrefix = 'Image', linkToOriginal = true, className, ...props }, ref) => {
        const classes = ['images-gallery', className].filter(Boolean).join(' ');

        if (images.length === 0) {
            return (
                <div ref={ref} className={classes} data-component="images-gallery" {...props}>
                    <EmptyState
                        icon={<ImageBroken weight="thin" />}
                        title="No images"
                        description="No images are available for this item."
                        size="sm"
                    />
                </div>
            );
        }

        return (
            <div ref={ref} className={classes} data-component="images-gallery" {...props}>
                <div className="images-gallery__grid">
                    {images.map((image) => (
                        <div key={image.id} className="images-gallery__item">
                            {linkToOriginal ? (
                                <a
                                    href={image.url}
                                    target="_blank"
                                    rel="noopener noreferrer"
                                    className="images-gallery__link"
                                >
                                    <img
                                        src={image.url}
                                        alt={`${altPrefix} ${image.type}`}
                                        className={`images-gallery__image images-gallery__image--${image.type}`}
                                        data-image-type={image.type}
                                        loading="lazy"
                                    />
                                </a>
                            ) : (
                                <img
                                    src={image.url}
                                    alt={`${altPrefix} ${image.type}`}
                                    className={`images-gallery__image images-gallery__image--${image.type}`}
                                    data-image-type={image.type}
                                    loading="lazy"
                                />
                            )}
                            <div className="images-gallery__meta">
                                <Badge variant="default" size="sm">
                                    {image.type}
                                </Badge>
                                {image.width != null && image.height != null && (
                                    <span className="images-gallery__dimensions">
                                        {image.width} x {image.height}
                                    </span>
                                )}
                            </div>
                        </div>
                    ))}
                </div>
            </div>
        );
    },
);

ImagesGallery.displayName = 'ImagesGallery';

export default ImagesGallery;
