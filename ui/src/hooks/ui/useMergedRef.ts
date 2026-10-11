import { useCallback, type Ref, type RefCallback } from 'react';

/** Returns one ref callback that gives the node to both refs. */
export function useMergedRef<T>(first: Ref<T> | undefined, second: Ref<T> | undefined): RefCallback<T> {
    return useCallback(
        (node: T | null) => {
            for (const ref of [first, second]) {
                if (typeof ref === 'function') {
                    ref(node);
                } else if (ref) {
                    ref.current = node;
                }
            }
        },
        [first, second],
    );
}
