type ReactActGlobal = typeof globalThis & { IS_REACT_ACT_ENVIRONMENT?: boolean };

(globalThis as ReactActGlobal).IS_REACT_ACT_ENVIRONMENT = true;
if (typeof window !== 'undefined') {
    (window as Window & { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;
}
