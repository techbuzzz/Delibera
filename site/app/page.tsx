import Link from 'next/link'
import { getVersion, getLatestDocumentedVersion, listReleaseNotes, listDocPages } from '@/lib/content'
import { PageShell } from '@/components/layout'
import { Reveal } from '@/components/reveal'
import {
  DOCKER_IMAGES,
  NUGET_PACKAGES,
  NUGET_URL,
  REPO_URL,
  RELEASES_URL,
} from '@/lib/site'
import { releaseRoute, docRoute } from '@/lib/routes'
import { brandImage } from '@/lib/paths'

function GithubIcon() {
  return (
    <svg viewBox="0 0 24 24" fill="currentColor" width="18" height="18">
      <path d="M12 0c-6.626 0-12 5.373-12 12 0 5.302 3.438 9.8 8.207 11.387.599.111.793-.261.793-.577v-2.234c-3.338.726-4.033-1.416-4.033-1.416-.546-1.387-1.333-1.756-1.333-1.756-1.089-.745.083-.729.083-.729 1.205.084 1.839 1.237 1.839 1.237 1.07 1.834 2.807 1.304 3.492.997.107-.775.418-1.305.762-1.604-2.665-.305-5.467-1.334-5.467-5.931 0-1.311.469-2.381 1.236-3.221-.124-.303-.535-1.524.117-3.176 0 0 1.008-.322 3.301 1.23.957-.266 1.983-.399 3.003-.404 1.02.005 2.047.138 3.006.404 2.291-1.552 3.297-1.23 3.297-1.23.653 1.653.242 2.874.118 3.176.77.84 1.235 1.911 1.235 3.221 0 4.609-2.807 5.624-5.479 5.921.43.372.823 1.102.823 2.222v3.293c0 .319.192.694.801.576 4.765-1.589 8.199-6.086 8.199-11.386 0-6.627-5.373-12-12-12z"/>
    </svg>
  )
}

function ArrowRightIcon() {
  return (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" width="18" height="18">
      <line x1="5" y1="12" x2="19" y2="12" />
      <polyline points="12 5 19 12 12 19" />
    </svg>
  )
}

function BookIcon() {
  return (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" width="18" height="18">
      <path d="M4 19.5A2.5 2.5 0 0 1 6.5 17H20" />
      <path d="M6.5 2H20v20H6.5A2.5 2.5 0 0 1 4 19.5v-15A2.5 2.5 0 0 1 6.5 2z" />
    </svg>
  )
}

const FEATURES = [
  {
    icon: '⚖️',
    title: 'Multi-Model Councils',
    desc: 'Orchestrate any number of LLM participants across structured debate rounds, with critique and refinement.',
  },
  {
    icon: '👑',
    title: 'Chairman Synthesis',
    desc: 'A dedicated moderator opens, regulates, and synthesises the final balanced verdict from all arguments.',
  },
  {
    icon: '📚',
    title: 'Knowledge Keeper',
    desc: 'Per-round semantic retrieval with RAG — Qdrant or PostgreSQL/pgvector, with structured cited responses.',
  },
  {
    icon: '🛠️',
    title: 'MCP Tools',
    desc: 'An Operator micro-agent that delegates tasks to MCP servers on demand during the debate.',
  },
  {
    icon: '🗜️',
    title: 'Context Compression',
    desc: '4 strategies applied to every round — measured at 11.7–12.1% prompt-token savings.',
  },
  {
    icon: '⚡',
    title: 'Production Ready',
    desc: '.NET 10, ASP.NET Core server with REST + SSE streaming, gRPC transport, Redis distribution.',
  },
]

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

          <div className="hero-actions">
            <Link href="/docs/quickstart/" className="btn btn-primary">
              <BookIcon />
              Get Started
            </Link>
            <a href={REPO_URL} className="btn btn-secondary" target="_blank" rel="noopener noreferrer">
              <GithubIcon />
              View on GitHub
            </a>
          </div>

          <div className="hero-tags">
            <span className="pill pill--accent">v{version}</span>
            <a className="pill" href={RELEASES_URL}>Release notes</a>
            <span className="pill">.NET 10</span>
            <span className="pill">C# 15</span>
            <span className="pill">MIT</span>
          </div>

          <div className="badges">
            <a href={NUGET_URL('Delibera.Core')}>
              {/* eslint-disable-next-line @next/next/no-img-element */}
              <img
                src="https://img.shields.io/nuget/v/Delibera.Core.svg"
                alt="Delibera.Core on NuGet"
                width={110}
                height={22}
              />
            </a>
            <a href={NUGET_URL('Delibera.Server')}>
              {/* eslint-disable-next-line @next/next/no-img-element */}
              <img
                src="https://img.shields.io/nuget/v/Delibera.Server.svg"
                alt="Delibera.Server on NuGet"
                width={118}
                height={22}
              />
            </a>
            <a href="https://img.shields.io/badge/Docker%20Hub-2496ED?logo=docker&logoColor=fff">
              {/* eslint-disable-next-line @next/next/no-img-element */}
              <img
                src="https://img.shields.io/badge/Docker%20Hub-2496ED?logo=docker&logoColor=fff"
                alt="Docker Hub"
                width={104}
                height={22}
              />
            </a>
          </div>

          <p style={{ marginTop: 20, fontSize: '0.9rem' }}>
            <a href="https://github.com/techbuzzz/Delibera/blob/main/README.md">English</a> ·{' '}
            <a href="https://github.com/techbuzzz/Delibera/blob/main/README-RU.md">Русский</a>
          </p>
        </div>
      </div>

      <section className="band">
        <div className="wrap">
          <Reveal>
            <h2 className="band__title">Why Delibera?</h2>
            <p className="band__hint">
              Six reasons teams choose structured deliberation over single-model answers.
            </p>
          </Reveal>
          <div className="feature-grid">
            {FEATURES.map((feature, index) => (
              <Reveal key={feature.title} delay={(index % 4 + 1) as 1 | 2 | 3 | 4}>
                <div className="feature">
                  <div className="feature__icon">{feature.icon}</div>
                  <h3 className="feature__title">{feature.title}</h3>
                  <p className="feature__desc">{feature.desc}</p>
                </div>
              </Reveal>
            ))}
          </div>
        </div>
      </section>

      <section className="band">
        <div className="wrap">
          <Reveal>
            <h2 className="band__title">Install</h2>
            <p className="band__hint">
              Three NuGet packages and two Docker images, published on every release.
            </p>
          </Reveal>

          <Reveal delay={1}>
            <h3>NuGet</h3>
            <pre>
              <code>{NUGET_PACKAGES.map((p) => `dotnet add package ${p.id}`).join('\n')}</code>
            </pre>
          </Reveal>

          <Reveal delay={2}>
            <h3>Docker Hub — no build required</h3>
            <pre>
              <code>{`curl -O https://raw.githubusercontent.com/techbuzzz/Delibera/main/deploy/docker-compose.hub.yml
export DELIBERA_VERSION=${version}   # omit to track: latest
docker compose -f docker-compose.hub.yml up -d`}</code>
            </pre>
          </Reveal>

          <Reveal delay={3}>
            <div className="callout">
              <p>
                <strong>The API is unauthenticated and unthrottled, and a debate spends real LLM
                credits.</strong> Every published port binds to <code>127.0.0.1</code>. Put an
                authenticating reverse proxy in front before exposing this stack to a network you
                do not control.
              </p>
            </div>
          </Reveal>
        </div>
      </section>

      <section className="band">
        <div className="wrap">
          <Reveal>
            <h2 className="band__title">What&rsquo;s new</h2>
            <p className="band__hint">
              Latest release: <Link href={releaseRoute(latestDocumented)}>v{latestDocumented}</Link>.
              Every number below was measured, not estimated — see{' '}
              <Link href="/measurements/">measured behaviour</Link>.
            </p>
          </Reveal>
          <div className="cards">
            {notes.map((note, index) => (
              <Reveal key={note.source} delay={(index % 3 + 1) as 1 | 2 | 3}>
                <Link href={releaseRoute(note.slug)} className="card">
                  <div className="t">{note.title}</div>
                  <div className="d">{note.description}</div>
                </Link>
              </Reveal>
            ))}
          </div>
          <p style={{ marginTop: 24 }}>
            <Link href="/blog/" className="btn btn-secondary">
              All release notes
              <ArrowRightIcon />
            </Link>
            {' '}
            <Link href="/changelog/" className="btn btn-secondary">
              Changelog
              <ArrowRightIcon />
            </Link>
          </p>
        </div>
      </section>

      <section className="band">
        <div className="wrap">
          <Reveal>
            <h2 className="band__title">Documentation</h2>
            <p className="band__hint">Start here, then go deeper.</p>
          </Reveal>
          <div className="cards">
            {featuredDocs.map((page, index) => (
              <Reveal key={page.source} delay={(index % 3 + 1) as 1 | 2 | 3}>
                <Link href={docRoute(page.slug)} className="card">
                  <div className="t">{page.title}</div>
                  <div className="d">{page.description}</div>
                </Link>
              </Reveal>
            ))}
            <Reveal delay={2}>
              <Link className="card" href="/templates/">
                <div className="t">Debate templates</div>
                <div className="d">Six built-in councils, parsed from the code that ships in the package.</div>
              </Link>
            </Reveal>
            <Reveal delay={3}>
              <Link className="card" href="/measurements/">
                <div className="t">Measured behaviour</div>
                <div className="d">Latency, throughput and token costs with the harness that produced them.</div>
              </Link>
            </Reveal>
          </div>
          <p style={{ marginTop: 24 }}>
            <Link href="/docs/" className="btn btn-primary">
              <BookIcon />
              All documentation
            </Link>
          </p>
        </div>
      </section>

      <section className="band">
        <div className="wrap">
          <Reveal>
            <h2 className="band__title">Get it</h2>
          </Reveal>
          <div className="cards">
            {NUGET_PACKAGES.map((pkg, index) => (
              <Reveal key={pkg.id} delay={(index % 3 + 1) as 1 | 2 | 3}>
                <a className="card" href={NUGET_URL(pkg.id)} target="_blank" rel="noopener noreferrer">
                  <div className="t">{pkg.id}</div>
                  <div className="d">{pkg.description}</div>
                </a>
              </Reveal>
            ))}
            {DOCKER_IMAGES.map((image, index) => (
              <Reveal key={image.id} delay={(index % 2 + 1) as 1 | 2}>
                <a className="card" href={image.hub} target="_blank" rel="noopener noreferrer">
                  <div className="t">Docker Hub — {image.id}</div>
                  <div className="d">{image.description}</div>
                  <span className="meta">linux/amd64 + linux/arm64</span>
                </a>
              </Reveal>
            ))}
            <Reveal delay={3}>
              <a className="card" href={REPO_URL} target="_blank" rel="noopener noreferrer">
                <div className="t">Source</div>
                <div className="d">MIT licensed. Clone and build with a single solution.</div>
              </a>
            </Reveal>
          </div>
        </div>
      </section>
    </PageShell>
  )
}
