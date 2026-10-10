import type { NextConfig } from 'next'

// GitHub Pages serves this site from https://techbuzzz.github.io/Delibera/, so every
// route and asset is prefixed with `/Delibera`. `actions/configure-pages` computes that
// prefix and hands it to the build as PAGES_BASE_PATH (see .github/workflows/pages.yml);
// locally the variable is absent and the site runs at `/`, which is what `npm run dev`
// wants. Keep the fallback in sync with lib/paths.ts - both read the same variable.
const basePath = process.env.PAGES_BASE_PATH ?? ''

const nextConfig: NextConfig = {
  // A Pages deploy is a static artifact: there is no Node server to render on demand.
  output: 'export',

  basePath,

  // Pages serves `/blog/v10.5.2/` (a directory) rather than `/blog/v10.5.2`. Without
  // this the export emits extensionless files and a visitor following a shared deep link
  // gets a 404 from the CDN.
  trailingSlash: true,

  // The image optimizer needs a running server; under `output: export` every image must
  // be served as-is.
  images: { unoptimized: true },

  // Markdown is rendered at build time from the repository's own files, so nothing is
  // fetched at runtime. No images, fonts or scripts come from a third-party origin either.
  reactStrictMode: true,
}

export default nextConfig