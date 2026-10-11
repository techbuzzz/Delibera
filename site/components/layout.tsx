'use client'

import Link from 'next/link'
import type { ReactNode } from 'react'
import { useState, useEffect, useCallback, useMemo } from 'react'
import { REPO_URL } from '@/lib/site'
import { brandImage } from '@/lib/paths'

const NAV = [
  { href: '/docs/', label: 'Docs' },
  { href: '/blog/', label: 'Releases' },
  { href: '/measurements/', label: 'Measurements' },
  { href: '/templates/', label: 'Templates' },
  { href: '/changelog/', label: 'Changelog' },
]

const ALL_PAGES = [
  { title: 'Home', description: 'Thoughtful AI Decisions — multi-model deliberation framework', href: '/' },
  { title: 'Documentation', description: 'Guides, quick start, server hosting, distributed debates', href: '/docs/' },
  { title: 'Release notes', description: 'What changed in every Delibera release', href: '/blog/' },
  { title: 'Measured behaviour', description: 'Latency, throughput and token costs, measured', href: '/measurements/' },
  { title: 'Debate templates', description: 'Ready-to-run councils parsed from source', href: '/templates/' },
  { title: 'Changelog', description: 'Complete change history', href: '/changelog/' },
]

function SunIcon() {
  return (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
      <circle cx="12" cy="12" r="5" />
      <line x1="12" y1="1" x2="12" y2="3" />
      <line x1="12" y1="21" x2="12" y2="23" />
      <line x1="4.22" y1="4.22" x2="5.64" y2="5.64" />
      <line x1="18.36" y1="18.36" x2="19.78" y2="19.78" />
      <line x1="1" y1="12" x2="3" y2="12" />
      <line x1="21" y1="12" x2="23" y2="12" />
      <line x1="4.22" y1="19.78" x2="5.64" y2="18.36" />
      <line x1="18.36" y1="5.64" x2="19.78" y2="4.22" />
    </svg>
  )
}

function MoonIcon() {
  return (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
      <path d="M21 12.79A9 9 0 1 1 11.21 3 7 7 0 0 0 21 12.79z" />
    </svg>
  )
}

function SearchIcon() {
  return (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
      <circle cx="11" cy="11" r="8" />
      <line x1="21" y1="21" x2="16.65" y2="16.65" />
    </svg>
  )
}

function MenuIcon() {
  return (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
      <line x1="3" y1="6" x2="21" y2="6" />
      <line x1="3" y1="12" x2="21" y2="12" />
      <line x1="3" y1="18" x2="21" y2="18" />
    </svg>
  )
}

function CloseIcon() {
  return (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
      <line x1="18" y1="6" x2="6" y2="18" />
      <line x1="6" y1="6" x2="18" y2="18" />
    </svg>
  )
}

function ArrowUpIcon() {
  return (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
      <line x1="12" y1="19" x2="12" y2="5" />
      <polyline points="5 12 12 5 19 12" />
    </svg>
  )
}

function GithubIcon() {
  return (
    <svg viewBox="0 0 24 24" fill="currentColor">
      <path d="M12 0c-6.626 0-12 5.373-12 12 0 5.302 3.438 9.8 8.207 11.387.599.111.793-.261.793-.577v-2.234c-3.338.726-4.033-1.416-4.033-1.416-.546-1.387-1.333-1.756-1.333-1.756-1.089-.745.083-.729.083-.729 1.205.084 1.839 1.237 1.839 1.237 1.07 1.834 2.807 1.304 3.492.997.107-.775.418-1.305.762-1.604-2.665-.305-5.467-1.334-5.467-5.931 0-1.311.469-2.381 1.236-3.221-.124-.303-.535-1.524.117-3.176 0 0 1.008-.322 3.301 1.23.957-.266 1.983-.399 3.003-.404 1.02.005 2.047.138 3.006.404 2.291-1.552 3.297-1.23 3.297-1.23.653 1.653.242 2.874.118 3.176.77.84 1.235 1.911 1.235 3.221 0 4.609-2.807 5.624-5.479 5.921.43.372.823 1.102.823 2.222v3.293c0 .319.192.694.801.576 4.765-1.589 8.199-6.086 8.199-11.386 0-6.627-5.373-12-12-12z"/>
    </svg>
  )
}

function ThemeToggle() {
  const [theme, setTheme] = useState<'light' | 'dark' | null>(null)
  const [mounted, setMounted] = useState(false)

  useEffect(() => {
    setMounted(true)
    const stored = localStorage.getItem('theme')
    const prefersDark = window.matchMedia('(prefers-color-scheme: dark)').matches
    const initial = stored ?? (prefersDark ? 'dark' : 'light')
    setTheme(initial as 'light' | 'dark')
    document.documentElement.setAttribute('data-theme', initial)
  }, [])

  const toggle = useCallback(() => {
    const next = theme === 'dark' ? 'light' : 'dark'
    setTheme(next)
    document.documentElement.setAttribute('data-theme', next)
    localStorage.setItem('theme', next)
  }, [theme])

  if (!mounted || theme === null) {
    return <div className="theme-toggle" style={{ opacity: 0 }} aria-hidden="true" />
  }

  return (
    <button
      className="theme-toggle"
      onClick={toggle}
      aria-label={`Switch to ${theme === 'dark' ? 'light' : 'dark'} theme`}
      title={`Switch to ${theme === 'dark' ? 'light' : 'dark'} theme`}
    >
      {theme === 'dark' ? <SunIcon /> : <MoonIcon />}
    </button>
  )
}

function SearchButton({ onClick }: { onClick: () => void }) {
  useEffect(() => {
    const handler = (e: KeyboardEvent) => {
      if ((e.metaKey || e.ctrlKey) && e.key === 'k') {
        e.preventDefault()
        onClick()
      }
    }
    document.addEventListener('keydown', handler)
    return () => document.removeEventListener('keydown', handler)
  }, [onClick])

  return (
    <button className="search-btn" onClick={onClick} aria-label="Search">
      <SearchIcon />
      <span>Search</span>
      <kbd>⌘K</kbd>
    </button>
  )
}

function SearchModal({ open, onClose }: { open: boolean; onClose: () => void }) {
  const [query, setQuery] = useState('')
  const [selected, setSelected] = useState(0)

  const results = useMemo(() => {
    if (!query.trim()) return ALL_PAGES.slice(0, 6)
    const q = query.toLowerCase()
    return ALL_PAGES.filter(
      (page) =>
        page.title.toLowerCase().includes(q) ||
        page.description.toLowerCase().includes(q)
    ).slice(0, 8)
  }, [query])

  useEffect(() => {
    if (open) {
      setQuery('')
      setSelected(0)
      document.body.style.overflow = 'hidden'
    } else {
      document.body.style.overflow = ''
    }
    return () => { document.body.style.overflow = '' }
  }, [open])

  useEffect(() => {
    if (!open) return
    const handler = (e: KeyboardEvent) => {
      if (e.key === 'Escape') onClose()
      if (e.key === 'ArrowDown') { e.preventDefault(); setSelected(s => Math.min(s + 1, results.length - 1)) }
      if (e.key === 'ArrowUp') { e.preventDefault(); setSelected(s => Math.max(s - 1, 0)) }
      if (e.key === 'Enter' && results[selected]) {
        window.location.href = results[selected].href
        onClose()
      }
    }
    document.addEventListener('keydown', handler)
    return () => document.removeEventListener('keydown', handler)
  }, [open, results, selected, onClose])

  if (!open) return null

  return (
    <div className="search-overlay" onClick={onClose}>
      <div className="search-modal" onClick={e => e.stopPropagation()} role="dialog" aria-modal="true" aria-label="Search">
        <div className="search-input-wrapper">
          <SearchIcon />
          <input
            type="text"
            className="search-input"
            placeholder="Search documentation..."
            value={query}
            onChange={e => { setQuery(e.target.value); setSelected(0) }}
            autoFocus
          />
          <kbd>ESC</kbd>
        </div>
        <div className="search-results">
          {results.length === 0 ? (
            <div className="search-empty">No results found for &ldquo;{query}&rdquo;</div>
          ) : (
            results.map((page, index) => (
              <Link
                key={page.href}
                href={page.href}
                className={`search-result ${index === selected ? 'selected' : ''}`}
                onClick={onClose}
                onMouseEnter={() => setSelected(index)}
              >
                <div className="search-result__title">{page.title}</div>
                <div className="search-result__desc">{page.description}</div>
                <div className="search-result__path">{page.href}</div>
              </Link>
            ))
          )}
        </div>
        <div className="search-hint">
          <span><kbd>↑↓</kbd> Navigate</span>
          <span><kbd>↵</kbd> Select</span>
          <span><kbd>ESC</kbd> Close</span>
        </div>
      </div>
    </div>
  )
}

function MobileNav({ open, onClose, current }: { open: boolean; onClose: () => void; current?: string }) {
  useEffect(() => {
    if (open) {
      document.body.style.overflow = 'hidden'
    } else {
      document.body.style.overflow = ''
    }
    return () => { document.body.style.overflow = '' }
  }, [open])

  return (
    <nav className={`mobile-nav ${open ? 'open' : ''}`} aria-label="Mobile">
      {NAV.map((item) => (
        <Link
          key={item.href}
          href={item.href}
          aria-current={current === item.href ? 'page' : undefined}
          onClick={onClose}
        >
          {item.label}
        </Link>
      ))}
      <div className="mobile-nav__footer">
        <a href={REPO_URL} target="_blank" rel="noopener noreferrer" className="pill">
          <GithubIcon /> GitHub
        </a>
        <a href="https://www.nuget.org/packages/Delibera.Core" target="_blank" rel="noopener noreferrer" className="pill">
          NuGet
        </a>
      </div>
    </nav>
  )
}

function BackToTop() {
  const [visible, setVisible] = useState(false)

  useEffect(() => {
    const handler = () => setVisible(window.scrollY > 400)
    window.addEventListener('scroll', handler, { passive: true })
    return () => window.removeEventListener('scroll', handler)
  }, [])

  const scrollToTop = useCallback(() => {
    window.scrollTo({ top: 0, behavior: 'smooth' })
  }, [])

  return (
    <button
      className={`back-to-top ${visible ? 'visible' : ''}`}
      onClick={scrollToTop}
      aria-label="Back to top"
      title="Back to top"
    >
      <ArrowUpIcon />
    </button>
  )
}

function ReadingProgress() {
  const [progress, setProgress] = useState(0)

  useEffect(() => {
    const handler = () => {
      const scrollTop = window.scrollY
      const docHeight = document.documentElement.scrollHeight - window.innerHeight
      setProgress(docHeight > 0 ? (scrollTop / docHeight) * 100 : 0)
    }
    window.addEventListener('scroll', handler, { passive: true })
    handler()
    return () => window.removeEventListener('scroll', handler)
  }, [])

  return <div className="reading-progress" style={{ transform: `scaleX(${progress / 100})` }} />
}

function HeaderScrollEffect() {
  useEffect(() => {
    const header = document.querySelector('.site-header')
    if (!header) return

    const handler = () => {
      if (window.scrollY > 10) {
        header.classList.add('scrolled')
      } else {
        header.classList.remove('scrolled')
      }
    }

    window.addEventListener('scroll', handler, { passive: true })
    return () => window.removeEventListener('scroll', handler)
  }, [])

  return null
}

export function SiteHeader({ current }: { current?: string }) {
  const [searchOpen, setSearchOpen] = useState(false)
  const [mobileOpen, setMobileOpen] = useState(false)

  return (
    <>
      <HeaderScrollEffect />
      <ReadingProgress />
      <header className="site-header">
        <div className="wrap site-header__inner">
          <Link href="/" className="site-header__brand">
            <span className="site-header__logo">
              {/* eslint-disable-next-line @next/next/no-img-element */}
              <img
                src={brandImage('delibera-square-1024x1024-clear.png')}
                alt="Delibera"
                width={535}
                height={412}
              />
            </span>
          </Link>
          <nav className="site-nav" aria-label="Main">
            {NAV.map((item) => (
              <Link
                key={item.href}
                href={item.href}
                aria-current={current === item.href ? 'page' : undefined}
              >
                {item.label}
              </Link>
            ))}
          </nav>
          <div className="site-header__actions">
            <SearchButton onClick={() => setSearchOpen(true)} />
            <ThemeToggle />
            <button
              className="mobile-menu-btn"
              onClick={() => setMobileOpen(!mobileOpen)}
              aria-label={mobileOpen ? 'Close menu' : 'Open menu'}
              aria-expanded={mobileOpen}
            >
              {mobileOpen ? <CloseIcon /> : <MenuIcon />}
            </button>
          </div>
        </div>
      </header>
      <MobileNav open={mobileOpen} onClose={() => setMobileOpen(false)} current={current} />
      <SearchModal open={searchOpen} onClose={() => setSearchOpen(false)} />
      <BackToTop />
    </>
  )
}

export function SiteFooter({ version }: { version: string }) {
  return (
    <footer className="site-footer">
      <div className="wrap site-footer__inner">
        <div>
          Delibera {version} · MIT · <Link href={REPO_URL}>Repository</Link> ·{' '}
          <Link href={`${REPO_URL}/issues`}>Issues</Link>
        </div>
        <nav aria-label="Footer">
          <Link href="/docs/">Documentation</Link>
          <Link href="/blog/">Release notes</Link>
          <a href="https://www.nuget.org/packages/Delibera.Core">NuGet</a>
          <a href="https://hub.docker.com/r/techbuzzz/delibera-server">Docker Hub</a>
        </nav>
      </div>
    </footer>
  )
}

export interface PageShellProps {
  children: ReactNode
  version: string
  current?: string
}

export function PageShell({ children, version, current }: PageShellProps) {
  return (
    <>
      <SiteHeader current={current} />
      <main id="main">{children}</main>
      <SiteFooter version={version} />
    </>
  )
}

export function PageHead({ title, description }: { title: string; description?: string }) {
  return (
    <div className="page-head">
      <div className="wrap">
        <h1>{title}</h1>
        {description ? <p>{description}</p> : null}
      </div>
    </div>
  )
}

export function Breadcrumbs({ items }: { items: Array<{ href?: string; label: string }> }) {
  return (
    <div className="wrap">
      <nav className="breadcrumbs" aria-label="Breadcrumb">
        {items.map((item, index) => (
          <span key={`${item.label}-${index}`}>
            {item.href ? <Link href={item.href}>{item.label}</Link> : item.label}
          </span>
        ))}
      </nav>
    </div>
  )
}
