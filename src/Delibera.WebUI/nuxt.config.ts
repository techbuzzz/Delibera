import { defineNuxtConfig } from 'nuxt/config'

// https://nuxt.com/docs/api/configuration/nuxt-config
export default defineNuxtConfig({
  compatibilityDate: '2026-01-01',

  // SSR stays on: the BFF proxy runs server-side, which is the whole reason the browser
  // never touches the API origin. Debates are a private, non-indexed tool surface, so the
  // SEO argument for CSR does not apply here.
  ssr: true,

  devtools: { enabled: false },

  // No UI kit and no Tailwind. This app becomes a published container image, and every
  // dependency is image weight and supply-chain surface for a surface that is a handful of
  // forms and a timeline. Styling is a scoped stylesheet per component.
  modules: [],

  runtimeConfig: {
    // Server-only. Reaches the container as NUXT_DELIBERA_API_BASE and is never exposed to
    // the browser — the client only ever sees the same-origin /api/delibera proxy route.
    deliberaApiBase: 'http://localhost:5200',

    public: {
      // Sent as X-Tenant-Id by the BFF. The API falls back to "default" when the header is
      // absent, so it is always set explicitly — otherwise every user shares one bucket.
      tenantId: 'default',
    },
  },

  nitro: {
    preset: 'node-server',
  },

  // Route rules are deliberately empty. Debate status is mutable: caching /debates/** with
  // swr or isr would serve a stale "Running" state and defeat the live view.
  routeRules: {},

  typescript: {
    strict: true,
    typeCheck: false,
  },
})