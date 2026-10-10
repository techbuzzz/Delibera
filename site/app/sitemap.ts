import type { MetadataRoute } from 'next'
import { listDocPages, listReleaseNotes } from '@/lib/content'
import { docRoute, releaseRoute, MEASUREMENTS_ROUTE, TEMPLATES_ROUTE, CHANGELOG_ROUTE } from '@/lib/routes'
import { siteOrigin } from '@/lib/paths'

// `output: 'export'` has no server to revalidate anything, so every route must declare
// itself static or the build fails while collecting page data.
export const dynamic = 'force-static'

/**
 * Sitemap for the static export.
 *
 * Next.js emits `sitemap.xml` during `next build` under `output: 'export'`. Entries are
 * absolute: the sitemap protocol requires a full URL, and a bare `/docs/quickstart/` tells a
 * crawler nothing about the origin. The deployment prefix has to be part of that URL too —
 * `siteOrigin()` carries the `/Delibera` base path, so a route is never published under the
 * wrong root.
 *
 * Pages are listed without `lastModified`: `git log` is not reliably available in the Pages
 * checkout, and a timestamp derived from the checkout time would be uniformly wrong.
 */
export default async function sitemap(): Promise<MetadataRoute.Sitemap> {
  const docs = await listDocPages()
  const notes = await listReleaseNotes()

  const staticRoutes = ['/', '/docs/', '/blog/', MEASUREMENTS_ROUTE, TEMPLATES_ROUTE, CHANGELOG_ROUTE]

  return [
    ...staticRoutes.map((route) => ({ url: siteOrigin(route) })),
    ...docs.map((page) => ({ url: siteOrigin(docRoute(page.slug)) })),
    ...notes.map((note) => ({ url: siteOrigin(releaseRoute(note.slug)) })),
  ]
}