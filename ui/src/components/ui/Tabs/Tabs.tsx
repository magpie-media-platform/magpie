import { forwardRef, type ComponentPropsWithoutRef, type ElementRef } from 'react';
import * as TabsPrimitive from '@radix-ui/react-tabs';

import './Tabs.css';

export type TabsProps = ComponentPropsWithoutRef<typeof TabsPrimitive.Root>;

export type TabsListProps = ComponentPropsWithoutRef<typeof TabsPrimitive.List>;

export type TabsTriggerProps = ComponentPropsWithoutRef<typeof TabsPrimitive.Trigger>;

export type TabsContentProps = ComponentPropsWithoutRef<typeof TabsPrimitive.Content>;

const Tabs = forwardRef<ElementRef<typeof TabsPrimitive.Root>, TabsProps>(({ className, ...props }, ref) => {
    const classes = ['tabs', className].filter(Boolean).join(' ');

    return <TabsPrimitive.Root ref={ref} className={classes} data-component="tabs" {...props} />;
});

Tabs.displayName = 'Tabs';

const TabsList = forwardRef<ElementRef<typeof TabsPrimitive.List>, TabsListProps>(({ className, ...props }, ref) => {
    const classes = ['tabs-list', className].filter(Boolean).join(' ');

    return <TabsPrimitive.List ref={ref} className={classes} {...props} />;
});

TabsList.displayName = 'TabsList';

const TabsTrigger = forwardRef<ElementRef<typeof TabsPrimitive.Trigger>, TabsTriggerProps>(
    ({ className, ...props }, ref) => {
        const classes = ['tabs-trigger', className].filter(Boolean).join(' ');

        return <TabsPrimitive.Trigger ref={ref} className={classes} {...props} />;
    },
);

TabsTrigger.displayName = 'TabsTrigger';

const TabsContent = forwardRef<ElementRef<typeof TabsPrimitive.Content>, TabsContentProps>(
    ({ className, ...props }, ref) => {
        const classes = ['tabs-content', className].filter(Boolean).join(' ');

        return <TabsPrimitive.Content ref={ref} className={classes} {...props} />;
    },
);

TabsContent.displayName = 'TabsContent';

export { Tabs, TabsList, TabsTrigger, TabsContent };

export default Tabs;
