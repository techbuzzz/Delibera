#!/usr/bin/env node
/**
 * Verify the static export before it is published.
 *
 * `next build` proves the pages compiled; it does not prove they link to each other
 * correctly. Three failure modes survive a green build and only show up on the live site:
 *
 *   - an internal href that no emitted file matches (404 for the visitor);
 *   - a `basePath`-prefixed asset that the export does not actually contain;
 *   - a route still written in the old mixed case, which GitHub Pages resolves
 *     case-sensitively while every local check passes.
 *
 * Exits non-zero on the first class of failure, so CI refuses to deploy a broken artifact.
 *
 * Usage: node scripts/verify-export.mjs [outDir]
 */
import { existsSync, readFileSync, readdirSync, statSync } from 'node:fs'
import { dirname, join, relative, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

const siteRoot = dirname(dirname(fileURLToPath(import.meta.url)))
const outDir = resolve(siteRoot, process.argv[2] ?? 'out')

if (!existsSync(outDir)) {
  console.error(`[verify-export] ${outDir} does not exist - run "npm run build" first`)
  process.exit(1)
}

const basePath = process.env.PAGES_BASE_PATH ?? ''
const problems = []
const checked = { pages: 0, internalLinks: 0, assets: 0 }

/** Every file in the export, as forward-slash paths relative to `outDir`. */
const files = []
;(function walk(dir) {
  for (const entry of readdirSync(dir, { withFileTypes: true })) {
    const full = join(dir, entry.name)
    if (entry.isDirectory()) walk(full)
    else files.push(relative(outDir, full).replace(/\\/g, '/'))
  }
})(outDir)

const fileSet = new Set(files)

/**
 * Does this URL path correspond to a file the export produced?
 *
 * `trailingSlash: true` means `/blog/10.5.2/` is served by `blog/10.5.2/index.html`, while
 * `/sitemap.xml` is a plain file. Both spellings are accepted.
 */
function targetExists(pathname) {
  const clean = pathname.replace(/^\/+|\/+$/g, '')

  return (
    fileSet.has(clean) ||
    fileSet.has(`${clean}/index.html`) ||
    fileSet.has(`${clean}.html`)
  )
}

function checkHtml(file) {
  const html = readFileSync(join(outDir, file), 'utf8')
  checked.pages += 1

  for (const match of html.matchAll(/(?:href|src)="([^"]+)"/g)) {
    const url = match[1]
    if (!url || url.startsWith('#') || /^(https?:|mailto:|data:|\/\/)/i.test(url)) continue

    // Root-relative to this site: must carry the deployment base path.
    if (!url.startsWith('/')) {
      problems.push(`${file}: relative URL "${url}" - it will resolve against the current path`)
      continue
    }

    if (basePath) {
      if (!url.startsWith(`${basePath}/`) && url !== basePath) {
        problems.push(`${file}: "${url}" is missing the basePath prefix "${basePath}"`)
        continue
      }
    }

    const pathname = url.slice(basePath.length)
    if (pathname === '' || pathname === '/') continue

    const isAsset = /\.(png|jpe?g|gif|svg|webp|avif|css|js|woff2?|xml|txt|ico)$/i.test(pathname)
    if (isAsset) checked.assets += 1
    else checked.internalLinks += 1

    // A fragment addresses a position *within* an existing page, so it is not part of the
    // file lookup: `/docs/quickstart/#cost-limits` resolves to `docs/quickstart/index.html`
    // plus a scroll position. Checking the whole string would report a false 404.
    const [pathPart, fragment] = pathname.split('#')

    if (!targetExists(pathPart)) {
      problems.push(`${file}: link target "${url}" has no file in the export`)
      continue
    }

    // When the target page exists, verify the anchor too. A renamed heading leaves the page
    // published but every cross-reference to it dead — which no build error reports.
    if (fragment) {
      const targetFile = resolveTargetFile(pathPart)
      if (targetFile && !anchorsOf(targetFile).has(fragment)) {
        problems.push(`${file}: "${url}" points at an anchor that does not exist on the page`)
      }
    }
  }
}

/** The exported file that actually serves a route path. */
function resolveTargetFile(pathname) {
  const clean = pathname.replace(/^\/+|\/+$/g, '')
  const candidates = [clean, `${clean}/index.html`, `${clean}.html`]
  return candidates.find((candidate) => fileSet.has(candidate))
}

/** Every `id="..."` declared by an exported HTML page, plus its named anchors. */
const anchorCache = new Map()

function anchorsOf(file) {
  const cached = anchorCache.get(file)
  if (cached) return cached

  const html = readFileSync(join(outDir, file), 'utf8')
  const anchors = new Set()
  for (const match of html.matchAll(/\sid="([^"]+)"/g)) {
    if (match[1]) anchors.add(match[1])
  }

  anchorCache.set(file, anchors)
  return anchors
}

for (const file of files) {
  if (file.endsWith('.html') || file.endsWith('.txt')) checkHtml(file)
}

// sitemap.xml must reference only pages that exist, otherwise it advertises 404s.
const sitemap = join(outDir, 'sitemap.xml')
if (existsSync(sitemap)) {
  const xml = readFileSync(sitemap, 'utf8')

  for (const match of xml.matchAll(/<loc>([^<]+)<\/loc>/g)) {
    const loc = match[1]
    if (!loc) continue

    // Entries may be absolute or root-relative; both need a base to parse.
    let pathname
    try {
      pathname = new URL(loc, 'https://example.invalid').pathname
    } catch {
      problems.push(`sitemap.xml: <loc>${loc}</loc> is not a valid URL`)
      continue
    }

    const relativePath = pathname.replace(new RegExp(`^${basePath}`), '').replace(/^\/+/, '')
    if (relativePath === '') continue

    if (!targetExists(relativePath)) problems.push(`sitemap.xml: <loc>${loc}</loc> has no file`)
  }
}

// The export must not carry source maps or development-only leftovers.
for (const file of files) {
  if (/\.map$/.test(file)) problems.push(`${file}: source map shipped to production`)
}

/**
 * Every page must actually load a stylesheet.
 *
 * A stylesheet that is present in the source tree but never imported produces a build with
 * zero CSS files, zero errors, and a site that renders as unstyled default HTML. Nothing in
 * `next build` reports it, and the export verification above passes — it only checks links
 * that exist, and a missing `<link>` is not a link to anything.
 */
const htmlFiles = files.filter((file) => file.endsWith('.html'))
const pagesWithCss = htmlFiles.filter((file) => {
  const html = readFileSync(join(outDir, file), 'utf8')
  return /<link[^>]+rel="stylesheet"/i.test(html) || /<style[\s>]/i.test(html)
})

if (files.some((file) => file.endsWith('.css'))) {
  if (pagesWithCss.length === 0) {
    problems.push('no page links a stylesheet - app/globals.css is probably never imported')
  } else if (pagesWithCss.length < htmlFiles.length) {
    const unstyled = htmlFiles.filter((file) => !pagesWithCss.includes(file)).slice(0, 5)
    problems.push(`${htmlFiles.length - pagesWithCss.length} page(s) render without CSS: ${unstyled.join(', ')}`)
  }
} else {
  problems.push('the export contains no .css file at all')
}

/**
 * No doubled base path.
 *
 * `metadataBase` already carries the deployment prefix, and Next joins metadata URLs onto it
 * path-wise rather than resolving them as absolute URLs. Passing an image path that *also*
 * contains the prefix emits `.../Delibera/Delibera/img/...` — a URL that looks plausible in
 * a diff and 404s for every crawler and social card.
 */
if (basePath) {
  const doubled = basePath + basePath
  for (const file of htmlFiles) {
    const html = readFileSync(join(outDir, file), 'utf8')
    if (html.includes(doubled)) {
      problems.push(`${file}: contains a doubled basePath "${doubled}"`)
    }
  }
}

const totalBytes = files.reduce((sum, file) => sum + statSync(join(outDir, file)).size, 0)

console.log(
  `[verify-export] ${checked.pages} html files, ${checked.internalLinks} internal links, ${checked.assets} asset refs, basePath="${basePath}"`,
)
console.log(`[verify-export] export size: ${(totalBytes / 1024 / 1024).toFixed(2)} MB`)

if (problems.length > 0) {
  console.error(`[verify-export] ${problems.length} problem(s):`)
  for (const problem of problems.slice(0, 40)) console.error(`  - ${problem}`)
  if (problems.length > 40) console.error(`  ... and ${problems.length - 40} more`)
  process.exit(1)
}

console.log('[verify-export] OK - every internal link and asset resolves inside the export')