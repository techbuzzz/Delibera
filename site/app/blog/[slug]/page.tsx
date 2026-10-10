import { notFound } from 'next/navigation'
import type { Metadata } from 'next'
import { getVersion, listReleaseNotes, loadDoc } from '@/lib/content'
import { buildRouteIndex, releaseRoute } from '@/lib/routes'
import { renderMarkdown } from '@/lib/markdown'
import { Breadcrumbs, PageHead, PageShell } from '@/components/layout'
import { DocLayout } from '@/components/markdown'
import { githubBlob } from '@/lib/site'

export const dynamicParams = false

export async function generateStaticParams() {
  const notes = await listReleaseNotes()
  return notes.map((note) => ({ slug: note.slug }))
}

export async function generateMetadata({
  params,
}: {
  params: Promise<{ slug: string }>
}): Promise<Metadata> {
  const { slug } = await params
  const note = (await listReleaseNotes()).find((candidate) => candidate.slug === slug)

  if (!note) return { title: 'Not found' }

  return {
    title: note.title,
    description: note.description,
    alternates: { canonical: releaseRoute(note.slug) },
  }
}

export default async function ReleaseNotePage({ params }: { params: Promise<{ slug: string }> }) {
  const { slug } = await params
  const version = await getVersion()

  const note = (await listReleaseNotes()).find((candidate) => candidate.slug === slug)
  if (!note) notFound()

  const { markdown } = await loadDoc(note.source)
  const [routes] = await Promise.all([buildRouteIndex()])
  const { html, headings } = await renderMarkdown(markdown, {
    source: note.source,
    routes,
    stripPreamble: true,
  })

  return (
    <PageShell version={version} current="/blog/">
      {/* No visible description: the release note opens with that same sentence. The
          extracted description is still used for the page metadata above. */}
      <PageHead title={note.title} />
      <Breadcrumbs
        items={[
          { href: '/', label: 'Home' },
          { href: '/blog/', label: 'Releases' },
          { label: note.title },
        ]}
      />

      <DocLayout html={html} headings={headings} />

      <div className="wrap" style={{ paddingBottom: 48, fontSize: '0.875rem' }}>
        <p>
          Source:{' '}
          <a href={githubBlob(note.source)}>
            <code>{note.source}</code>
          </a>{' '}
          on GitHub.
        </p>
      </div>
    </PageShell>
  )
}