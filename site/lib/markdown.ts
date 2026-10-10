import { Marked } from 'marked'
import GithubSlugger from 'github-slugger'
import { createHighlighter, type Highlighter } from 'shiki'
import { githubBlob, githubTree } from './site'
import { withBasePath } from './paths'

/**
 * Markdown -> HTML for build-time rendering.
 *
 * Three things happen here that a bare `marked.parse()` would not do:
 *
 *  1. Syntax highlighting via Shiki, using the same TextMate grammars GitHub uses. The
 *     highlighter is created once per build and shared across every page.
 *  2. Stable heading anchors, generated with `github-slugger` so the `#anchor` fragments
 *     written in the Markdown keep resolving after the docs move from GitHub to this site.
 *  3. Link rewriting. The Markdown in `docs/` links to its neighbours by repository path
 *     (`../CHANGELOG.md`, `QuickStart.md`, `docs/performance-measurements.md#3-context-compression`).
 *     Those targets do not exist on a static host, so each one is resolved to either a site
 *     route or an absolute GitHub URL.
 *
 * The Markdown is repository content and is trusted, but `raw` HTML is still passed through
 * rather than escaped because the docs legitimately embed `<div align="center">` banners and
 * badge images. It is first-party content in a repository we build, not user input.
 */

/** Languages the docs actually use. Loading all of Shiki's grammars would triple build time. */
const LANGS = [
  'csharp',
  'bash',
  'shell',
  'json',
  'yaml',
  'xml',
  'dockerfile',
  'docker',
  'sql',
  'ini',
  'console',
  'powershell',
  'diff',
  'typescript',
  'markdown',
  'text',
] as const

let highlighterPromise: Promise<Highlighter> | null = null

function getHighlighter(): Promise<Highlighter> {
  highlighterPromise ??= createHighlighter({
    themes: ['github-light', 'github-dark'],
    langs: [...LANGS],
  })

  return highlighterPromise
}

/**
 * Repository-relative paths that have a page on this site, mapped to the route that serves
 * them. Filled in by `createMarkdown()` from the caller's content index, so adding a document
 * automatically makes every existing cross-link to it resolve to a real page.
 */
export type RouteIndex = Map<string, string>

function normaliseRepoPath(input: string): string {
  return input.replace(/^\.\//, '').replace(/^\/+/, '')
}

export interface MarkdownOptions {
  /** `docs/QuickStart.md` — the document being rendered, used to resolve relative links. */
  source: string
  /** repo path -> site route, for every document published on the site. */
  routes: RouteIndex
  /**
   * Drop the document's own banner and first `#` heading.
   *
   * Set by every page that renders a `PageHead` above the body: `QuickStart.md` opens with a
   * centred logo, a tagline, a link to the translation and then `# Delibera — Quick Start`.
   * Rendered as-is on a page that already has a title bar, that logo and headline appear
   * twice, and the banner's tagline becomes a top-level entry in the table of contents.
   */
  stripPreamble?: boolean
}

/**
 * Remove the leading banner block and the first H1 from a document.
 *
 * Both are optional and position-sensitive: only a `<div>` at the very start of the file is
 * treated as a banner, and only the *first* H1 is dropped, so headings further down keep
 * their level and the in-page table of contents is unaffected.
 */
export function stripDocumentPreamble(markdown: string): string {
  let text = markdown.replace(/^\s+/, '')

  text = text.replace(/^<div\b[^>]*>[\s\S]*?<\/div>\s*/i, '')

  // The first H1 is the document title, already shown by the page header.
  text = text.replace(/^#\s+.+?#*\s*(\r?\n)+/, '')

  return text
}

/**
 * Rewrite one link target.
 *
 * Order matters: absolute URLs and bare anchors are returned untouched, then we try to
 * resolve the path against the repository, and only fall back to GitHub when the target is
 * not something this site serves.
 */
function rewriteHref(href: string, source: string, routes: RouteIndex): string {
  if (!href || href.startsWith('#') || /^[a-z][a-z0-9+.-]*:/i.test(href) || href.startsWith('//')) {
    return href
  }

  // Slice at the `#` rather than splitting on it: the fragment has to be carried over
  // *with* its marker. Dropping it turns `QuickStart.md#cost-limits` into
  // `/docs/quickstart/cost-limits`, which looks like a path and 404s.
  const hashIndex = href.indexOf('#')
  const rawPath = hashIndex === -1 ? href : href.slice(0, hashIndex)
  const hash = hashIndex === -1 ? '' : href.slice(hashIndex)

  if (!rawPath) return href

  // Resolve relative to the document's own directory, then normalise `..` segments.
  const sourceDir = source.includes('/') ? source.slice(0, source.lastIndexOf('/')) : ''
  const joined = rawPath.startsWith('/')
    ? normaliseRepoPath(rawPath)
    : normaliseRepoPath(`${sourceDir}/${rawPath}`)

  const resolved = normalise(joined)
  if (!resolved) return href

  // Images and other binaries are served from the site's own `public/` directory.
  if (/\.(png|jpe?g|gif|svg|webp|avif)$/i.test(resolved)) {
    if (resolved.startsWith('img/')) return withBasePath(`/img/${resolved.slice('img/'.length)}${hash}`)
    return githubBlob(resolved)
  }

  const route = routes.get(resolved)
  if (route) return `${withBasePath(route)}${hash}`

  if (resolved.endsWith('.md')) {
    return githubBlob(resolved)
  }

  if (resolved.endsWith('/')) return githubTree(resolved.slice(0, -1))

  // A source file, a compose file, a directory — none of these are rendered here.
  return /\.[a-z0-9]+$/i.test(resolved) ? githubBlob(resolved) : githubTree(resolved)
}

function normalise(p: string): string {
  const out: string[] = []

  for (const segment of p.split('/')) {
    if (segment === '' || segment === '.') continue
    if (segment === '..') {
      out.pop()
      continue
    }
    out.push(segment)
  }

  return out.join('/')
}

function escapeAttribute(value: string): string {
  return value
    .replace(/&/g, '&amp;')
    .replace(/"/g, '&quot;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
}

export interface RenderResult {
  html: string
  /** Ordered table of contents built from the rendered headings. */
  headings: Array<{ depth: number; text: string; id: string }>
}

/** Render one Markdown document to HTML. */
export async function renderMarkdown(markdown: string, options: MarkdownOptions): Promise<RenderResult> {
  const highlighter = await getHighlighter()
  const slugger = new GithubSlugger()
  const headings: RenderResult['headings'] = []

  // Shiki is async and `marked`'s renderer must be synchronous, so highlighting happens in
  // the `walkTokens` hook — which does accept a promise — and the result is handed to the
  // renderer through this map. Marked awaits the hook before parsing, so every entry is
  // present by the time `code` runs.
  const highlighted = new WeakMap<object, string>()

  const marked = new Marked({
    gfm: true,
    async: true,

    walkTokens: async (token) => {
      if (token.type !== 'code') return

      const language = (token.lang ?? '').split(/\s+/)[0]?.toLowerCase() ?? ''
      if (!language || !highlighter.getLoadedLanguages().includes(language)) return

      highlighted.set(
        token,
        highlighter.codeToHtml(token.text, {
          lang: language,
          themes: { light: 'github-light', dark: 'github-dark' },
          defaultColor: 'light',
        }),
      )
    },

    renderer: {
      heading({ tokens, depth }) {
        const text = this.parser.parseInline(tokens)
        const plain = stripTags(text)
        const id = slugger.slug(plain)

        headings.push({ depth, text: plain, id })

        return `<h${depth} id="${escapeAttribute(id)}" class="md-heading">${text}<a class="md-anchor" href="#${escapeAttribute(id)}" aria-label="Link to this section">#</a></h${depth}>\n`
      },

      link({ href, title, tokens }) {
        const text = this.parser.parseInline(tokens)
        const target = rewriteHref(href, options.source, options.routes)
        const titleAttr = title ? ` title="${escapeAttribute(title)}"` : ''

        // Absolute links leave the site; open them in a new tab and drop the referrer.
        const isExternal = /^https?:\/\//i.test(target)
        const externalAttrs = isExternal ? ' target="_blank" rel="noopener noreferrer"' : ''

        return `<a href="${escapeAttribute(target)}"${titleAttr}${externalAttrs}>${text}</a>`
      },

      image({ href, title, text }) {
        const target = rewriteHref(href, options.source, options.routes)
        const titleAttr = title ? ` title="${escapeAttribute(title)}"` : ''
        return `<img src="${escapeAttribute(target)}" alt="${escapeAttribute(text)}"${titleAttr} loading="lazy" decoding="async" />`
      },

      /**
       * Raw HTML blocks are passed through by `marked` verbatim, which is what lets the
       * docs keep their `<div align="center">` banners and badge images. The side effect is
       * that an `<img src="../img/...">` inside raw HTML never reaches the `image`
       * renderer, so its relative path would survive into the page and resolve against
       * `/docs/quickstart/` instead of the site root.
       *
       * Rewriting the `src`/`href` attributes of inline HTML fixes that without parsing
       * the markup into a DOM.
       */
      html(token) {
        return rewriteInlineHtml(token.text, options.source, options.routes)
      },

      code(token) {
        const preHighlighted = highlighted.get(token)
        if (preHighlighted) return `<div class="md-code">${preHighlighted}</div>\n`

        // No grammar loaded for this fence: emit escaped plain text rather than guessing a
        // language, which would highlight every shell transcript as if it were C#.
        return `<div class="md-code"><pre><code>${escapeHtml(token.text)}</code></pre></div>\n`
      },
    },
  })

  const html = await marked.parse(options.stripPreamble ? stripDocumentPreamble(markdown) : markdown)

  return { html, headings }
}

function stripTags(html: string): string {
  return html.replace(/<[^>]+>/g, '').replace(/&amp;/g, '&').trim()
}

function escapeHtml(value: string): string {
  return value
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
}

/**
 * Rewrite `src=` / `href=` inside a raw HTML block so relative repository paths resolve.
 *
 * Only two attributes are touched, and only when their value looks like a relative path —
 * an external `https://` image or a `#anchor` is left exactly as written.
 */
function rewriteInlineHtml(html: string, source: string, routes: RouteIndex): string {
  return html.replace(/\b(src|href)="([^"]+)"/g, (match, attribute: string, value: string) => {
    if (!value || value.startsWith('#') || /^(https?:|data:|mailto:|\/\/)/i.test(value)) return match

    return `${attribute}="${escapeAttribute(rewriteHref(value, source, routes))}"`
  })
}