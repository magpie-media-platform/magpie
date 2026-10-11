import { forwardRef } from 'react';

import { TabsList, type TabFit, type TabsListProps } from './Tabs';

export type DetailTabsListProps = Omit<TabsListProps, 'fits'>;

const DETAIL_FITS: readonly TabFit[] = ['labels', 'icons', 'rows'];

const DetailTabsList = forwardRef<HTMLDivElement, DetailTabsListProps>(({ className, ...props }, ref) => {
    const classes = ['tabs-list--detail', className].filter(Boolean).join(' ');

    return <TabsList ref={ref} className={classes} fits={DETAIL_FITS} {...props} />;
});

DetailTabsList.displayName = 'DetailTabsList';

export { DetailTabsList };

export default DetailTabsList;
