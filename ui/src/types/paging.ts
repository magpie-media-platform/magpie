export interface PagedList<T> {
    items: T[];
    pageNumber: number | null;
    pageSize: number | null;
    totalItems: number;
}
