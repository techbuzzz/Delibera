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
      <div className="hero">
        <div className="wrap">
          <h1>404</h1>
          <p>
            That page is not here. It may have moved when the documentation moved from GitHub
            onto this site.
          </p>
          <p style={{ marginTop: 20 }}>
            <Link className="pill pill--accent" href="/">
              Home
            </Link>{' '}
            <Link className="pill" href="/docs/">
              Docs
            </Link>{' '}
            <Link className="pill" href="/blog/">
              Releases
            </Link>{' '}
            <Link className="pill" href="/measurements/">
              Measurements
            </Link>{' '}
            <Link className="pill" href="/changelog/">
              Changelog
            </Link>
          </p>
        </div>
      </div>
    </PageShell>
  )
}