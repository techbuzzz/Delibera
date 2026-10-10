import type { MetadataRoute } from 'next'
import { siteOrigin } from '@/lib/paths'

// See app/sitemap.ts — required under `output: 'export'`.
export const dynamic = 'force-static'

/**
 * Deliberately permissive.
 *
 * The site's content is public documentation, and the previous behaviour was to let anyone
 * reach it. The one thing this does *not* do is advertise an API surface — the Nuxt Web UI
 * is deployed as a container next to `Delibera.Server`, never from this repository's Pages
 * site, and nothing here links to a live instance.
 *
 * `Sitemap` is absolute because a crawler cannot resolve a relative one against an origin it
 * has not been told about.
 */
export default function robots(): MetadataRoute.Robots {
  return {
    rules: [{ userAgent: '*', allow: '/' }],
    sitemap: siteOrigin('/sitemap.xml'),
  }
}