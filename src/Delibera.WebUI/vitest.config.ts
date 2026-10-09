import { defineConfig } from 'vitest/config'
import { fileURLToPath } from 'node:url'

export default defineConfig({
  resolve: {
    alias: {
      '#shared': fileURLToPath(new URL('./shared', import.meta.url)),
    },
  },
  test: {
    environment: 'happy-dom',
    include: ['test/**/*.test.ts'],
    // The SSE test boots the built Nitro server and holds real sockets open, so it needs
    // room beyond the 5 s default.
    testTimeout: 20_000,
    hookTimeout: 60_000,
  },
})