import type { Key, ReactNode } from 'react';

export interface DetailRow {
    key?: Key;
    label: ReactNode;
    value: ReactNode;
    mono?: boolean;
}
