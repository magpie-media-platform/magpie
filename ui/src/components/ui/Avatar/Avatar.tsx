import { forwardRef, useState, type ImgHTMLAttributes, type ReactNode } from 'react';

import './Avatar.css';

export type AvatarSize = 'sm' | 'md' | 'lg' | 'xl';

export interface AvatarProps extends Omit<ImgHTMLAttributes<HTMLImageElement>, 'children'> {
    size?: AvatarSize;

    src?: string;

    alt?: string;

    fallback?: ReactNode;

    'data-testid'?: string;
}

const Avatar = forwardRef<HTMLSpanElement, AvatarProps>(
    ({ size = 'md', src, alt, fallback, className, 'data-testid': dataTestId, ...props }, ref) => {
        const [imageError, setImageError] = useState(false);
        const showImage = src && !imageError;
        const classes = ['avatar', `avatar--${size}`, className].filter(Boolean).join(' ');

        return (
            <span ref={ref} className={classes} data-component="avatar" data-size={size} data-testid={dataTestId}>
                {showImage ? (
                    <img className="avatar__image" src={src} alt={alt} onError={() => setImageError(true)} {...props} />
                ) : (
                    <span className="avatar__fallback">{fallback}</span>
                )}
            </span>
        );
    },
);

Avatar.displayName = 'Avatar';

export { Avatar };
