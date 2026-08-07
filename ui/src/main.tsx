import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';

import { installPreloadErrorReload } from './lib/preloadErrorReload';
import './styles/index.css';
import App from './App.tsx';

installPreloadErrorReload();

createRoot(document.getElementById('root')!).render(
    <StrictMode>
        <App />
    </StrictMode>,
);
