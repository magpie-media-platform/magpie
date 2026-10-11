import { forwardRef, type HTMLAttributes } from 'react';
import { Link } from 'react-router-dom';
import {
    ArrowRightIcon,
    CheckCircleIcon,
    CircleIcon,
    CompassIcon,
    DownloadSimpleIcon,
    MagnifyingGlassIcon,
    WarningCircleIcon,
    XIcon,
} from '@phosphor-icons/react';

import { Badge } from '@/components/ui/Badge';
import { Button } from '@/components/ui/Button';
import { Card, CardBody, CardHeader } from '@/components/ui/Card';
import type { OnboardingStep } from '@/hooks/useOnboardingStatus';
import { useInstanceName } from '@/hooks/useInstanceName';

import './OnboardingChecklist.css';

export interface OnboardingChecklistProps extends HTMLAttributes<HTMLDivElement> {
    steps: OnboardingStep[];
    completedCount: number;
    onDismiss: () => void;
}

const SkeletonStep = () => (
    <li className="onboarding-checklist__step onboarding-checklist__step--skeleton" aria-hidden="true">
        <span className="onboarding-checklist__status onboarding-checklist__status--skeleton" />
        <span className="onboarding-checklist__step-body">
            <span className="onboarding-checklist__step-title onboarding-checklist__step-title--skeleton" />
            <span className="onboarding-checklist__step-reason onboarding-checklist__step-reason--skeleton" />
        </span>
    </li>
);

const StepRow = ({ step }: { step: OnboardingStep }) => {
    const instanceName = useInstanceName();
    const state = step.failed ? 'unknown' : step.done ? 'done' : 'todo';

    return (
        <li
            className="onboarding-checklist__step"
            data-step={step.key}
            data-done={step.done || undefined}
            data-state={state}
        >
            <span className="onboarding-checklist__status" aria-hidden="true">
                {state === 'unknown' && (
                    <WarningCircleIcon weight="fill" className="onboarding-checklist__status-icon--unknown" />
                )}
                {state === 'done' && (
                    <CheckCircleIcon weight="fill" className="onboarding-checklist__status-icon--done" />
                )}
                {state === 'todo' && (
                    <CircleIcon weight="duotone" className="onboarding-checklist__status-icon--todo" />
                )}
            </span>
            <span className="onboarding-checklist__step-body">
                <span className="onboarding-checklist__step-title">
                    {step.title}
                    <span className="onboarding-checklist__sr-only">
                        {state === 'unknown' ? ' (not checked)' : state === 'done' ? ' (done)' : ' (to do)'}
                    </span>
                </span>
                <span className="onboarding-checklist__step-reason">
                    {step.failed ? `${instanceName} could not check this step. It may already be done.` : step.reason}
                </span>
            </span>
            {!step.done && (
                <Button asChild variant="ghost" size="sm" className="onboarding-checklist__step-action">
                    <Link to={step.to} aria-label={`Set up: ${step.title}`}>
                        Set up
                        <ArrowRightIcon weight="bold" aria-hidden="true" />
                    </Link>
                </Button>
            )}
        </li>
    );
};

export const OnboardingChecklist = forwardRef<HTMLDivElement, OnboardingChecklistProps>(
    ({ steps, completedCount, onDismiss, className, ...props }, ref) => {
        const instanceName = useInstanceName();
        const classes = ['onboarding-checklist', className].filter(Boolean).join(' ');
        const total = steps.length;
        const loading = total === 0;

        return (
            <Card ref={ref} className={classes} data-component="onboarding-checklist" {...props}>
                <CardHeader className="onboarding-checklist__header">
                    <span className="onboarding-checklist__header-icon" aria-hidden="true">
                        <CompassIcon weight="duotone" />
                    </span>
                    <div className="onboarding-checklist__header-text">
                        <h2 className="onboarding-checklist__title">Finish setting up {instanceName}</h2>
                        <p className="onboarding-checklist__subtitle">Complete these steps to automate your library.</p>
                    </div>
                    {!loading && (
                        <Badge
                            variant="info"
                            badgeStyle="outline"
                            className="onboarding-checklist__progress"
                            aria-label={`${completedCount} of ${total} steps complete`}
                        >
                            {completedCount} / {total}
                        </Badge>
                    )}
                    <Button
                        variant="ghost"
                        size="sm"
                        className="onboarding-checklist__dismiss"
                        onClick={onDismiss}
                        aria-label="Dismiss setup checklist"
                    >
                        <XIcon weight="bold" aria-hidden="true" />
                    </Button>
                </CardHeader>

                <CardBody className="onboarding-checklist__body">
                    <ol className="onboarding-checklist__steps" role="list">
                        {loading ? (
                            <>
                                <SkeletonStep />
                                <SkeletonStep />
                                <SkeletonStep />
                                <SkeletonStep />
                            </>
                        ) : (
                            steps.map((step) => <StepRow key={step.key} step={step} />)
                        )}
                    </ol>

                    <section className="onboarding-checklist__overview" aria-label="How the pipeline works">
                        <h3 className="onboarding-checklist__overview-title">How it works once set up</h3>
                        <p className="onboarding-checklist__overview-text">
                            {instanceName} watches your monitored content, searches your indexers for releases, grabs
                            the best match to your download client, then imports the finished file into your library.
                        </p>
                        <ol className="onboarding-checklist__pipeline" aria-hidden="true">
                            <li className="onboarding-checklist__pipeline-step">
                                <CompassIcon weight="duotone" />
                                Monitor
                            </li>
                            <li className="onboarding-checklist__pipeline-step">
                                <MagnifyingGlassIcon weight="duotone" />
                                Search
                            </li>
                            <li className="onboarding-checklist__pipeline-step">
                                <DownloadSimpleIcon weight="duotone" />
                                Grab
                            </li>
                            <li className="onboarding-checklist__pipeline-step">
                                <CheckCircleIcon weight="duotone" />
                                Import
                            </li>
                        </ol>
                    </section>
                </CardBody>
            </Card>
        );
    },
);

OnboardingChecklist.displayName = 'OnboardingChecklist';

export default OnboardingChecklist;
