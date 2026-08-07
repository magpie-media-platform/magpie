import { forwardRef, type HTMLAttributes } from 'react';
import { ImageBrokenIcon, StarIcon } from '@phosphor-icons/react';

import { Badge } from '@/components/ui/Badge';
import { EmptyState } from '@/components/ui/EmptyState';
import { safeHttpUrl } from '@/lib/safeUrl';
import type { GalleryImage } from '@/types/images';

import './ImagesGallery.css';

export type { GalleryImage } from '@/types/images';

export interface ImagesGalleryProps extends HTMLAttributes<HTMLDivElement> {
    images: GalleryImage[];

    altPrefix?: string;

    linkToOriginal?: boolean;

    onSetPrimary?: (image: GalleryImage) => void;

    settingPrimary?: boolean;
}

export const ImagesGallery = forwardRef<HTMLDivElement, ImagesGalleryProps>(
    (
        {
            images,
            altPrefix = 'Image',
            linkToOriginal = true,
            onSetPrimary,
            settingPrimary = false,
            className,
            ...props
        },
        ref,
    ) => {
        const classes = ['images-gallery', className].filter(Boolean).join(' ');

        if (images.length === 0) {
            return (
                <div ref={ref} className={classes} data-component="images-gallery" {...props}>
                    <EmptyState
                        icon={<ImageBrokenIcon weight="thin" />}
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
                    {images.map((image) => {
                        const imageUrl = safeHttpUrl(image.url);
                        const imageNode = imageUrl ? (
                            <img
                                src={imageUrl}
                                alt={`${altPrefix} ${image.type}`}
                                className={`images-gallery__image images-gallery__image--${image.type}`}
                                data-image-type={image.type}
                                loading="lazy"
                            />
                        ) : (
                            <div
                                role="img"
                                aria-label={`${altPrefix} ${image.type} unavailable`}
                                className={`images-gallery__image images-gallery__image--${image.type} images-gallery__image--unavailable`}
                                data-image-type={image.type}
                            >
                                <ImageBrokenIcon weight="thin" aria-hidden="true" />
                            </div>
                        );

                        return (
                            <div
                                key={image.id}
                                className="images-gallery__item"
                                data-primary={image.isPrimary ? 'true' : undefined}
                            >
                                <div className="images-gallery__media">
                                    {linkToOriginal && imageUrl ? (
                                        <a
                                            href={imageUrl}
                                            target="_blank"
                                            rel="noopener noreferrer"
                                            className="images-gallery__link"
                                        >
                                            {imageNode}
                                        </a>
                                    ) : (
                                        imageNode
                                    )}
                                    {image.isPrimary && (
                                        <Badge variant="healthy" size="sm" className="images-gallery__primary-badge">
                                            <StarIcon weight="fill" aria-hidden="true" />
                                            Primary
                                        </Badge>
                                    )}
                                    {onSetPrimary && !image.isPrimary && (
                                        <button
                                            type="button"
                                            className="images-gallery__set-primary"
                                            onClick={() => onSetPrimary(image)}
                                            disabled={settingPrimary}
                                            data-action="set-primary"
                                        >
                                            <StarIcon weight="bold" aria-hidden="true" />
                                            Set as primary
                                        </button>
                                    )}
                                </div>
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
                        );
                    })}
                </div>
            </div>
        );
    },
);

ImagesGallery.displayName = 'ImagesGallery';

export default ImagesGallery;
