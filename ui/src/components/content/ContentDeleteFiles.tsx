import type { UseQueryResult } from '@tanstack/react-query';

import { Alert } from '@/components/ui/Alert';
import { Skeleton } from '@/components/ui/Skeleton';
import { getErrorMessage } from '@/lib/errors';
import { formatFileSize } from '@/lib/formatSize';
import type { ContentDeleteFile } from '@/types/contentMetadata';

import './ContentDeleteFiles.css';

interface ContentDeleteFilesProps {
    preview: Pick<UseQueryResult<ContentDeleteFile[]>, 'data' | 'error' | 'isLoading'>;
}

export function ContentDeleteFiles({ preview }: ContentDeleteFilesProps) {
    const { data: files, error, isLoading } = preview;

    if (!isLoading && !error && !files) {
        return null;
    }

    return (
        <div className="content-delete-files" data-component="content-delete-files">
            {isLoading ? (
                <Skeleton variant="rect" height="4rem" />
            ) : error ? (
                <Alert variant="error">{getErrorMessage(error)}</Alert>
            ) : (
                files && (
                    <div className="content-delete-files__box">
                        {files.length > 0 && (
                            <ul className="content-delete-files__list" aria-label="Files to delete" role="list">
                                {files.map((file) => (
                                    <li key={file.path} className="content-delete-files__file">
                                        <span className="content-delete-files__path">{file.path}</span>
                                        <span className="content-delete-files__size">{formatFileSize(file.size)}</span>
                                    </li>
                                ))}
                            </ul>
                        )}
                        <div className="content-delete-files__total">
                            <span>Total</span>
                            <span>{formatFileSize(files.reduce((sum, file) => sum + file.size, 0))}</span>
                        </div>
                    </div>
                )
            )}
        </div>
    );
}

ContentDeleteFiles.displayName = 'ContentDeleteFiles';
