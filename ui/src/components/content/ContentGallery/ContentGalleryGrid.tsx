import type { ReactNode } from 'react';

import './ContentGallery.css';

interface ContentGalleryGridProps<T> {
    items: readonly T[];
    getId: (item: T) => string;
    children: (item: T) => ReactNode;
    label?: string;
}

export function ContentGalleryGrid<T>({
    items,
    getId,
    children,
    label = 'Content gallery',
}: ContentGalleryGridProps<T>) {
    return (
        <ul className="content-gallery-grid" role="list" aria-label={label}>
            {items.map((item) => (
                <li key={getId(item)} className="content-gallery-cell">
                    {children(item)}
                </li>
            ))}
        </ul>
    );
}
