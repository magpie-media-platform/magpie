import { renderHook } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';

import { useFit } from './useFit';

const FITS = ['labels', 'icons', 'rows'];

describe('useFit', () => {
    afterEach(() => {
        vi.unstubAllGlobals();
        vi.restoreAllMocks();
    });

    it('useFit_SetsTheFirstFit_WhenEveryChildHolds', () => {
        // Arrange
        const list = document.createElement('div');
        list.innerHTML = '<button></button>'.repeat(4);
        vi.spyOn(HTMLElement.prototype, 'offsetWidth', 'get').mockReturnValue(60);
        Object.defineProperty(list, 'clientWidth', { configurable: true, value: 300 });
        Object.defineProperty(list, 'scrollWidth', { configurable: true, value: 300 });

        // Act
        renderHook(() => useFit(list, FITS));

        // Assert
        expect(list.dataset.fit).toBe('labels');
    });

    it('useFit_SetsTheNextFit_WhenTheFirstOverflows', () => {
        // Arrange
        const list = document.createElement('div');
        list.innerHTML = '<button></button>'.repeat(4);
        vi.spyOn(HTMLElement.prototype, 'offsetWidth', 'get').mockReturnValue(44);
        Object.defineProperty(list, 'clientWidth', { configurable: true, value: 300 });
        Object.defineProperty(list, 'scrollWidth', {
            configurable: true,
            get: () => (list.dataset.fit === 'labels' ? 420 : 300),
        });

        // Act
        renderHook(() => useFit(list, FITS));

        // Assert
        expect(list.dataset.fit).toBe('icons');
    });

    it('useFit_SetsTheLastFit_WhenNoFitHolds', () => {
        // Arrange
        const list = document.createElement('div');
        list.innerHTML = '<button></button>';
        vi.spyOn(HTMLElement.prototype, 'offsetWidth', 'get').mockReturnValue(200);
        Object.defineProperty(list, 'clientWidth', { configurable: true, value: 60 });
        Object.defineProperty(list, 'scrollWidth', { configurable: true, value: 200 });

        // Act
        renderHook(() => useFit(list, ['full', 'small', 'wrap']));

        // Assert
        expect(list.dataset.fit).toBe('wrap');
    });

    it('useFit_KeepsTheEqualFit_WhenTheChildrenShareTheRowWithTheSameWidth', () => {
        // Arrange
        const list = document.createElement('div');
        list.innerHTML = '<button style="flex-basis: 0px"></button>'.repeat(3);
        vi.spyOn(HTMLElement.prototype, 'offsetWidth', 'get').mockReturnValue(99);
        Object.defineProperty(list, 'clientWidth', { configurable: true, value: 300 });
        Object.defineProperty(list, 'scrollWidth', { configurable: true, value: 300 });

        // Act
        renderHook(() => useFit(list, ['equal', 'content']));

        // Assert
        expect(list.dataset.fit).toBe('equal');
    });

    it('useFit_MovesPastTheEqualFit_WhenOneChildNeedsMoreThanAnEqualShare', () => {
        // Arrange
        const list = document.createElement('div');
        list.innerHTML = '<button style="flex-basis: 0px"></button>'.repeat(3);
        vi.spyOn(HTMLElement.prototype, 'offsetWidth', 'get').mockReturnValue(90);
        Object.defineProperty(list.children[2], 'offsetWidth', { configurable: true, value: 120 });
        Object.defineProperty(list, 'clientWidth', { configurable: true, value: 300 });
        Object.defineProperty(list, 'scrollWidth', { configurable: true, value: 300 });

        // Act
        renderHook(() => useFit(list, ['equal', 'content']));

        // Assert
        expect(list.dataset.fit).toBe('content');
    });

    it('useFit_WrapsNineChildrenIntoRowsOfFiveAndFour_WhenNoFitBeforeRowsHolds', () => {
        // Arrange
        const list = document.createElement('div');
        list.innerHTML = '<button></button>'.repeat(9);
        vi.spyOn(HTMLElement.prototype, 'offsetWidth', 'get').mockReturnValue(54);
        Object.defineProperty(list, 'clientWidth', { configurable: true, value: 288 });
        Object.defineProperty(list, 'scrollWidth', { configurable: true, value: 520 });

        // Act
        renderHook(() => useFit(list, FITS));

        // Assert
        expect(list.dataset.fit).toBe('rows');
        expect(
            Array.from(list.querySelectorAll<HTMLElement>('button'), (child) =>
                child.style.getPropertyValue('--tab-row-size'),
            ),
        ).toEqual(['5', '5', '5', '5', '5', '4', '4', '4', '4']);
    });

    it('useFit_LeavesNoRowWithASingleChild_WhenThirteenChildrenWrap', () => {
        // Arrange
        const list = document.createElement('div');
        list.innerHTML = '<button></button>'.repeat(13);
        vi.spyOn(HTMLElement.prototype, 'offsetWidth', 'get').mockReturnValue(54);
        Object.defineProperty(list, 'clientWidth', { configurable: true, value: 288 });
        Object.defineProperty(list, 'scrollWidth', { configurable: true, value: 700 });

        // Act
        renderHook(() => useFit(list, FITS));

        // Assert
        expect(
            Array.from(list.querySelectorAll<HTMLElement>('button'), (child) =>
                child.style.getPropertyValue('--tab-row-size'),
            ),
        ).toEqual(['5', '5', '5', '5', '5', '4', '4', '4', '4', '4', '4', '4', '4']);
    });

    it('useFit_FitsAgain_WhenTheWidthChanges', () => {
        // Arrange
        let clientWidth = 600;
        let notify: ResizeObserverCallback = () => {};
        vi.stubGlobal(
            'ResizeObserver',
            class {
                constructor(callback: ResizeObserverCallback) {
                    notify = callback;
                }
                observe = vi.fn();
                disconnect = vi.fn();
            },
        );
        const list = document.createElement('div');
        list.innerHTML = '<button></button>'.repeat(4);
        vi.spyOn(HTMLElement.prototype, 'offsetWidth', 'get').mockReturnValue(44);
        Object.defineProperty(list, 'clientWidth', { configurable: true, get: () => clientWidth });
        Object.defineProperty(list, 'scrollWidth', {
            configurable: true,
            get: () => (list.dataset.fit === 'labels' && clientWidth < 400 ? 420 : clientWidth),
        });
        renderHook(() => useFit(list, FITS));
        const fitBefore = list.dataset.fit;

        // Act
        clientWidth = 300;
        notify([], {} as ResizeObserver);

        // Assert
        expect(fitBefore).toBe('labels');
        expect(list.dataset.fit).toBe('icons');
    });

    it('useFit_ClearsTheRowSizes_WhenALaterFitHolds', () => {
        // Arrange
        let clientWidth = 288;
        let notify: ResizeObserverCallback = () => {};
        vi.stubGlobal(
            'ResizeObserver',
            class {
                constructor(callback: ResizeObserverCallback) {
                    notify = callback;
                }
                observe = vi.fn();
                disconnect = vi.fn();
            },
        );
        const list = document.createElement('div');
        list.innerHTML = '<button></button>'.repeat(9);
        vi.spyOn(HTMLElement.prototype, 'offsetWidth', 'get').mockReturnValue(54);
        Object.defineProperty(list, 'clientWidth', { configurable: true, get: () => clientWidth });
        Object.defineProperty(list, 'scrollWidth', {
            configurable: true,
            get: () => (list.dataset.fit === 'icons' && clientWidth >= 600 ? clientWidth : 700),
        });
        renderHook(() => useFit(list, FITS));
        const fitBefore = list.dataset.fit;

        // Act
        clientWidth = 640;
        notify([], {} as ResizeObserver);

        // Assert
        expect(fitBefore).toBe('rows');
        expect(list.dataset.fit).toBe('icons');
        expect(
            Array.from(list.querySelectorAll<HTMLElement>('button'), (child) =>
                child.style.getPropertyValue('--tab-row-size'),
            ),
        ).toEqual(['', '', '', '', '', '', '', '', '']);
    });
});
