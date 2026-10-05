import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
import { fileURLToPath } from 'node:url';

const outDir = fileURLToPath(new URL('../Win11DesktopApp/WebPanel/docx-editor', import.meta.url));

export default defineConfig({
  base: './',
  plugins: [react()],
  build: {
    outDir,
    emptyOutDir: true,
    target: 'es2022',
    chunkSizeWarningLimit: 8000,
    sourcemap: false,
  },
});
