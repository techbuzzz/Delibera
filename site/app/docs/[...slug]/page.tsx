import { notFound } from 'next/navigation'
import type { Metadata } from 'next'
import { getVersion, listDocPages, loadDoc } from '@/lib/content'
import { buildRouteIndex } from '@/lib/routes'
import { renderMarkdown } from '@/lib/markdown'
import { Breadcrumbs, PageHead, PageShell } from '@/components/layout'
import { DocLayout } from '@/components/markdown'
import { githubBlob } from '@/lib/site'

/**
 * One page per Markdown file under `docs/`, at any depth.
 *
 * The catch-all `[...slug]` is what lets `docs/TASKS/W1-correctness.md` publish to
 * `/docs/tasks/w1-correctness/` and `docs/TASKS/README.md` publish to `/docs/tasks/`.
 * A single-segment `[slug]` route cannot express that, and the work log was previously
 * reachable from the landing page.
 *
 * `dynamicParams = false` is deliberate: under `output: 'export'` an unlisted parameter would
 * have no server to render it on request, and Next.js fails the build rather than emitting a
 * page that 404s. The document list is therefore the contract — a new file under `docs/` must
 * be published to appear on the site, which is exactly the guarantee that stops a link from
 * silently pointing at something that was never exported.
 */
export const dynamicParams = false

export async function generateStaticParams() {
  const pages = await listDocPages()
  return pages.map((page) => ({ slug: page.slug.split('/') }))
}

export async function generateMetadata({
  params,
}: {
  params: Promise<{ slug: string[] }>
}): Promise<Metadata> {
  const { slug } = await params
  const wanted = slug.join('/')
  const page = (await listDocPages()).find((candidate) => candidate.slug === wanted)

  if (!page) return { title: 'Not found' }

  return {
    title: page.title,
    description: page.description,
    alternates: { canonical: `/docs/${page.slug}/` },
  }
}

export default async function DocPage({ params }: { params: Promise<{ slug: string[] }> }) {
  const { slug } = await params
  const wanted = slug.join('/')
  const version = await getVersion()

  const pages = await listDocPages()
  const page = pages.find((candidate) => candidate.slug === wanted)

  if (!page) notFound()

  const { markdown } = await loadDoc(page.source)
  const [routes] = await Promise.all([buildRouteIndex()])
  const { html, headings } = await renderMarkdown(markdown, {
    source: page.source,
    routes,
    stripPreamble: true,
  })

  // Breadcrumbs mirror the directory: `/docs/tasks/w1-correctness/` yields
  // Home / Docs / Work log / W1 — Correctness.
  //
  // The group label is plain text, not a link to `/docs/<group>/`. Only `docs/TASKS/` has an
  // index file; `docs/scope/` has none, so a link there would be a 404 on a breadcrumb.
  const segments = page.slug.split('/')
  const crumbs: Array<{ href?: string; label: string }> = [
    { href: '/', label: 'Home' },
    { href: '/docs/', label: 'Docs' },
    ...(segments.length > 1 ? [{ label: sectionLabel(segments[0] ?? '') }] : []),
    { label: page.title },
  ]

  return (
    <PageShell version={version} current="/docs/">
      {/* No visible description: the document opens with that same sentence. The extracted
          description is still used for the page metadata above. */}
      <PageHead title={page.title} />
      <Breadcrumbs items={crumbs} />

      <DocLayout html={html} headings={headings} />

      <div className="wrap" style={{ paddingBottom: 48, fontSize: '0.875rem' }}>
        <p>
          Source:{' '}
          <a href={githubBlob(page.source)}>
            <code>{page.source}</code>
          </a>{' '}
          on GitHub — edit it there and this page updates on the next deploy.
        </p>
      </div>
    </PageShell>
  )
}

/** Directory segment -> the heading the docs index uses for that group. */
function sectionLabel(segment: string): string {
  const labels: Record<string, string> = {
    tasks: 'Work log',
    scope: 'Scope',
  }

  return labels[segment] ?? segment
}