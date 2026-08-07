import { useMediaQuery } from './useMediaQuery';

const CARD_LAYOUT_BREAKPOINT = 1024;
export const CARD_LAYOUT_QUERY = `(max-width: ${CARD_LAYOUT_BREAKPOINT - 1}px)`;
export const TABLE_LAYOUT_QUERY = `(min-width: ${CARD_LAYOUT_BREAKPOINT}px)`;

export function useCardLayout(): boolean {
    return useMediaQuery(CARD_LAYOUT_QUERY);
}
