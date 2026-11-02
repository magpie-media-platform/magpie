import './AlphabetRail.css';
import { useRef, useState, type PointerEvent } from 'react';

const LETTERS = Array.from('ABCDEFGHIJKLMNOPQRSTUVWXYZ');

interface AlphabetRailProps {
    titlePrefix: string;
    onTitlePrefixChange: (prefix: string) => void;
}

interface Press {
    letter: string;
    moved: boolean;
}

function letterAtHeight(rail: HTMLElement, clientY: number): string {
    const letters = Array.from(rail.querySelectorAll<HTMLElement>('[data-letter]'));
    const hit = letters.find((letter) => {
        const { top, bottom } = letter.getBoundingClientRect();
        return clientY >= top && clientY < bottom;
    });
    const nearest =
        hit ?? (clientY < letters[0].getBoundingClientRect().top ? letters[0] : letters[letters.length - 1]);

    return nearest.dataset.letter!;
}

export function AlphabetRail({ titlePrefix, onTitlePrefixChange }: AlphabetRailProps) {
    const activeLetter = titlePrefix.toUpperCase();
    const press = useRef<Press | null>(null);
    const [previewLetter, setPreviewLetter] = useState<string | null>(null);

    const handlePointerDown = (event: PointerEvent<HTMLDivElement>) => {
        const letter = (event.target as HTMLElement).closest<HTMLElement>('[data-letter]')?.dataset.letter;
        if (!letter || event.button !== 0) {
            return;
        }

        event.currentTarget.setPointerCapture(event.pointerId);
        press.current = { letter, moved: false };
        setPreviewLetter(letter);
    };

    const handlePointerMove = (event: PointerEvent<HTMLDivElement>) => {
        if (!press.current) {
            return;
        }

        const letter = letterAtHeight(event.currentTarget, event.clientY);
        if (letter !== press.current.letter) {
            press.current = { letter, moved: true };
            setPreviewLetter(letter);
        }
    };

    const handlePointerUp = () => {
        const current = press.current;
        press.current = null;
        setPreviewLetter(null);
        if (current) {
            onTitlePrefixChange(current.letter === activeLetter && !current.moved ? '' : current.letter);
        }
    };

    const handlePointerCancel = () => {
        press.current = null;
        setPreviewLetter(null);
    };

    return (
        <div
            className="alphabet-rail"
            role="group"
            aria-label="Filter by first letter"
            data-component="alphabet-rail"
            onPointerDown={handlePointerDown}
            onPointerMove={handlePointerMove}
            onPointerUp={handlePointerUp}
            onPointerCancel={handlePointerCancel}
        >
            {LETTERS.map((letter) => (
                <button
                    key={letter}
                    type="button"
                    className="alphabet-rail__letter"
                    data-letter={letter}
                    data-preview={letter === previewLetter || undefined}
                    aria-pressed={letter === activeLetter}
                    onClick={(event) => {
                        if (event.detail === 0) {
                            onTitlePrefixChange(letter === activeLetter ? '' : letter);
                        }
                    }}
                >
                    {letter}
                </button>
            ))}
        </div>
    );
}

AlphabetRail.displayName = 'AlphabetRail';

export default AlphabetRail;
