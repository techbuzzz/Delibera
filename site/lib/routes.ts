import { listDocPages, listReleaseNotes } from './content'
import type { RouteIndex } from './markdown'

/**
 * The single mapping from "a file in this repository" to "a page on this site".
 *
 * Markdown in `docs/` links between documents by repository path. Rendering those links as
 * written would produce `/docs/QuickStart.md`, which 404s on a static host. Both the link
 * rewriter and the pages themselves derive their routes from this one function, so a
 * document can never be published at one URL and linked from another.
 */

/** Route for a guide in `docs/`, e.g. `docs/QuickStart.md` -> `/docs/quickstart/`. */
export function docRoute(slug: string): string {
  return `/docs/${slug}/`
}

/**
 * Route for a release note.
 *
 * Keyed on the document slug, which is the version with any `-RU` suffix preserved:
 * `WhatsNew-v10.5.2.md` -> `/blog/10.5.2/`, `WhatsNew-v10.2.6-RU.md` ->
 * `/blog/10.2.6-ru/`. Keying on the bare version instead would collapse the Russian
 * translations onto the English pages, and one of the two would silently overwrite the
 * other at export time.
 */
export function releaseRoute(slug: string): string {
  return `/blog/${slug}/`
}

/** Normalised slug for linking a version number to whichever translation exists. */
export function normalisedReleaseSlug(slug: string): string {
  return slug.replace(/-ru$/i, '')
}

/**
 * Trailing slashes are part of every route string.
 *
 * `next.config.ts` sets `trailingSlash: true`, so `out/blog/10.5.2/index.html` is what the
 * Pages CDN serves. A link written as `/blog/10.5.2` would still redirect, but a file copied
 * out of the export and opened from disk would not.
 */
export const MEASUREMENTS_ROUTE = '/measurements/'
export const CHANGELOG_ROUTE = '/changelog/'
export const TEMPLATES_ROUTE = '/templates/'

/**
 * Build the repo-path -> route index used when rewriting Markdown links.
 *
 * Release notes are registered under both their source path and their bare version, because
 * documents reference them either way (`WhatsNew-v10.5.2.md` from the changelog, `v10.5.2`
 * in prose). Whichever form appears in the source resolves to the same page.
 */
export async function buildRouteIndex(): Promise<RouteIndex> {
  const routes: RouteIndex = new Map()

  for (const page of await listDocPages()) {
    routes.set(page.source, docRoute(page.slug))
  }

  for (const note of await listReleaseNotes()) {
    const route = releaseRoute(note.slug)
    routes.set(note.source, route)

    // `WhatsNew-v10.5.2.md` is also written as `v10.5.2` in prose (README, changelog), so
    // register that spelling too. Translations register under their own slug as well, which
    // is why the source path — not the bare version — is the primary key above.
    routes.set(`v${normalisedReleaseSlug(note.slug)}`, route)
  }

  routes.set('docs/performance-measurements.md', MEASUREMENTS_ROUTE)
  routes.set('performance-measurements.md', MEASUREMENTS_ROUTE)
  routes.set('CHANGELOG.md', CHANGELOG_ROUTE)

  return routes
}

/** Every static path the export must emit, for `generateStaticParams`. */
export async function listStaticDocPaths(): Promise<string[]> {
  const guides = await listDocPages()
  const notes = await listReleaseNotes()

  return [
    ...guides.map((page) => docRoute(page.slug)),
    ...notes.map((note) => releaseRoute(note.slug)),
  ]
}