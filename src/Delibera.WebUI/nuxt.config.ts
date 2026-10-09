import { defineNuxtConfig } from 'nuxt/config'

// https://nuxt.com/docs/api/configuration/nuxt-config
export default defineNuxtConfig({
  compatibilityDate: '2026-01-01',

  // SSR stays on for the container image: the BFF proxy runs server-side, which is the whole
  // reason the browser never touches the API origin. Debates are a private, non-indexed tool
  // surface, so the SEO argument for CSR does not apply here.
  //
  // The GITHUB PAGES build is the opposite case. `configure-pages` with
  // `static_site_generator: nuxt` flips this to `target: 'static'` and injects `router.base` at
  // build time — a static export has no Nitro server, so the BFF route does not exist there.
  // That build therefore needs `NUXT_PUBLIC_DELIBERA_API_BASE` pointing at a reachable server
  // origin that allows this one via CORS. See `resolveApiBase()` in app/composables/useDebates.ts.
  ssr: true,

  devtools: { enabled: false },

  // Required for the accessibility tree and for a usable browser tab title — Lighthouse
  // fails the document without all three of these.
  app: {
    head: {
      htmlAttrs: { lang: 'en' },
      title: 'Delibera — AI council',
      meta: [
        {
          name: 'description',
          content:
            'Run multi-model AI council debates and watch the rounds arrive live.',
        },
        { name: 'viewport', content: 'width=device-width, initial-scale=1' },
      ],
    },
  },

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

    // Needed ONLY by the static export (`nuxt generate` / GitHub Pages).
    //
    // Without an explicit route list, Nuxt 4 emits just the SPA fallback pair
    // (200.html + 404.html) and no document for the real pages — a Pages deploy of that is a
    // blank site. `/debates/[id]` is deliberately absent: its ids only exist at runtime, so it
    // renders client-side from whatever the id in the URL is.
    prerender: {
      routes: ['/', '/debates/new'],
    },
  },

  // Route rules are deliberately empty. Debate status is mutable: caching /debates/** with
  // swr or isr would serve a stale "Running" state and defeat the live view.
  routeRules: {},

  typescript: {
    strict: true,
    typeCheck: false,
  },
})