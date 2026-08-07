import { useCallback, useState } from 'react';

interface AddAnotherStepState {
    formOpen: boolean;

    setFormOpen: (open: boolean) => void;

    added: boolean;

    markAdded: () => void;

    addAnother: () => void;

    continueToNext: () => void;
}

export function useAddAnotherStep(onAdvance: () => void): AddAnotherStepState {
    const [formOpen, setFormOpen] = useState(false);
    const [added, setAdded] = useState(false);

    const markAdded = useCallback(() => {
        setFormOpen(false);
        setAdded(true);
    }, []);

    const addAnother = useCallback(() => {
        setAdded(false);
        setFormOpen(true);
    }, []);

    const continueToNext = useCallback(() => {
        onAdvance();
    }, [onAdvance]);

    return { formOpen, setFormOpen, added, markAdded, addAnother, continueToNext };
}
