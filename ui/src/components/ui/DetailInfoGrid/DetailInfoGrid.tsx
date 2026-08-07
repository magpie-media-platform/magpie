import { forwardRef, type ComponentPropsWithoutRef, type ReactNode } from 'react';

import './DetailInfoGrid.css';

export interface DetailInfoGridProps extends ComponentPropsWithoutRef<'section'> {
    children: ReactNode;
}

export interface DetailInfoRowProps extends Omit<ComponentPropsWithoutRef<'div'>, 'children'> {
    label: ReactNode;
    value: ReactNode;
    control?: ReactNode;
}

export const DetailInfoGrid = forwardRef<HTMLElement, DetailInfoGridProps>(({ children, className, ...props }, ref) => {
    const classes = ['detail-info-grid', className].filter(Boolean).join(' ');

    return (
        <section ref={ref} className={classes} data-component="detail-info-grid" {...props}>
            {children}
        </section>
    );
});

DetailInfoGrid.displayName = 'DetailInfoGrid';

export const DetailInfoRow = forwardRef<HTMLDivElement, DetailInfoRowProps>(
    ({ label, value, control, className, ...props }, ref) => {
        const classes = ['detail-info-row', className].filter(Boolean).join(' ');

        return (
            <div ref={ref} className={classes} data-has-control={control ? 'true' : 'false'} {...props}>
                <div className="detail-info-row__label">{label}</div>
                <div className="detail-info-row__value">{value}</div>
                {control && <div className="detail-info-row__control">{control}</div>}
            </div>
        );
    },
);

DetailInfoRow.displayName = 'DetailInfoRow';
