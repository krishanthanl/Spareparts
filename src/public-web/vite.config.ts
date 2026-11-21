import { defineConfig } from 'vitest/config';
import react from '@vitejs/plugin-react';
import { loadEnv } from 'vite';
export default defineConfig(({ mode }) => ({
  plugins: [
    react(),
    {
      name: 'storefront-robots',
      apply: 'build',
      generateBundle() {
        const apiBase = loadEnv(mode, process.cwd(), 'VITE_').VITE_API_BASE_URL;
        const sitemap = apiBase
          ? new URL('/api/public/sitemap.xml', apiBase)
          : null;
        this.emitFile({
          type: 'asset',
          fileName: 'robots.txt',
          source: `User-agent: *\nAllow: /\n${sitemap ? `Sitemap: ${sitemap.href}\n` : ''}`,
        });
      },
    },
  ],
  test: {
    environment: 'jsdom',
    setupFiles: './src/test/setup.ts',
    globals: true,
    restoreMocks: true,
  },
}));
