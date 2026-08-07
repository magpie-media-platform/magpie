import { Fragment, type ReactNode } from 'react';
import { ArrowRightIcon, PlusIcon } from '@phosphor-icons/react';

import { Button } from '@/components/ui/Button';

interface AddAnotherActionsProps {
    label: string;

    action: string;

    onAddAnother: () => void;

    onContinue: () => void;

    disabled?: boolean;
}

function capitalize(value: string): string {
    return value.charAt(0).toUpperCase() + value.slice(1);
}

export function AddAnotherActions({
    label,
    action,
    onAddAnother,
    onContinue,
    disabled = false,
}: AddAnotherActionsProps): ReactNode {
    return (
        <Fragment>
            <p className="setup-page__hint" data-state="added-confirmation">
                {`${capitalize(label)} added. Add another or continue.`}
            </p>
            <Button
                type="button"
                variant="primary"
                size="lg"
                onClick={onAddAnother}
                disabled={disabled}
                className="setup-page__submit"
                data-action={`add-another-${action}`}
            >
                <PlusIcon weight="bold" aria-hidden="true" />
                {`Add another ${label}`}
            </Button>
            <Button
                type="button"
                variant="secondary"
                size="lg"
                onClick={onContinue}
                className="setup-page__submit"
                data-action={`continue-${action}`}
            >
                Continue
                <ArrowRightIcon weight="bold" aria-hidden="true" />
            </Button>
        </Fragment>
    );
}

AddAnotherActions.displayName = 'AddAnotherActions';
