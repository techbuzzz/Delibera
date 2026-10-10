import type { Metadata } from 'next'
import Link from 'next/link'
import {
  getVersion,
  getReleases,
  byVersionDescending,
  isReleased,
  listReleaseNotes,
} from '@/lib/content'
import { PageShell, PageHead } from '@/components/layout'
import { buildRouteIndex, releaseRoute, normalisedReleaseSlug, CHANGELOG_ROUTE } from '@/lib/routes'
import { renderMarkdown } from '@/lib/markdown'
import { githubBlob } from '@/lib/site'

export const metadata: Metadata = {
  title: 'Changelog',
  description: 'Complete change history for Delibera, from 10.1.0 onwards.',
}

/**
 * The changelog, split per release so each version gets an anchor instead of one
 * 23 KB wall of text.
 *
 * Read straight from `CHANGELOG.md` and rendered through the same pipeline as the guides, so
 * a `## [10.5.2]` section here is byte-identical to the one in the repository file.
 */
export default async function ChangelogPage() {
  const version = await getVersion()
  const releases = (await getReleases()).sort(byVersionDescending)
  const [routes] = await Promise.all([buildRouteIndex()])

  const rendered = await Promise.all(
    releases.map(async (release) => {
      const { html } = await renderMarkdown(release.body, {
        source: 'CHANGELOG.md',
        routes,
      })

      return { release, html }
    }),
  )

  const published = rendered.filter(({ release }) => isReleased(release)).length

  // Not every released version has a long-form note: the changelog goes back to 10.1.0,
  // while `docs/WhatsNew-*.md` starts at 10.2.6. Link only where a note actually exists —
  // otherwise the card is a 404 on a page that exists to be the complete record.
  const notedVersions = new Set(
    (await listReleaseNotes()).map((note) => normalisedReleaseSlug(note.slug)),
  )

  return (
    <PageShell version={version} current="/changelog/">
      <PageHead
        title="Changelog"
        description={`All notable changes to Delibera — ${published} published releases plus the unreleased section.`}
      />

      <div className="wrap doc-layout">
        <div className="prose">
          {rendered.map(({ release, html }) => (
            <section key={release.version} id={`v${release.version}`}>
              <h2 className="md-heading" id={`v${release.version}`}>
                v{release.version}
                {release.date ? (
                  <span style={{ color: 'var(--fg-dim)', fontWeight: 400, fontSize: '0.85rem' }}>
                    {' '}
                    — {release.date}
                  </span>
                ) : null}
              </h2>
              {notedVersions.has(release.version) ? (
                <p>
                  {/* `Link`, not `a`: only the router-aware component gets `basePath`
                      applied automatically. A raw anchor would point at /blog/... and 404
                      on the deployed site under /Delibera. */}
                  <Link href={releaseRoute(normalisedReleaseSlug(release.version))}>
                    Read the long-form notes for v{release.version}
                  </Link>
                </p>
              ) : null}
              <div dangerouslySetInnerHTML={{ __html: html }} />
            </section>
          ))}
        </div>

        <nav className="toc" aria-label="Releases">
          <h2>Versions</h2>
          <ol>
            {rendered.map(({ release }) => (
              <li key={release.version}>
                <a href={`#v${normalisedReleaseSlug(release.version)}`}>v{release.version}</a>
              </li>
            ))}
          </ol>
          <p style={{ marginTop: 16, fontSize: '0.85rem' }}>
            Source:{' '}
            <a href={githubBlob('CHANGELOG.md')}>
              <code>CHANGELOG.md</code>
            </a>
            . Route: <code>{CHANGELOG_ROUTE}</code>
          </p>
        </nav>
      </div>
    </PageShell>
  )
}