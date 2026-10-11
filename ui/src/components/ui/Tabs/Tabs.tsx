import {
    forwardRef,
    useCallback,
    useEffect,
    useRef,
    useState,
    type ComponentPropsWithoutRef,
    type ComponentRef,
} from 'react';
import * as TabsPrimitive from '@radix-ui/react-tabs';

import { useFit } from '@/hooks/ui/useFit';
import { useMergedRef } from '@/hooks/ui/useMergedRef';

import './Tabs.css';

type TabsRootElement = ComponentRef<typeof TabsPrimitive.Root>;

const HOLD_BACKSTOP_MS = 1500;

const findScrollContainer = (element: HTMLElement): HTMLElement | null => {
    let node: HTMLElement | null = element.parentElement;
    while (node) {
        const overflowY = window.getComputedStyle(node).overflowY;
        if (overflowY === 'auto' || overflowY === 'scroll' || overflowY === 'overlay') {
            return node;
        }
        node = node.parentElement;
    }
    return null;
};

export type TabsProps = ComponentPropsWithoutRef<typeof TabsPrimitive.Root> & {
    holdHeightOnValueChange?: boolean;
};

export type TabFit = 'equal' | 'content' | 'labels' | 'icons' | 'rows';

export type TabsListProps = ComponentPropsWithoutRef<typeof TabsPrimitive.List> & {
    fits?: readonly TabFit[];
};

export type TabsTriggerProps = ComponentPropsWithoutRef<typeof TabsPrimitive.Trigger>;

export type TabsContentProps = ComponentPropsWithoutRef<typeof TabsPrimitive.Content>;

const EQUAL_FITS: readonly TabFit[] = ['equal', 'content'];

const Tabs = forwardRef<TabsRootElement, TabsProps>(
    ({ className, holdHeightOnValueChange = false, onValueChange, ...props }, ref) => {
        const classes = ['tabs', className].filter(Boolean).join(' ');
        const rootRef = useRef<TabsRootElement | null>(null);
        const measureFrame = useRef(0);
        const observerRef = useRef<ResizeObserver | null>(null);
        const backstopTimer = useRef(0);

        const stopHold = useCallback(() => {
            cancelAnimationFrame(measureFrame.current);
            measureFrame.current = 0;
            observerRef.current?.disconnect();
            observerRef.current = null;
            window.clearTimeout(backstopTimer.current);
            backstopTimer.current = 0;
        }, []);

        const setRefs = useMergedRef(rootRef, ref);

        const handleValueChange = useCallback(
            (next: string) => {
                const node = rootRef.current;
                if (holdHeightOnValueChange && node) {
                    stopHold();

                    const scroller = findScrollContainer(node);
                    const scrollTop = scroller?.scrollTop ?? 0;
                    const heldHeight = node.offsetHeight;
                    node.style.minHeight = `${heldHeight}px`;

                    const release = () => {
                        node.style.minHeight = '';
                        stopHold();
                    };

                    measureFrame.current = requestAnimationFrame(() => {
                        const panel = node.querySelector<HTMLElement>('[role="tabpanel"][data-state="active"]') ?? node;

                        const settle = () => {
                            if (scroller) {
                                scroller.scrollTop = scrollTop;
                            }
                            release();
                        };

                        if (panel.offsetHeight >= heldHeight) {
                            settle();
                            return;
                        }

                        if (typeof ResizeObserver === 'undefined') {
                            backstopTimer.current = window.setTimeout(settle, HOLD_BACKSTOP_MS);
                            return;
                        }

                        observerRef.current = new ResizeObserver(() => {
                            if (scroller) {
                                scroller.scrollTop = scrollTop;
                            }
                            if (panel.offsetHeight >= heldHeight) {
                                settle();
                            }
                        });
                        observerRef.current.observe(panel);
                        backstopTimer.current = window.setTimeout(release, HOLD_BACKSTOP_MS);
                    });
                }
                onValueChange?.(next);
            },
            [holdHeightOnValueChange, onValueChange, stopHold],
        );

        useEffect(() => () => stopHold(), [stopHold]);

        return (
            <TabsPrimitive.Root
                ref={setRefs}
                className={classes}
                data-component="tabs"
                onValueChange={handleValueChange}
                {...props}
            />
        );
    },
);

Tabs.displayName = 'Tabs';

const TabsList = forwardRef<ComponentRef<typeof TabsPrimitive.List>, TabsListProps>(
    ({ className, fits = EQUAL_FITS, ...props }, ref) => {
        const classes = ['tabs-list', className].filter(Boolean).join(' ');
        const [list, setList] = useState<HTMLElement | null>(null);
        useFit(list, fits);

        const setRefs = useMergedRef(setList, ref);

        return <TabsPrimitive.List ref={setRefs} className={classes} {...props} />;
    },
);

TabsList.displayName = 'TabsList';

const TabsTrigger = forwardRef<ComponentRef<typeof TabsPrimitive.Trigger>, TabsTriggerProps>(
    ({ className, ...props }, ref) => {
        const classes = ['tabs-trigger', className].filter(Boolean).join(' ');

        return <TabsPrimitive.Trigger ref={ref} className={classes} {...props} />;
    },
);

TabsTrigger.displayName = 'TabsTrigger';

const TabsContent = forwardRef<ComponentRef<typeof TabsPrimitive.Content>, TabsContentProps>(
    ({ className, ...props }, ref) => {
        const classes = ['tabs-content', className].filter(Boolean).join(' ');

        return <TabsPrimitive.Content ref={ref} className={classes} {...props} />;
    },
);

TabsContent.displayName = 'TabsContent';

export { Tabs, TabsList, TabsTrigger, TabsContent };

export default Tabs;
