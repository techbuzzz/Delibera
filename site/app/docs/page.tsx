import Link from 'next/link'
import type { Metadata } from 'next'
import { getVersion, listDocPages } from '@/lib/content'
import { PageShell, PageHead } from '@/components/layout'
import { Reveal } from '@/components/reveal'
import { docRoute } from '@/lib/routes'
import { DocsSearch } from '@/components/docs-search'

export const metadata: Metadata = {
  title: 'Documentation',
  description:
    'Guides for Delibera: quick start, server hosting, distributed debates, caching, the work log and the release history.',
}

/** Documents are grouped by the directory they live in; top-level files form their own group. */
interface Group {
  key: string
  title: string
  hint: string
  pages: Awaited<ReturnType<typeof listDocPages>>
}

const GROUP_META: Record<string, { title: string; hint: string }> = {
  guides: {
    title: 'Guides',
    hint: 'How to use Delibera: setup, hosting, distribution, caching and providers.',
  },
  tasks: {
    title: 'Work log',
    hint: 'Open items by severity, including what is deliberately not fixed. Updated as work lands.',
  },
  scope: {
    title: 'Scope',
    hint: 'Planning notes behind the stabilisation effort — hypotheses, and the breaking-change inventory.',
  },
}

export default async function DocsIndexPage() {
  const version = await getVersion()
  const pages = await listDocPages()

  const groups = new Map<string, Group>()
  for (const page of pages) {
    // Group by the *directory* the file lives in, not by its slug. `docs/TASKS/README.md`
    // collapses to the slug `tasks` (a directory with no path separator), and keying on the
    // slug would file the work-log index under "Guides" alongside the user documentation.
    const relative = page.source.replace(/^docs\//, '')
    const inDirectory = relative.includes('/')
    const key = inDirectory ? (relative.split('/')[0] ?? 'guides').toLowerCase() : 'guides'

    const group = groups.get(key) ?? {
      key,
      ...(GROUP_META[key] ?? { title: key, hint: '' }),
      pages: [],
    }
    group.pages.push(page)
    groups.set(key, group)
  }

  // Guides first, then the supporting material, so the index reads top-down by importance.
  const order = ['guides', 'tasks', 'scope']
  const ordered = [...groups.values()].sort(
    (a, b) => order.indexOf(a.key) - order.indexOf(b.key),
  )

  // Flatten for search
  const allPages = pages.map((page) => ({
    title: page.title,
    description: page.description,
    href: docRoute(page.slug),
    group: page.source.replace(/^docs\//, '').includes('/')
      ? (page.source.replace(/^docs\//, '').split('/')[0] ?? 'guides').toLowerCase()
      : 'guides',
  }))

  return (
    <PageShell version={version} current="/docs/">
      <PageHead
        title="Documentation"
        description="Everything under docs/, rendered on the site. Each page is the same Markdown that lives in the repository."
      />

      <DocsSearch pages={allPages} />

      <section className="band">
        <div className="wrap">
          {ordered.map((group, index) => {
            const english = group.pages.filter((page) => page.lang === 'en')
            const russian = group.pages.filter((page) => page.lang === 'ru')

            return (
              <div key={group.key} style={index === 0 ? undefined : { marginTop: 48 }} data-group={group.key}>
                <Reveal>
                  <h2 className="band__title">{group.title}</h2>
                  {group.hint ? <p className="band__hint">{group.hint}</p> : null}
                </Reveal>

                <div className="cards">
                  {english.map((page, pageIndex) => (
                    <Reveal key={page.source} delay={(pageIndex % 3 + 1) as 1 | 2 | 3}>
                      <Link href={docRoute(page.slug)} className="card">
                        <div className="t">{page.title}</div>
                        {page.description ? <div className="d">{page.description}</div> : null}
                      </Link>
                    </Reveal>
                  ))}
                </div>

                {russian.length > 0 ? (
                  <>
                    <h3 style={{ marginTop: 32, fontSize: '1rem', fontWeight: 600 }}>Русский</h3>
                    <div className="cards">
                      {russian.map((page, pageIndex) => (
                        <Reveal key={page.source} delay={(pageIndex % 3 + 1) as 1 | 2 | 3}>
                          <Link href={docRoute(page.slug)} className="card">
                            <div className="t">{page.title}</div>
                            {page.description ? <div className="d">{page.description}</div> : null}
                          </Link>
                        </Reveal>
                      ))}
                    </div>
                  </>
                ) : null}
              </div>
            )
          })}
        </div>
      </section>
    </PageShell>
  )
}
