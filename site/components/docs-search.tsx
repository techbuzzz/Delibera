'use client'

import { useState, useMemo, useCallback } from 'react'
import Link from 'next/link'

interface SearchablePage {
  title: string
  description: string
  href: string
  group: string
}

interface DocsSearchProps {
  pages: SearchablePage[]
}

export function DocsSearch({ pages }: DocsSearchProps) {
  const [query, setQuery] = useState('')

  const results = useMemo(() => {
    if (!query.trim()) return []
    const q = query.toLowerCase()
    return pages
      .filter(
        (page) =>
          page.title.toLowerCase().includes(q) ||
          page.description.toLowerCase().includes(q)
      )
      .slice(0, 10)
  }, [query, pages])

  const clear = useCallback(() => setQuery(''), [])

  // Group filter counts
  const groupCounts = useMemo(() => {
    const counts: Record<string, number> = {}
    for (const page of pages) {
      counts[page.group] = (counts[page.group] ?? 0) + 1
    }
    return counts
  }, [pages])

  return (
    <div className="wrap" style={{ paddingTop: 32, paddingBottom: 0 }}>
      <div style={{ position: 'relative', maxWidth: 560 }}>
        <div
          style={{
            display: 'flex',
            alignItems: 'center',
            gap: 12,
            padding: '12px 16px',
            border: '1px solid var(--border)',
            borderRadius: 'var(--radius-lg)',
            background: 'var(--bg)',
            boxShadow: 'var(--shadow-sm)',
            transition: 'all 0.2s ease',
          }}
        >
          <svg
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            strokeWidth="2"
            strokeLinecap="round"
            strokeLinejoin="round"
            style={{ width: 20, height: 20, color: 'var(--fg-dim)', flex: 'none' }}
          >
            <circle cx="11" cy="11" r="8" />
            <line x1="21" y1="21" x2="16.65" y2="16.65" />
          </svg>
          <input
            type="text"
            placeholder="Filter documentation..."
            value={query}
            onChange={(e) => setQuery(e.target.value)}
            style={{
              flex: 1,
              border: 'none',
              outline: 'none',
              background: 'transparent',
              color: 'var(--fg)',
              fontSize: '1rem',
              fontFamily: 'var(--sans)',
            }}
          />
          {query && (
            <button
              onClick={clear}
              aria-label="Clear search"
              style={{
                border: 'none',
                background: 'var(--bg-soft)',
                color: 'var(--fg-dim)',
                cursor: 'pointer',
                padding: '4px 8px',
                borderRadius: 6,
                fontSize: '0.75rem',
              }}
            >
              Clear
            </button>
          )}
        </div>

        {query && (
          <div
            style={{
              position: 'absolute',
              top: '100%',
              left: 0,
              right: 0,
              marginTop: 8,
              background: 'var(--bg)',
              border: '1px solid var(--border)',
              borderRadius: 'var(--radius-lg)',
              boxShadow: 'var(--shadow-lg)',
              overflow: 'hidden',
              zIndex: 30,
            }}
          >
            {results.length === 0 ? (
              <div style={{ padding: 24, textAlign: 'center', color: 'var(--fg-dim)' }}>
                No documents match &ldquo;{query}&rdquo;
              </div>
            ) : (
              <div style={{ maxHeight: 320, overflowY: 'auto', padding: 8 }}>
                {results.map((page) => (
                  <Link
                    key={page.href}
                    href={page.href}
                    style={{
                      display: 'block',
                      padding: '12px 16px',
                      borderRadius: 8,
                      color: 'var(--fg)',
                      textDecoration: 'none',
                      transition: 'background 0.15s ease',
                    }}
                    onMouseEnter={(e) => {
                      e.currentTarget.style.background = 'var(--bg-soft)'
                    }}
                    onMouseLeave={(e) => {
                      e.currentTarget.style.background = 'transparent'
                    }}
                  >
                    <div style={{ fontWeight: 600, marginBottom: 4 }}>{page.title}</div>
                    <div style={{ fontSize: '0.875rem', color: 'var(--fg-dim)', lineHeight: 1.5 }}>
                      {page.description}
                    </div>
                    <div style={{ fontSize: '0.75rem', color: 'var(--fg-dim)', fontFamily: 'var(--mono)', marginTop: 4 }}>
                      {page.href}
                    </div>
                  </Link>
                ))}
              </div>
            )}
          </div>
        )}
      </div>

      {!query && (
        <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap', marginTop: 16 }}>
          {Object.entries(groupCounts).map(([group, count]) => (
            <span key={group} className="pill">
              {group}: {count}
            </span>
          ))}
          <span className="pill pill--accent">{pages.length} documents total</span>
        </div>
      )}
    </div>
  )
}
