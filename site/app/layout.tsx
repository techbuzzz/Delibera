import type { ReactNode } from 'react'
import { brandImage, siteOrigin } from '@/lib/paths'
import './globals.css'

// Two different rules apply to the two kinds of URL below, which is the whole trap:
//
//   - `openGraph` / `twitter` images are *joined* onto `metadataBase` path-wise by Next, so
//     they must NOT already contain the base path — passing it yields `.../Delibera/Delibera/…`.
//   - `icons` are emitted verbatim, so they MUST already contain it, or the favicon 404s on
//     a Pages deploy where the site is served from a subdirectory.
//
// See scripts/verify-export.mjs, which fails the build on either mistake.
const BANNER = '/img/delibera-horizontal-1920x480.png'
const FAVICON = brandImage('delibera-favicon-1024x1024.png')

/**
 * Site-wide metadata.
 *
 * `metadataBase` is required for any relative URL (OpenGraph images, canonical links) to
 * resolve against an absolute origin — Next.js otherwise emits a warning and drops the tag.
 * It is derived from the deployment origin *including* the base path, so a canonical link
 * emitted for `/docs/quickstart/` comes out as
 * `https://techbuzzz.github.io/Delibera/docs/quickstart/` rather than dropping the prefix.
 */
export const metadata = {
  metadataBase: new URL(siteOrigin('/')),
  title: {
    default: 'Delibera — Thoughtful AI Decisions',
    template: '%s · Delibera',
  },
  description:
    'Collective decision making through structured AI deliberation — multi-model councils with RAG, pgvector, Knowledge Keeper, MCP tools and live SSE streaming.',
  applicationName: 'Delibera',
  authors: [{ name: 'Delibera' }],
  keywords: [
    'AI',
    'LLM',
    'multi-agent',
    'deliberation',
    '.NET',
    'C#',
    'RAG',
    'pgvector',
    'Qdrant',
    'MCP',
  ],
  openGraph: {
    type: 'website',
    siteName: 'Delibera',
    title: 'Delibera — Thoughtful AI Decisions',
    description:
      'Multi-model AI councils with a chairman, RAG knowledge keeper, MCP tools and live streaming.',
    images: [{ url: BANNER, width: 1920, height: 480 }],
  },
  twitter: {
    card: 'summary_large_image',
    title: 'Delibera — Thoughtful AI Decisions',
    description: 'Multi-model AI councils with a chairman, RAG, MCP tools and live streaming.',
    images: [BANNER],
  },
  icons: {
    icon: [{ url: FAVICON, type: 'image/png' }],
  },
}

export const viewport = {
  themeColor: [
    { media: '(prefers-color-scheme: light)', color: '#ffffff' },
    { media: '(prefers-color-scheme: dark)', color: '#0d1117' },
  ],
}

export default function RootLayout({ children }: { children: ReactNode }) {
  return (
    <html lang="en">
      <body>
        <a className="skip-link" href="#main">
          Skip to content
        </a>
        {children}
      </body>
    </html>
  )
}