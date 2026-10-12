import type { CapacitorConfig } from '@capacitor/cli';

// Native iOS and Android shells around the same Vite build (`pnpm build` first, then `pnpm cap sync`).
const config: CapacitorConfig = {
  appId: 'com.snipervssniper.game',
  appName: 'Sniper vs Sniper',
  webDir: 'dist',
  backgroundColor: '#111111',
  ios: { contentInset: 'never' },
  android: { allowMixedContent: false },
};

export default config;
