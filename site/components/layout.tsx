import Link from 'next/link'
import type { ReactNode } from 'react'
import { REPO_URL } from '@/lib/site'
import { brandImage } from '@/lib/paths'

const NAV = [
  { href: '/docs/', label: 'Docs' },
  { href: '/blog/', label: 'Releases' },
  { href: '/measurements/', label: 'Measurements' },
  { href: '/templates/', label: 'Templates' },
  { href: '/changelog/', label: 'Changelog' },
]

export function SiteHeader({ current }: { current?: string }) {
  return (
    <header className="site-header">
      <div className="wrap site-header__inner">
        <Link href="/" className="site-header__brand">
          {/* eslint-disable-next-line @next/next/no-img-element */}
          <img
            src={brandImage('delibera-horizontal-1920x480.png')}
            alt="Delibera"
            width={1920}
            height={480}
          />
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
      </div>
    </header>
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
            {index < items.length - 1 ? ' / ' : null}
          </span>
        ))}
      </nav>
    </div>
  )
}