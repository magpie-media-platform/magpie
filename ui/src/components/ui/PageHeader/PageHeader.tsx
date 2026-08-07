import { forwardRef, useId, type HTMLAttributes, type ReactNode } from 'react';

import './PageHeader.css';

export interface PageHeaderProps extends HTMLAttributes<HTMLElement> {
    title: string;
    description?: string;
    icon?: ReactNode;
    titleAdornment?: ReactNode;
    actions?: ReactNode;
}

export const PageHeader = forwardRef<HTMLElement, PageHeaderProps>(
    ({ title, description, icon, titleAdornment, actions, className, ...props }, ref) => {
        const descriptionId = useId();
        const classes = ['page-header', className].filter(Boolean).join(' ');

        return (
            <header ref={ref} className={classes} data-component="page-header" {...props}>
                <div className="page-header__main">
                    <div className="page-header__title-row">
                        {icon && (
                            <span className="page-header__icon" data-testid="page-header__icon" aria-hidden="true">
                                {icon}
                            </span>
                        )}
                        <h1 className="page-header__title" aria-describedby={description ? descriptionId : undefined}>
                            {title}
                        </h1>
                        {titleAdornment && <span className="page-header__title-adornment">{titleAdornment}</span>}
                    </div>
                    {description && (
                        <p
                            id={descriptionId}
                            className="page-header__description"
                            data-testid="page-header__description"
                        >
                            {description}
                        </p>
                    )}
                </div>
                {actions && <div className="page-header__actions">{actions}</div>}
            </header>
        );
    },
);

PageHeader.displayName = 'PageHeader';

export default PageHeader;
