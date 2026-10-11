import Link from 'next/link'
import { getVersion } from '@/lib/content'
import { PageShell } from '@/components/layout'

/**
 * 404 page.
 *
 * Under `output: 'export'` Next.js emits this as a static `404.html`, which GitHub Pages
 * serves for any unmatched path. It links back to the sections most likely to be what a
 * visitor was reaching for — a renamed deep link is the common case.
 */
export default async function NotFoundPage() {
  const version = await getVersion()

  return (
    <PageShell version={version}>
      <div className="hero" style={{ padding: '120px 0 80px' }}>
        <div className="wrap">
          <div style={{
            fontSize: '8rem',
            fontWeight: 800,
            lineHeight: 1,
            background: 'var(--gradient-hero)',
            WebkitBackgroundClip: 'text',
            WebkitTextFillColor: 'transparent',
            backgroundClip: 'text',
            marginBottom: 16,
          }}>
            404
          </div>
          <h1 style={{ fontSize: 'clamp(1.5rem, 3vw, 2rem)', marginBottom: 16 }}>
            Page not found
          </h1>
          <p style={{ fontSize: '1.1rem', maxWidth: 480, margin: '0 auto' }}>
            That page is not here. It may have moved when the documentation moved from GitHub
            onto this site.
          </p>
          <div className="hero-actions" style={{ marginTop: 32 }}>
            <Link className="btn btn-primary" href="/">
              ← Back to Home
            </Link>
            <Link className="btn btn-secondary" href="/docs/">
              Browse Documentation
            </Link>
          </div>
          <div className="hero-tags" style={{ marginTop: 32 }}>
            <Link className="pill" href="/blog/">Releases</Link>
            <Link className="pill" href="/measurements/">Measurements</Link>
            <Link className="pill" href="/templates/">Templates</Link>
            <Link className="pill" href="/changelog/">Changelog</Link>
          </div>
        </div>
      </div>
    </PageShell>
  )
}
