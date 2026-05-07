import { forwardRef, type ComponentPropsWithoutRef } from 'react';

import './SkipLink.css';

export interface SkipLinkProps extends Omit<ComponentPropsWithoutRef<'a'>, 'href'> {
    targetId: string;

    children?: React.ReactNode;
}

const SkipLink = forwardRef<HTMLAnchorElement, SkipLinkProps>(
    ({ targetId, children = 'Skip to main content', className, ...props }, ref) => {
        const classes = ['skip-link', className].filter(Boolean).join(' ');

        return (
            <a ref={ref} href={`#${targetId}`} className={classes} data-component="skip-link" {...props}>
                {children}
            </a>
        );
    },
);

SkipLink.displayName = 'SkipLink';

export { SkipLink };
export default SkipLink;
