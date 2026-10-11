import { useLayoutEffect } from 'react';

const ROW_SIZE_PROPERTY = '--tab-row-size';

function clearRows(list: HTMLElement): void {
    for (const child of Array.from(list.children) as HTMLElement[]) {
        child.style.removeProperty(ROW_SIZE_PROPERTY);
    }
}

function assignRows(list: HTMLElement, widestChild: number): void {
    const children = Array.from(list.children) as HTMLElement[];
    const style = getComputedStyle(list);
    const gap = parseFloat(style.columnGap) || 0;
    const room = list.clientWidth - (parseFloat(style.paddingLeft) || 0) - (parseFloat(style.paddingRight) || 0);
    const perRow = Math.max(1, Math.floor((room + gap) / (widestChild + gap)));
    const rows = Math.ceil(children.length / perRow);
    const fewest = Math.floor(children.length / rows);
    const longRows = children.length % rows;

    let index = 0;
    for (let row = 0; row < rows; row++) {
        const size = row < longRows ? fewest + 1 : fewest;
        for (let count = 0; count < size; count++) {
            children[index++].style.setProperty(ROW_SIZE_PROPERTY, String(size));
        }
    }
}

function holdsEveryChild(list: HTMLElement): boolean {
    if (list.scrollWidth > list.clientWidth) {
        return false;
    }

    const children = Array.from(list.children) as HTMLElement[];
    if (children.length === 0 || getComputedStyle(children[0]).flexBasis !== '0px') {
        return true;
    }

    const widths = children.map((child) => child.offsetWidth);
    return Math.max(...widths) - Math.min(...widths) <= 1;
}

function fitElement(list: HTMLElement, fits: readonly string[]): void {
    clearRows(list);
    for (const fit of fits) {
        if (fit === 'rows') {
            const widestChild = Math.max(
                0,
                ...(Array.from(list.children) as HTMLElement[]).map((child) => child.offsetWidth),
            );
            list.dataset.fit = fit;
            assignRows(list, widestChild);
            return;
        }
        list.dataset.fit = fit;
        if (holdsEveryChild(list)) {
            return;
        }
    }
}

/** Sets `data-fit` on the element to the first of the fits whose layout holds every child. */
export function useFit(list: HTMLElement | null, fits: readonly string[]): void {
    const fitsKey = fits.join(',');

    useLayoutEffect(() => {
        if (!list) {
            return;
        }

        const candidates = fitsKey.split(',');
        let width = -1;
        const refit = () => fitElement(list, candidates);
        const handleResize = () => {
            if (list.clientWidth !== width) {
                width = list.clientWidth;
                refit();
            }
        };

        refit();
        width = list.clientWidth;
        const resizeObserver = new ResizeObserver(handleResize);
        resizeObserver.observe(list);
        const mutationObserver = new MutationObserver(refit);
        mutationObserver.observe(list, { childList: true, characterData: true, subtree: true });
        let mounted = true;
        void document.fonts?.ready.then(() => {
            if (mounted) {
                refit();
            }
        });

        return () => {
            mounted = false;
            resizeObserver.disconnect();
            mutationObserver.disconnect();
        };
    }, [list, fitsKey]);
}
