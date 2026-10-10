import Link from 'next/link'
import { getVersion, getLatestDocumentedVersion, listReleaseNotes, listDocPages } from '@/lib/content'
import { PageShell } from '@/components/layout'
import {
  DOCKER_IMAGES,
  NUGET_PACKAGES,
  NUGET_URL,
  REPO_URL,
  RELEASES_URL,
} from '@/lib/site'
import { releaseRoute, docRoute } from '@/lib/routes'
import { brandImage } from '@/lib/paths'

/**
 * Landing page.
 *
 * Built from the same data the rest of the site uses: the version comes from the packable
 * csproj, the release cards from `docs/WhatsNew-*.md`, the documentation cards from the
 * documents themselves. Nothing here is a hand-maintained copy of the README, so the page
 * cannot drift away from what the repository actually contains.
 */
export default async function HomePage() {
  const version = await getVersion()
  const latestDocumented = await getLatestDocumentedVersion()
  const allNotes = await listReleaseNotes()

  // English notes only on the English home page. Taking the first three of a mixed list
  // means a Russian translation of an old release can displace the actual latest three.
  const notes = allNotes.filter((note) => note.lang === 'en').slice(0, 3)

  // Featured order is an editorial choice, but membership and links come from the real
  // document list — a renamed or deleted guide drops off the front page on its own instead
  // of leaving a card pointing at a 404.
  const featuredOrder = ['quickstart', 'server', 'distributed-debates', 'caching']
  const guides = await listDocPages()
  const featuredDocs = featuredOrder
    .map((slug) => guides.find((page) => page.slug === slug))
    .filter((page): page is NonNullable<typeof page> => page !== undefined)

  return (
    <PageShell version={version}>
      <div className="hero">
        <div className="wrap">
          {/* eslint-disable-next-line @next/next/no-img-element */}
          <img
            src={brandImage('delibera-horizontal-1920x480.png')}
            alt="Delibera"
            width={1920}
            height={480}
          />
          <h1>Thoughtful AI Decisions</h1>
          <p>
            Collective decision making through structured AI deliberation — multiple models
            reason through a question across rounds, critique each other&rsquo;s answers, and a{' '}
            <strong>Chairman</strong> weighs the arguments into a final verdict. RAG via
            Qdrant or PostgreSQL/pgvector, MCP tools, live SSE streaming.
          </p>
          <p style={{ marginTop: 18 }}>
            <span className="pill pill--accent">v{version}</span>{' '}
            <a className="pill" href={RELEASES_URL}>
              Release notes
            </a>{' '}
            <span className="pill">.NET 10</span>
            <span className="pill">MIT</span>
          </p>
          <div className="badges">
            <a href={NUGET_URL('Delibera.Core')}>
              {/* eslint-disable-next-line @next/next/no-img-element */}
              <img
                src="https://img.shields.io/nuget/v/Delibera.Core.svg"
                alt="Delibera.Core on NuGet"
                width={110}
                height={20}
              />
            </a>
            <a href={NUGET_URL('Delibera.Server')}>
              {/* eslint-disable-next-line @next/next/no-img-element */}
              <img
                src="https://img.shields.io/nuget/v/Delibera.Server.svg"
                alt="Delibera.Server on NuGet"
                width={118}
                height={20}
              />
            </a>
            <a href="https://img.shields.io/badge/Docker%20Hub-2496ED?logo=docker&logoColor=fff">
              {/* eslint-disable-next-line @next/next/no-img-element */}
              <img
                src="https://img.shields.io/badge/Docker%20Hub-2496ED?logo=docker&logoColor=fff"
                alt="Docker Hub"
                width={104}
                height={20}
              />
            </a>
          </div>
          <p style={{ marginTop: 16, fontSize: '0.9rem' }}>
            <a href="https://github.com/techbuzzz/Delibera/blob/main/README.md">English</a> ·{' '}
            <a href="https://github.com/techbuzzz/Delibera/blob/main/README-RU.md">Русский</a>
          </p>
        </div>
      </div>

      <section className="band">
        <div className="wrap">
          <h2 className="band__title">Install</h2>
          <p className="band__hint">
            Three NuGet packages and two Docker images, published on every release.
          </p>

          <h3>NuGet</h3>
          <pre>
            <code>{NUGET_PACKAGES.map((p) => `dotnet add package ${p.id}`).join('\n')}</code>
          </pre>

          <h3>Docker Hub — no build required</h3>
          <pre>
            <code>{`curl -O https://raw.githubusercontent.com/techbuzzz/Delibera/main/deploy/docker-compose.hub.yml
export DELIBERA_VERSION=${version}   # omit to track: latest
docker compose -f docker-compose.hub.yml up -d`}</code>
          </pre>

          <div className="callout">
            <p>
              <strong>The API is unauthenticated and unthrottled, and a debate spends real LLM
              credits.</strong> Every published port binds to <code>127.0.0.1</code>. Put an
              authenticating reverse proxy in front before exposing this stack to a network you
              do not control.
            </p>
          </div>
        </div>
      </section>

      <section className="band">
        <div className="wrap">
          <h2 className="band__title">What&rsquo;s new</h2>
          <p className="band__hint">
            Latest release: <Link href={releaseRoute(latestDocumented)}>v{latestDocumented}</Link>.
            Every number below was measured, not estimated — see{' '}
            <Link href="/measurements/">measured behaviour</Link>.
          </p>
          <div className="cards">
            {notes.map((note) => (
              <Link key={note.source} href={releaseRoute(note.slug)} className="card">
                <div className="t">{note.title}</div>
                <div className="d">{note.description}</div>
              </Link>
            ))}
          </div>
          <p style={{ marginTop: 16 }}>
            <Link href="/blog/">All release notes</Link> · <Link href="/changelog/">Changelog</Link>
          </p>
        </div>
      </section>

      <section className="band">
        <div className="wrap">
          <h2 className="band__title">Documentation</h2>
          <p className="band__hint">Start here, then go deeper.</p>
          <div className="cards">
            {featuredDocs.map((page) => (
              <Link key={page.source} href={docRoute(page.slug)} className="card">
                <div className="t">{page.title}</div>
                <div className="d">{page.description}</div>
              </Link>
            ))}
            <Link className="card" href="/templates/">
              <div className="t">Debate templates</div>
              <div className="d">Six built-in councils, parsed from the code that ships in the package.</div>
            </Link>
            <Link className="card" href="/measurements/">
              <div className="t">Measured behaviour</div>
              <div className="d">Latency, throughput and token costs with the harness that produced them.</div>
            </Link>
          </div>
          <p style={{ marginTop: 16 }}>
            <Link href="/docs/">All documentation</Link>
          </p>
        </div>
      </section>

      <section className="band">
        <div className="wrap">
          <h2 className="band__title">Get it</h2>
          <div className="cards">
            {NUGET_PACKAGES.map((pkg) => (
              <a key={pkg.id} className="card" href={NUGET_URL(pkg.id)}>
                <div className="t">{pkg.id}</div>
                <div className="d">{pkg.description}</div>
              </a>
            ))}
            {DOCKER_IMAGES.map((image) => (
              <a key={image.id} className="card" href={image.hub}>
                <div className="t">Docker Hub — {image.id}</div>
                <div className="d">{image.description}</div>
                <span className="meta">linux/amd64 + linux/arm64</span>
              </a>
            ))}
            <a className="card" href={REPO_URL}>
              <div className="t">Source</div>
              <div className="d">MIT licensed. Clone and build with a single solution.</div>
            </a>
          </div>
        </div>
      </section>
    </PageShell>
  )
}