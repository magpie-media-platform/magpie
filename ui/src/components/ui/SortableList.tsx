import {
    useCallback,
    useEffect,
    useMemo,
    useRef,
    useState,
    type DragEvent,
    type RefObject,
    type TouchEvent,
} from 'react';
import { CaretDownIcon, CaretUpIcon, CircleNotchIcon, DotsSixVerticalIcon } from '@phosphor-icons/react';

import { Button } from '@/components/ui/Button';

import './SortableList.css';

export type SortableMoveDirection = 'up' | 'down';
export type SortableDropPosition = 'before' | 'after';

interface SortableItem {
    id: string;
}

interface SortableMove<T> {
    item: T;
    position: number;
    total: number;
}

interface UseSortableListOptions<T extends SortableItem> {
    items: T[];
    getLabel: (item: T) => string;
    onReorder: (orderedIds: string[]) => Promise<void>;
    onReorderSuccess?: (move: SortableMove<T>) => void;
    onReorderError?: (error: unknown, item: T) => void;
    disabled?: boolean;
}

interface KeyboardFocusTarget {
    id: string;
    direction: SortableMoveDirection;
}

export function useSortableList<T extends SortableItem>({
    items,
    getLabel,
    onReorder,
    onReorderSuccess,
    onReorderError,
    disabled = false,
}: UseSortableListOptions<T>) {
    const [orderedIds, setOrderedIds] = useState<string[] | null>(null);
    const [movingId, setMovingId] = useState<string | null>(null);
    const [draggedId, setDraggedId] = useState<string | null>(null);
    const [dropTargetId, setDropTargetId] = useState<string | null>(null);
    const [dropPosition, setDropPosition] = useState<SortableDropPosition | null>(null);
    const [announcement, setAnnouncement] = useState('');
    const [keyboardFocusTarget, setKeyboardFocusTarget] = useState<KeyboardFocusTarget | null>(null);
    const dragImageRef = useRef<HTMLDivElement>(null);
    const sourceIdRef = useRef<string | null>(null);
    const dropTargetIdRef = useRef<string | null>(null);
    const dropPositionRef = useRef<SortableDropPosition | null>(null);

    const displayItems = useMemo(() => {
        if (!orderedIds) {
            return items;
        }

        const byId = new Map(items.map((item) => [item.id, item]));
        const ordered = orderedIds.map((id) => byId.get(id)).filter((item): item is T => item !== undefined);
        for (const item of items) {
            if (!orderedIds.includes(item.id)) {
                ordered.push(item);
            }
        }
        return ordered;
    }, [items, orderedIds]);

    const clearDropTarget = useCallback(() => {
        dropTargetIdRef.current = null;
        dropPositionRef.current = null;
        setDropTargetId(null);
        setDropPosition(null);
    }, []);

    const clearDrag = useCallback(() => {
        sourceIdRef.current = null;
        setDraggedId(null);
        clearDropTarget();
    }, [clearDropTarget]);

    const setTarget = useCallback((targetId: string, position: SortableDropPosition) => {
        dropTargetIdRef.current = targetId;
        dropPositionRef.current = position;
        setDropTargetId(targetId);
        setDropPosition(position);
    }, []);

    const commitMove = useCallback(
        async (
            sourceId: string,
            targetId: string,
            position: SortableDropPosition,
            focusDirection: SortableMoveDirection | null,
        ) => {
            if (disabled || movingId || sourceId === targetId) {
                return;
            }

            const sourceIndex = displayItems.findIndex((item) => item.id === sourceId);
            const targetIndex = displayItems.findIndex((item) => item.id === targetId);
            if (sourceIndex === -1 || targetIndex === -1) {
                return;
            }

            const reordered = [...displayItems];
            const [moved] = reordered.splice(sourceIndex, 1);
            let insertIndex = targetIndex + (position === 'after' ? 1 : 0);
            if (sourceIndex < targetIndex) {
                insertIndex -= 1;
            }
            if (insertIndex === sourceIndex) {
                return;
            }

            reordered.splice(insertIndex, 0, moved);
            setOrderedIds(reordered.map((item) => item.id));
            setMovingId(moved.id);

            let succeeded = false;
            try {
                await onReorder(reordered.map((item) => item.id));
                succeeded = true;
                setAnnouncement(`${getLabel(moved)} moved to position ${insertIndex + 1} of ${reordered.length}.`);
                onReorderSuccess?.({ item: moved, position: insertIndex, total: reordered.length });
            } catch (error) {
                setAnnouncement(`Failed to move ${getLabel(moved)}.`);
                onReorderError?.(error, moved);
            } finally {
                setOrderedIds(null);
                setMovingId(null);
                if (focusDirection) {
                    const atBoundary = succeeded && (insertIndex === 0 || insertIndex === reordered.length - 1);
                    setKeyboardFocusTarget({
                        id: moved.id,
                        direction: atBoundary ? (focusDirection === 'up' ? 'down' : 'up') : focusDirection,
                    });
                }
            }
        },
        [disabled, displayItems, getLabel, movingId, onReorder, onReorderError, onReorderSuccess],
    );

    const handleDragStart = useCallback(
        (item: T, event: DragEvent) => {
            if (disabled || movingId) {
                event.preventDefault();
                return;
            }

            sourceIdRef.current = item.id;
            setDraggedId(item.id);
            if (dragImageRef.current) {
                dragImageRef.current.textContent = getLabel(item);
                dragImageRef.current.style.display = 'block';
                event.dataTransfer.setDragImage(dragImageRef.current, 0, 0);
                requestAnimationFrame(() => {
                    if (dragImageRef.current) {
                        dragImageRef.current.style.display = 'none';
                    }
                });
            }
            event.dataTransfer.effectAllowed = 'move';
            event.dataTransfer.setData('text/plain', item.id);
        },
        [disabled, getLabel, movingId],
    );

    const handleDragOver = useCallback(
        (targetId: string, event: DragEvent) => {
            const sourceId = sourceIdRef.current ?? draggedId;
            if (disabled || movingId || !sourceId || sourceId === targetId) {
                return;
            }

            event.preventDefault();
            event.dataTransfer.dropEffect = 'move';
            const rect = event.currentTarget.getBoundingClientRect();
            setTarget(targetId, event.clientY < rect.top + rect.height / 2 ? 'before' : 'after');
        },
        [disabled, draggedId, movingId, setTarget],
    );

    const handleDrop = useCallback(
        (targetId: string, event: DragEvent) => {
            event.preventDefault();
            const sourceId = event.dataTransfer.getData('text/plain') || draggedId;
            const position = dropPositionRef.current;
            clearDrag();
            if (sourceId && position) {
                void commitMove(sourceId, targetId, position, null);
            }
        },
        [clearDrag, commitMove, draggedId],
    );

    const handleTouchStart = useCallback(
        (item: T, event: TouchEvent) => {
            if (disabled || movingId) {
                return;
            }

            event.preventDefault();
            sourceIdRef.current = item.id;
            setDraggedId(item.id);
        },
        [disabled, movingId],
    );

    const handleTouchMove = useCallback(
        (event: TouchEvent) => {
            const sourceId = sourceIdRef.current;
            const touch = event.touches[0];
            if (!sourceId || !touch) {
                return;
            }

            event.preventDefault();
            const element = document.elementFromPoint?.(touch.clientX, touch.clientY);
            const target = element?.closest<HTMLElement>('[data-sortable-list-item-id]');
            const targetId = target?.dataset.sortableListItemId;
            if (!target || !targetId || targetId === sourceId || !displayItems.some((item) => item.id === targetId)) {
                clearDropTarget();
                return;
            }

            const rect = target.getBoundingClientRect();
            setTarget(targetId, touch.clientY < rect.top + rect.height / 2 ? 'before' : 'after');
        },
        [clearDropTarget, displayItems, setTarget],
    );

    const handleTouchEnd = useCallback(() => {
        const sourceId = sourceIdRef.current;
        const targetId = dropTargetIdRef.current;
        const position = dropPositionRef.current;
        clearDrag();
        if (sourceId && targetId && position) {
            void commitMove(sourceId, targetId, position, null);
        }
    }, [clearDrag, commitMove]);

    const handleMove = useCallback(
        (item: T, direction: SortableMoveDirection) => {
            const sourceIndex = displayItems.findIndex((candidate) => candidate.id === item.id);
            const targetIndex = direction === 'up' ? sourceIndex - 1 : sourceIndex + 1;
            if (sourceIndex === -1 || targetIndex < 0 || targetIndex >= displayItems.length) {
                return;
            }

            void commitMove(item.id, displayItems[targetIndex].id, direction === 'up' ? 'before' : 'after', direction);
        },
        [commitMove, displayItems],
    );

    return {
        items: displayItems,
        disabled,
        hasOptimisticOrder: orderedIds !== null,
        movingId,
        isMoving: movingId !== null,
        draggedId,
        dropTargetId,
        dropPosition,
        announcement,
        keyboardFocusTarget,
        dragImageRef,
        handleDragStart,
        handleDragEnd: clearDrag,
        handleDragOver,
        handleDragLeave: clearDropTarget,
        handleDrop,
        handleTouchStart,
        handleTouchMove,
        handleTouchEnd,
        handleTouchCancel: clearDrag,
        handleMove,
        handleFocusRestored: () => setKeyboardFocusTarget(null),
    };
}

export function sortableListItemClassNames(
    isDragging: boolean,
    isDropTarget: boolean,
    dropPosition: SortableDropPosition | null,
): string {
    return [
        'sortable-list__item',
        isDragging && 'sortable-list__item--dragging',
        isDropTarget && 'sortable-list__item--drop-target',
        isDropTarget && dropPosition === 'before' && 'sortable-list__item--drop-before',
        isDropTarget && dropPosition === 'after' && 'sortable-list__item--drop-after',
    ]
        .filter(Boolean)
        .join(' ');
}

interface SortableListControlsProps {
    label: string;
    position: number;
    itemCount: number;
    moving: boolean;
    disabled: boolean;
    focusDirection: SortableMoveDirection | null;
    focusFallbackRef?: RefObject<HTMLElement | null>;
    dragHandleClassName?: string;
    orderControlsClassName?: string;
    orderButtonClassName?: string;
    spinnerClassName?: string;
    dragHandleTestId?: string;
    actionName: string;
    onDragStart: (event: DragEvent) => void;
    onDragEnd: () => void;
    onTouchStart: (event: TouchEvent) => void;
    onTouchMove: (event: TouchEvent) => void;
    onTouchEnd: () => void;
    onTouchCancel: () => void;
    onMove: (direction: SortableMoveDirection) => void;
    onFocusRestored: () => void;
}

export function SortableListControls({
    label,
    position,
    itemCount,
    moving,
    disabled,
    focusDirection,
    focusFallbackRef,
    dragHandleClassName,
    orderControlsClassName,
    orderButtonClassName,
    spinnerClassName,
    dragHandleTestId,
    actionName,
    onDragStart,
    onDragEnd,
    onTouchStart,
    onTouchMove,
    onTouchEnd,
    onTouchCancel,
    onMove,
    onFocusRestored,
}: SortableListControlsProps) {
    const moveUpRef = useRef<HTMLButtonElement>(null);
    const moveDownRef = useRef<HTMLButtonElement>(null);

    useEffect(() => {
        if (!focusDirection) {
            return;
        }

        const requestedButton = focusDirection === 'up' ? moveUpRef.current : moveDownRef.current;
        const siblingButton = focusDirection === 'up' ? moveDownRef.current : moveUpRef.current;
        const focusTarget =
            requestedButton && !requestedButton.disabled
                ? requestedButton
                : siblingButton && !siblingButton.disabled
                  ? siblingButton
                  : focusFallbackRef?.current;
        focusTarget?.focus();
        onFocusRestored();
    }, [focusDirection, focusFallbackRef, onFocusRestored]);

    return (
        <>
            <div
                className={['sortable-list__drag-handle', dragHandleClassName].filter(Boolean).join(' ')}
                data-testid={dragHandleTestId}
                data-sortable-drag-handle
                draggable={!disabled}
                onDragStart={onDragStart}
                onDragEnd={onDragEnd}
                onTouchStart={onTouchStart}
                onTouchMove={onTouchMove}
                onTouchEnd={onTouchEnd}
                onTouchCancel={onTouchCancel}
                aria-hidden="true"
            >
                {moving ? (
                    <CircleNotchIcon
                        weight="bold"
                        className={['sortable-list__spinner', spinnerClassName].filter(Boolean).join(' ')}
                    />
                ) : (
                    <DotsSixVerticalIcon weight="bold" />
                )}
            </div>
            <div
                className={['sortable-list__order-controls', orderControlsClassName].filter(Boolean).join(' ')}
                aria-label={`Reorder ${label}`}
            >
                <Button
                    ref={moveUpRef}
                    variant="ghost"
                    size="sm"
                    className={['sortable-list__order-button', orderButtonClassName].filter(Boolean).join(' ')}
                    aria-label={`Move ${label} up`}
                    title={`Move ${label} up`}
                    disabled={position === 0 || disabled}
                    onClick={() => onMove('up')}
                    data-action={`move-${actionName}-up`}
                >
                    <CaretUpIcon weight="bold" aria-hidden="true" />
                </Button>
                <Button
                    ref={moveDownRef}
                    variant="ghost"
                    size="sm"
                    className={['sortable-list__order-button', orderButtonClassName].filter(Boolean).join(' ')}
                    aria-label={`Move ${label} down`}
                    title={`Move ${label} down`}
                    disabled={position === itemCount - 1 || disabled}
                    onClick={() => onMove('down')}
                    data-action={`move-${actionName}-down`}
                >
                    <CaretDownIcon weight="bold" aria-hidden="true" />
                </Button>
            </div>
        </>
    );
}
