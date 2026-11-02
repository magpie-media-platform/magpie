import { Link } from 'react-router-dom';
import { ImageBrokenIcon } from '@phosphor-icons/react';

import { Avatar } from '@/components/ui/Avatar';

import './SubjectCard.css';

interface SubjectCardProps {
    subjectId?: string | null;
    name: string;
    imageUrl?: string | null;
    qualifier?: string | null;
    testId?: string;
}

export function SubjectCard({ subjectId, name, imageUrl, qualifier, testId }: SubjectCardProps) {
    const body = (
        <>
            <Avatar
                key={imageUrl}
                src={imageUrl ?? undefined}
                alt={name}
                loading="lazy"
                className="subject-card__avatar"
                fallback={<ImageBrokenIcon weight="thin" aria-hidden="true" data-testid="subject-avatar-placeholder" />}
            />
            <span className="subject-card__name">{name}</span>
            {qualifier && <span className="subject-card__qualifier">{qualifier}</span>}
        </>
    );

    return subjectId ? (
        <Link
            to={`/subjects/${subjectId}`}
            className="subject-card"
            aria-label={qualifier ? `${name}, ${qualifier}` : name}
            data-testid={testId}
        >
            {body}
        </Link>
    ) : (
        <div className="subject-card subject-card--dangling" data-testid={testId}>
            {body}
        </div>
    );
}
