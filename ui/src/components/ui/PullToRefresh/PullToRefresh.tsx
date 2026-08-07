import {
    forwardRef,
    useCallback,
    useImperativeHandle,
    useRef,
    useState,
    type HTMLAttributes,
    type ReactNode,
} from 'react';
import { ArrowClockwiseIcon } from '@phosphor-icons/react';

import { useMediaQuery } from '@/hooks/ui/useMediaQuery';

import './PullToRefresh.css';

export type PullToRefreshState = 'idle' | 'pulling' | 'ready' | 'refreshing';

export interface PullToRefreshProps extends Omit<HTMLAttributes<HTMLDivElement>, 'children'> {
    children: ReactNode;

    onRefresh: () => Promise<void>;

    threshold?: number;

    maxPullDistance?: number;

    disabled?: boolean;

    indicator?: ReactNode;

    pullLabel?: string;

    releaseLabel?: string;

    refreshingLabel?: string;
}

export interface PullToRefreshRef {
    state: PullToRefreshState;

    refresh: () => Promise<void>;
}

export const PullToRefresh = forwardRef<PullToRefreshRef, PullToRefreshProps>(
    (
        {
            children,
            onRefresh,
            threshold = 80,
            maxPullDistance = 150,
            disabled = false,
            indicator,
            pullLabel = 'Pull to refresh',
            releaseLabel = 'Release to refresh',
            refreshingLabel = 'Refreshing...',
            className,
            ...props
        },
        ref,
    ) => {
        const containerRef = useRef<HTMLDivElement>(null);
        const isMobile = useMediaQuery('(max-width: 767px)');
        const [state, setState] = useState<PullToRefreshState>('idle');
        const [pullDistance, setPullDistance] = useState(0);

        const startYRef = useRef(0);
        const isPullingRef = useRef(false);

        const getScrollOwner = useCallback(() => {
            const container = containerRef.current;
            if (!container) {
                return null;
            }

            return isMobile
                ? (container.closest<HTMLElement>('.app-shell__main, .admin-shell__main') ?? container)
                : container;
        }, [isMobile]);

        const refresh = useCallback(async () => {
            if (state === 'refreshing' || disabled) {
                return;
            }

            setState('refreshing');
            setPullDistance(threshold);
            try {
                await onRefresh();
            } finally {
                setState('idle');
                setPullDistance(0);
            }
        }, [onRefresh, state, disabled, threshold]);

        useImperativeHandle(ref, () => ({ state, refresh }), [state, refresh]);

        const handleTouchStart = useCallback(
            (e: React.TouchEvent) => {
                if (disabled || state === 'refreshing') {
                    return;
                }

                const scrollOwner = getScrollOwner();
                if (!scrollOwner) {
                    return;
                }

                const isAtTop = scrollOwner.scrollTop <= 0;
                if (!isAtTop) {
                    return;
                }

                startYRef.current = e.touches[0].clientY;
                isPullingRef.current = true;
            },
            [disabled, getScrollOwner, state],
        );

        const handleTouchMove = useCallback(
            (e: React.TouchEvent) => {
                if (!isPullingRef.current || disabled || state === 'refreshing') {
                    return;
                }

                const scrollOwner = getScrollOwner();
                if (!scrollOwner) {
                    return;
                }

                const isAtTop = scrollOwner.scrollTop <= 0;
                if (!isAtTop) {
                    isPullingRef.current = false;
                    setState('idle');
                    setPullDistance(0);
                    return;
                }

                const currentY = e.touches[0].clientY;
                const delta = currentY - startYRef.current;

                if (delta < 0) {
                    isPullingRef.current = false;
                    setState('idle');
                    setPullDistance(0);
                    return;
                }

                const resistance = 0.5;
                const distance = Math.min(delta * resistance, maxPullDistance);
                setPullDistance(distance);

                if (distance >= threshold) {
                    setState('ready');
                } else if (distance > 0) {
                    setState('pulling');
                }
            },
            [disabled, getScrollOwner, state, threshold, maxPullDistance],
        );

        const handleTouchEnd = useCallback(() => {
            if (!isPullingRef.current) {
                return;
            }

            isPullingRef.current = false;

            if (state === 'ready') {
                void refresh();
            } else {
                setState('idle');
                setPullDistance(0);
            }
        }, [state, refresh]);

        const handleTouchCancel = useCallback(() => {
            if (!isPullingRef.current) {
                return;
            }

            isPullingRef.current = false;
            setState('idle');
            setPullDistance(0);
        }, []);

        const progress = Math.min(pullDistance / threshold, 1);

        const classes = [
            'pull-to-refresh',
            `pull-to-refresh--${state}`,
            disabled && 'pull-to-refresh--disabled',
            className,
        ]
            .filter(Boolean)
            .join(' ');

        const getLabel = () => {
            switch (state) {
                case 'idle':
                    return pullLabel;
                case 'pulling':
                    return pullLabel;
                case 'ready':
                    return releaseLabel;
                case 'refreshing':
                    return refreshingLabel;
            }
        };

        return (
            <div className={classes} data-state={state} data-disabled={disabled || undefined} {...props}>
                <div
                    className="pull-to-refresh__indicator"
                    style={{
                        height: pullDistance > 0 || state === 'refreshing' ? `${pullDistance}px` : 0,
                        opacity: progress,
                    }}
                    aria-live="polite"
                    aria-busy={state === 'refreshing'}
                >
                    {indicator ?? (
                        <div className="pull-to-refresh__default-indicator">
                            <span
                                className="pull-to-refresh__icon"
                                style={{
                                    transform: `rotate(${progress * 180}deg)`,
                                }}
                            >
                                <ArrowClockwiseIcon
                                    size={24}
                                    weight="bold"
                                    className={state === 'refreshing' ? 'pull-to-refresh__icon--spinning' : ''}
                                />
                            </span>
                            <span className="pull-to-refresh__label">{getLabel()}</span>
                        </div>
                    )}
                </div>
                <div
                    ref={containerRef}
                    className="pull-to-refresh__content"
                    onTouchStart={handleTouchStart}
                    onTouchMove={handleTouchMove}
                    onTouchEnd={handleTouchEnd}
                    onTouchCancel={handleTouchCancel}
                >
                    {children}
                </div>
            </div>
        );
    },
);

PullToRefresh.displayName = 'PullToRefresh';

export default PullToRefresh;
