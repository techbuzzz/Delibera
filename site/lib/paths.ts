/**
 * Base path helpers.
 *
 * `next.config.ts` sets `basePath` from `PAGES_BASE_PATH`, which `actions/configure-pages`
 * fills in as `/Delibera` on a Pages deploy and which is empty locally. Next.js applies that
 * prefix automatically to `<Link>`, `next/image` and its own chunk URLs — but **not** to a
 * plain `<img src="/img/...">` or to a string literal inside the metadata object, because
 * those bypass the router entirely.
 *
 * That asymmetry is the whole reason this module exists: the hand-written markup in
 * `app/page.tsx` and `app/layout.tsx` uses raw `<img>` tags, and every one of them needs the
 * prefix added by hand or the deployed site renders broken images.
 *
 * Keep the fallback in sync with `basePath` in next.config.ts — both read the same variable.
 */
const BASE_PATH = process.env.PAGES_BASE_PATH ?? ''

/** Prefix a root-relative site path with the deployment base path. */
export function withBasePath(path: string): string {
  if (!path.startsWith('/')) return path
  return `${BASE_PATH}${path}`
}

/** Public path of a repository brand image, ready for use in `src`/`href`. */
export function brandImage(fileName: string): string {
  return withBasePath(`/img/${fileName}`)
}

/**
 * Absolute origin the site is published at.
 *
 * Used for anything a crawler consumes — `sitemap.xml` entries and the `robots.txt`
 * `Sitemap:` line — where a relative URL is either invalid or unresolvable. Constructed from
 * the deployment origin plus `basePath`, so it follows the Pages configuration instead of
 * hardcoding `/Delibera` in a second place.
 */
export function siteOrigin(path: string): string {
  const origin = process.env.SITE_ORIGIN ?? 'https://techbuzzz.github.io'
  return `${origin}${withBasePath(path)}`
}