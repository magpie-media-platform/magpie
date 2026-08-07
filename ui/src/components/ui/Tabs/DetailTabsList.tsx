import { forwardRef } from 'react';

import { TabsList, type TabsListProps } from './Tabs';

export type DetailTabsListProps = TabsListProps;

const DetailTabsList = forwardRef<HTMLDivElement, DetailTabsListProps>(({ className, ...props }, ref) => {
    const classes = ['tabs-list--detail', className].filter(Boolean).join(' ');

    return <TabsList ref={ref} className={classes} {...props} />;
});

DetailTabsList.displayName = 'DetailTabsList';

export { DetailTabsList };

export default DetailTabsList;
