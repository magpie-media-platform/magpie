import { forwardRef, useContext, useEffect, useMemo, useState } from 'react';

import { CubeIcon, IconContext } from '@phosphor-icons/react';
import type { IconProps, IconWeight } from '@phosphor-icons/react';

const PHOSPHOR_CORE_CDN_ROOT = 'https://cdn.jsdelivr.net/npm/@phosphor-icons/core@2.1.1/assets';

export interface CdnPhosphorIconProps extends Omit<IconProps, 'name'> {
    name: string | null | undefined;
}

export function normalizePhosphorIconName(name: string | null | undefined): string | null {
    const nameWithoutSuffix = name?.trim().replace(/\s*icon$/i, '');
    if (!nameWithoutSuffix || !/^[a-z0-9 _-]+$/i.test(nameWithoutSuffix)) {
        return null;
    }

    const normalized = nameWithoutSuffix
        .replace(/([a-z0-9])([A-Z])/g, '$1-$2')
        .replace(/[ _]+/g, '-')
        .replace(/-+/g, '-')
        .toLowerCase();

    return normalized || null;
}

export function buildPhosphorIconUrl(name: string, weight: IconWeight): string {
    const fileName = weight === 'regular' ? `${name}.svg` : `${name}-${weight}.svg`;
    return `${PHOSPHOR_CORE_CDN_ROOT}/${weight}/${fileName}`;
}

export const CdnPhosphorIcon = forwardRef<SVGSVGElement, CdnPhosphorIconProps>(function CdnPhosphorIcon(
    { name, ...props },
    ref,
) {
    const context = useContext(IconContext);
    const mergedProps = { ...context, ...props };
    const {
        alt,
        color = 'currentColor',
        size = '1em',
        weight = 'regular',
        mirrored = false,
        style,
        children,
        ...svgProps
    } = mergedProps;
    const normalizedName = normalizePhosphorIconName(name);
    const iconUrl = useMemo(
        () => (normalizedName ? buildPhosphorIconUrl(normalizedName, weight) : null),
        [normalizedName, weight],
    );
    const [loadedUrl, setLoadedUrl] = useState<string | null>(null);
    const [failedUrl, setFailedUrl] = useState<string | null>(null);

    useEffect(() => {
        if (!iconUrl) {
            return;
        }

        let active = true;
        const image = new Image();
        image.onload = () => {
            if (active) {
                setFailedUrl(null);
                setLoadedUrl(iconUrl);
            }
        };
        image.onerror = () => {
            if (active) {
                setLoadedUrl(null);
                setFailedUrl(iconUrl);
            }
        };
        image.src = iconUrl;

        return () => {
            active = false;
            image.onload = null;
            image.onerror = null;
        };
    }, [iconUrl]);

    if (!iconUrl || failedUrl === iconUrl || loadedUrl !== iconUrl) {
        return <CubeIcon ref={ref} {...props} />;
    }

    return (
        <svg
            ref={ref}
            xmlns="http://www.w3.org/2000/svg"
            width={size}
            height={size}
            viewBox="0 0 256 256"
            {...svgProps}
            data-icon-source="phosphor-cdn"
            style={{
                display: 'inline-block',
                flexShrink: 0,
                backgroundColor: color,
                maskImage: `url("${iconUrl}")`,
                maskPosition: 'center',
                maskRepeat: 'no-repeat',
                maskSize: 'contain',
                transform: mirrored ? 'scaleX(-1)' : undefined,
                WebkitMaskImage: `url("${iconUrl}")`,
                WebkitMaskPosition: 'center',
                WebkitMaskRepeat: 'no-repeat',
                WebkitMaskSize: 'contain',
                ...style,
            }}
        >
            {alt && <title>{alt}</title>}
            {children}
        </svg>
    );
});

CdnPhosphorIcon.displayName = 'CdnPhosphorIcon';
