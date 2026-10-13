import { defineConfig } from 'vite';

// VITE_INLINE_ASSETS=1 bakes every asset (skies, textures, the soldier model) into one script, for
// the single-file playable that cannot fetch files beside it.
const inline = process.env.VITE_INLINE_ASSETS === '1';

export default defineConfig({
  base: './',
  server: { host: true },
  assetsInclude: ['**/*.hdr', '**/*.glb'],
  build: {
    chunkSizeWarningLimit: 1200,
    assetsInlineLimit: inline ? () => true : 4096,
    rolldownOptions: inline ? { output: { inlineDynamicImports: true } } : {},
  },
});
