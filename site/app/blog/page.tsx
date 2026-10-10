import Link from 'next/link'
import type { Metadata } from 'next'
import { getVersion, getReleases, byVersionDescending, isReleased, listReleaseNotes } from '@/lib/content'
import { PageShell, PageHead } from '@/components/layout'
import { releaseRoute, normalisedReleaseSlug } from '@/lib/routes'

export const metadata: Metadata = {
  title: 'Release notes',
  description: 'What changed in every Delibera release, newest first.',
}

/**
 * Release-note index.
 *
 * Reads `docs/WhatsNew-*.md` directly rather than the changelog: these are the long-form
 * notes written for each release, and their descriptions come from the documents themselves.
 * The changelog has its own page.
 */
export default async function BlogIndexPage() {
  const version = await getVersion()
  const notes = await listReleaseNotes()
  const releases = (await getReleases()).sort(byVersionDescending)

  const latestReleased = releases.find(isReleased)

  return (
    <PageShell version={version} current="/blog/">
      <PageHead
        title="Release notes"
        description="Long-form notes for every published version. Newest first."
      />

      <section className="band">
        <div className="wrap">
          {latestReleased ? (
            <p className="band__hint">
              Latest published release: <strong>v{latestReleased.version}</strong>
              {latestReleased.date ? ` (${latestReleased.date})` : null}.
            </p>
          ) : null}

          <div className="post-list">
            {notes.map((note) => (
              <article key={note.source} className="post">
                <h3>
                  <Link href={releaseRoute(note.slug)}>{note.title}</Link>
                </h3>
                {note.description ? <p>{note.description}</p> : null}
                <div className="post-meta">
                  <span>v{normalisedReleaseSlug(note.slug)}</span>
                  {note.lang === 'ru' ? <span className="pill">RU</span> : null}
                </div>
              </article>
            ))}
          </div>
        </div>
      </section>
    </PageShell>
  )
}