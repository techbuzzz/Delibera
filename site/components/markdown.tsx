import type { RenderResult } from '@/lib/markdown'

/**
 * Rendered Markdown document plus its table of contents.
 *
 * The HTML is inserted with `dangerouslySetInnerHTML` from repository-owned Markdown that
 * this build already parsed. There is no runtime input path here: `output: 'export'` means
 * these strings are baked into static files at build time.
 */
export function MarkdownBody({ html }: { html: string }) {
  return <div className="prose" dangerouslySetInnerHTML={{ __html: html }} />
}

/**
 * On-page table of contents.
 *
 * Built from the headings captured during rendering, so an entry can never point at an
 * anchor that does not exist. Restricted to `h2` and `h3`: `h4` and deeper make the list a
 * wall rather than a wayfinding aid, and `h1` is the document title, which the page header
 * already shows.
 */
export function TableOfContents({ headings }: { headings: RenderResult['headings'] }) {
  const items = headings.filter((heading) => heading.depth === 2 || heading.depth === 3)

  if (items.length < 2) return null

  return (
    <nav className="toc" aria-label="On this page">
      <h2>On this page</h2>
      <ol>
        {items.map((heading) => (
          <li key={heading.id} data-depth={heading.depth}>
            <a href={`#${heading.id}`}>{heading.text}</a>
          </li>
        ))}
      </ol>
    </nav>
  )
}

export function DocLayout({
  html,
  headings,
}: {
  html: string
  headings: RenderResult['headings']
}) {
  return (
    <div className="wrap doc-layout">
      <MarkdownBody html={html} />
      <TableOfContents headings={headings} />
    </div>
  )
}