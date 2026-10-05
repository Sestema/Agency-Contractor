import { createRoot } from 'react-dom/client';
import '@docx-editor.dev/core/styles/editor.css';
import './app.css';
import { App } from './App';

createRoot(document.getElementById('root')!).render(<App />);
