'use client'

import { useEffect, useRef, useState } from 'react'
import Link from 'next/link'

interface TimelineItem {
  version: string
  date: string | null
  slug: string
  hasNotes: boolean
  isUnreleased: boolean
}

interface ChangelogTimelineProps {
  items: TimelineItem[]
}

/**
 * Visual timeline for the changelog.
 *
 * Shows all releases as a vertical timeline with a progress line, click-to-scroll
 * navigation, and active state tracking via IntersectionObserver.
 */
export function ChangelogTimeline({ items }: ChangelogTimelineProps) {
  const [activeVersion, setActiveVersion] = useState<string>(items[0]?.version ?? '')
  const containerRef = useRef<HTMLDivElement>(null)

  useEffect(() => {
    const observer = new IntersectionObserver(
      (entries) => {
        const visible = entries
          .filter((entry) => entry.isIntersecting)
          .sort((a, b) => a.boundingClientRect.top - b.boundingClientRect.top)

        if (visible[0]) {
          setActiveVersion(visible[0].target.id.replace('v', ''))
        }
      },
      { rootMargin: '-100px 0px -60% 0px', threshold: 0 },
    )

    items.forEach((item) => {
      const element = document.getElementById(`v${item.slug}`)
      if (element) observer.observe(element)
    })

    return () => observer.disconnect()
  }, [items])

  const scrollTo = (slug: string) => {
    const element = document.getElementById(`v${slug}`)
    if (element) {
      element.scrollIntoView({ behavior: 'smooth', block: 'start' })
    }
  }

  return (
    <div className="wrap" style={{ paddingBottom: 32 }}>
      <div
        ref={containerRef}
        style={{
          display: 'flex',
          gap: 16,
          overflowX: 'auto',
          padding: '16px 0',
          scrollbarWidth: 'thin',
        }}
      >
        {items.map((item, index) => {
          const isActive = activeVersion === item.slug
          return (
            <button
              key={item.version}
              onClick={() => scrollTo(item.slug)}
              style={{
                display: 'flex',
                flexDirection: 'column',
                alignItems: 'center',
                gap: 8,
                padding: '12px 16px',
                border: '1px solid var(--border)',
                borderRadius: 'var(--radius)',
                background: isActive ? 'var(--accent-soft)' : 'var(--bg)',
                borderColor: isActive ? 'var(--accent)' : 'var(--border)',
                cursor: 'pointer',
                transition: 'all 0.2s ease',
                flex: 'none',
                minWidth: 100,
              }}
              onMouseEnter={(e) => {
                if (!isActive) {
                  e.currentTarget.style.borderColor = 'var(--accent)'
                  e.currentTarget.style.transform = 'translateY(-2px)'
                }
              }}
              onMouseLeave={(e) => {
                if (!isActive) {
                  e.currentTarget.style.borderColor = 'var(--border)'
                  e.currentTarget.style.transform = 'none'
                }
              }}
            >
              <div
                style={{
                  width: 12,
                  height: 12,
                  borderRadius: '50%',
                  background: item.isUnreleased
                    ? 'var(--warn-border)'
                    : isActive
                      ? 'var(--accent)'
                      : 'var(--border)',
                  boxShadow: isActive ? '0 0 12px var(--accent-glow)' : 'none',
                  transition: 'all 0.3s ease',
                }}
              />
              <div style={{ textAlign: 'center' }}>
                <div
                  style={{
                    fontWeight: 600,
                    fontSize: '0.875rem',
                    color: isActive ? 'var(--accent)' : 'var(--fg)',
                  }}
                >
                  {item.isUnreleased ? 'Unreleased' : `v${item.slug}`}
                </div>
                {item.date && (
                  <div style={{ fontSize: '0.7rem', color: 'var(--fg-dim)', marginTop: 2 }}>
                    {item.date}
                  </div>
                )}
                {item.hasNotes && !item.isUnreleased && (
                  <div style={{ fontSize: '0.65rem', color: 'var(--accent)', marginTop: 4 }}>
                    <Link
                      href={`/blog/${item.slug}/`}
                      onClick={(e) => e.stopPropagation()}
                      style={{ color: 'var(--accent)' }}
                    >
                      📝 notes
                    </Link>
                  </div>
                )}
              </div>
              {index < items.length - 1 && (
                <div
                  style={{
                    position: 'absolute',
                    right: -16,
                    top: '50%',
                    width: 16,
                    height: 2,
                    background: 'var(--border)',
                    display: 'none',
                  }}
                />
              )}
            </button>
          )
        })}
      </div>
    </div>
  )
}
