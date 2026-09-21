export interface ScalarPatch<T> {
    value: T;
}

export interface DictionaryPatch<K extends string, V> {
    add?: Record<K, V>;
    remove?: K[];
    update?: Record<K, V>;
}
