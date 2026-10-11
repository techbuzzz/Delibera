'use client'

import { useEffect, useState } from 'react'
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
 * On-page table of contents with scroll-spy.
 *
 * Built from the headings captured during rendering, so an entry can never point at an
 * anchor that does not exist. Restricted to `h2` and `h3`. An IntersectionObserver tracks
 * which heading is currently in view and highlights it.
 */
export function TableOfContents({ headings }: { headings: RenderResult['headings'] }) {
  const [activeId, setActiveId] = useState<string>('')
  const items = headings.filter((heading) => heading.depth === 2 || heading.depth === 3)

  useEffect(() => {
    if (items.length < 2) return

    const observer = new IntersectionObserver(
      (entries) => {
        // Find the topmost visible heading
        const visible = entries
          .filter((entry) => entry.isIntersecting)
          .sort((a, b) => a.boundingClientRect.top - b.boundingClientRect.top)

        if (visible[0]) {
          setActiveId(visible[0].target.id)
        }
      },
      {
        rootMargin: '-80px 0px -80% 0px',
        threshold: 0,
      },
    )

    items.forEach((heading) => {
      const element = document.getElementById(heading.id)
      if (element) observer.observe(element)
    })

    return () => observer.disconnect()
  }, [items])

  if (items.length < 2) return null

  return (
    <nav className="toc" aria-label="On this page">
      <h2>On this page</h2>
      <ol>
        {items.map((heading) => (
          <li
            key={heading.id}
            data-depth={heading.depth}
            className={activeId === heading.id ? 'active' : ''}
          >
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
