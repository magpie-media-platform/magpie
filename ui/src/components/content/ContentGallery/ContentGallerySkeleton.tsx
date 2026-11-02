import type { HTMLAttributes } from 'react';

import { Skeleton } from '@/components/ui/Skeleton';

import { ContentGalleryGrid } from './ContentGalleryGrid';

interface ContentGallerySkeletonProps extends HTMLAttributes<HTMLDivElement> {
    count?: number;
}

export function ContentGallerySkeleton({ count = 12, className, ...props }: ContentGallerySkeletonProps) {
    return (
        <div
            className={['content-gallery-skeleton', className].filter(Boolean).join(' ')}
            role="status"
            aria-label="Loading content"
            {...props}
        >
            <div aria-hidden="true">
                <ContentGalleryGrid
                    items={Array.from({ length: count }, (_, index) => String(index))}
                    getId={(id) => id}
                >
                    {() => (
                        <div className="content-gallery-tile">
                            <Skeleton variant="rect" className="content-gallery-tile__poster" height="auto" />
                            <Skeleton width="85%" />
                            <Skeleton width="60%" />
                        </div>
                    )}
                </ContentGalleryGrid>
            </div>
        </div>
    );
}
