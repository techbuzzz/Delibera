import { promises as fs } from 'node:fs'
import path from 'node:path'

/**
 * Build-time content loader.
 *
 * The site is a *view* over files that already live in this repository — the Markdown in
 * `docs/`, `CHANGELOG.md`, the version in `Delibera.Core.csproj`, the debate templates in
 * `DebateTemplate.cs`. Nothing here copies content into a second location, so the site can
 * never advertise a version, a feature or a measurement that the code does not have.
 *
 * Every read happens during `next build`. `output: 'export'` means there is no server at
 * runtime, which is exactly why `fs` is legal here and would be a bug in a deployed app.
 */

const REPO_ROOT = path.resolve(process.cwd(), '..')

async function readText(...segments: string[]): Promise<string> {
  return fs.readFile(path.join(REPO_ROOT, ...segments), 'utf8')
}

async function exists(...segments: string[]): Promise<boolean> {
  try {
    await fs.access(path.join(REPO_ROOT, ...segments))
    return true
  } catch {
    return false
  }
}

/**
 * The version every page renders.
 *
 * Read from the project that `publish-nuget.yml` actually packs, not from a git tag and not
 * from CHANGELOG.md. A tag can be pushed before the package reaches nuget.org, and the
 * changelog is edited by hand; both can disagree with what consumers can install. The csproj
 * cannot — it is the input to `dotnet pack`.
 *
 * Failing loudly is deliberate. A placeholder silently rendered as "v0.0.0" on the front page
 * of a published site is a worse outcome than a failed deploy.
 */
export async function getVersion(): Promise<string> {
  const csproj = await readText('src', 'Delibera.Core', 'Delibera.Core.csproj')
  const match = csproj.match(/<Version>([^<]+)<\/Version>/)

  if (!match?.[1]) {
    throw new Error(
      'Could not resolve <Version> from src/Delibera.Core/Delibera.Core.csproj. ' +
        'The site refuses to publish an unverified version.',
    )
  }

  return match[1]
}

export interface Release {
  /** `10.5.2`, or `unreleased` for the working-set section. */
  version: string
  /** ISO date when the changelog records one; older entries only carry the year. */
  date: string | null
  /** Raw Markdown body of the release section. */
  body: string
}

/**
 * Parse `## [10.5.2] - 2026-10-09` headings out of CHANGELOG.md.
 *
 * The changelog is the release ledger and the site reads it rather than duplicating it. Older
 * entries are dated `2026` only, which is why `date` is nullable: rendering a fabricated
 * day-of-month would be worse than showing the year alone.
 */
export async function getReleases(): Promise<Release[]> {
  const changelog = await readText('CHANGELOG.md')
  const header = /^## \[([^\]]+)\](?:\s+-\s+(\S+))?\s*$/gm

  const releases: Release[] = []
  const matches = [...changelog.matchAll(header)]

  for (const [index, match] of matches.entries()) {
    const bodyStart = (match.index ?? 0) + match[0].length
    const bodyEnd = matches[index + 1]?.index ?? changelog.length

    const version = (match[1] ?? '').trim()
    const rawDate = match[2]?.trim()

    releases.push({
      version: version.toLowerCase() === 'unreleased' ? 'unreleased' : version,
      date: rawDate && /^\d{4}-\d{2}-\d{2}$/.test(rawDate) ? rawDate : null,
      body: changelog.slice(bodyStart, bodyEnd).trim(),
    })
  }

  return releases
}

/** True for `10.5.2`, false for `unreleased` and for `0.0.0`-style placeholders. */
export function isReleased(release: Release): boolean {
  return release.version !== 'unreleased'
}

/** Semantic-version compare, newest first. `unreleased` always sorts to the top. */
export function byVersionDescending(a: Release, b: Release): number {
  if (a.version === 'unreleased') return -1
  if (b.version === 'unreleased') return 1

  const pa = a.version.split('.').map(Number)
  const pb = b.version.split('.').map(Number)

  for (let i = 0; i < 3; i += 1) {
    const diff = (pb[i] ?? 0) - (pa[i] ?? 0)
    if (diff !== 0) return diff
  }

  return 0
}

/**
 * The version whose release note the "what's new" cards link to.
 *
 * Resolved against what actually exists on disk rather than against `getVersion()`: a
 * release may ship the binary before its note is written, and a card pointing at a
 * not-yet-existing page is a 404 on the front page of the site.
 */
export async function getLatestDocumentedVersion(): Promise<string> {
  const version = await getVersion()
  if (await exists('docs', `WhatsNew-v${version}.md`)) return version

  // Walk down the patch line until a note exists (10.5.2 -> 10.5.1 -> ...).
  const parts = version.split('.').map(Number)
  for (let patch = (parts[2] ?? 0) - 1; patch >= 0; patch -= 1) {
    const candidate = [...parts.slice(0, 2), patch].join('.')
    if (await exists('docs', `WhatsNew-v${candidate}.md`)) return candidate
  }

  const documented = await listReleaseNotes()
  const newest = documented[0]

  return (newest && versionFromFileName(newest.source)) ?? version
}

export interface DocPage {
  slug: string
  /** Repository-relative POSIX path, e.g. `docs/QuickStart.md`. */
  source: string
  title: string
  /** One-paragraph summary pulled from the first paragraph after the title. */
  description: string
  /** `ru` for files suffixed `-RU`, otherwise `en`. */
  lang: 'en' | 'ru'
  /** Milliseconds since epoch, from the file's mtime. Zero when unknown (static checkout). */
  updatedAt: number
}

/** Strip Markdown/HTML decoration so a heading reads as a title, not as markup. */
function plainText(markdown: string): string {
  return markdown
    .replace(/<[^>]+>/g, ' ')
    .replace(/!\[([^\]]*)\]\([^)]*\)/g, '$1')
    .replace(/\[([^\]]*)\]\([^)]*\)/g, '$1')
    .replace(/`([^`]*)`/g, '$1')
    .split('\n')
    .map((line) =>
      line
        // Heading markers only where they are markup: a leading `##` or a closing `##`.
        // A `#` in the middle of a word is part of it — `C# 15`, `F#`, `#1` — and stripping
        // it turns the project's headline language name into "C 15".
        .replace(/^\s*#{1,6}\s*/, '')
        .replace(/\s*#+\s*$/, '')
        .replace(/[*_~>]/g, ''),
    )
    .join(' ')
    .replace(/\s+/g, ' ')
    .trim()
}

/**
 * Pull the document title and a one-line description out of Markdown.
 *
 * Three shapes have to be handled, all of them present in `docs/`:
 *
 *   - `QuickStart.md` opens with an HTML banner and puts its `#` title on line 5, so
 *     "line one is the title" does not hold;
 *   - release notes and the roadmap open with a `>` status block — `**Release type:** minor…`
 *     — which is metadata about the document, not prose a card should show;
 *   - the work-log entries open with a `**Problem.** …` paragraph that wraps across several
 *     source lines, followed by a table. Taking the first line alone yields a card reading
 *     "Delibera.Core referenced Microsoft.Extensions.AI 10.8.0 and".
 *
 * So: the first ATX heading is the title, and the description is the first whole *paragraph*
 * after it — skipping block quotes, HTML, tables, lists and headings, and joining the wrapped
 * lines before trimming.
 */
export function extractHeadings(markdown: string): { title: string; description: string } {
  const lines = markdown.split(/\r?\n/)
  let title = ''
  let titleIndex = -1

  for (const [index, line] of lines.entries()) {
    const match = line.match(/^#\s+(.+?)\s*#*\s*$/)
    if (match?.[1]) {
      title = plainText(match[1])
      titleIndex = index
      break
    }
  }

  /** Line that opens a different block, so the current paragraph has ended. */
  const startsBlock = (text: string): boolean =>
    /^#{1,6}\s/.test(text) ||
    text.startsWith('>') ||
    text.startsWith('<') ||
    text.startsWith('|') ||
    text === '---' ||
    /^([-*+]\s|\d+\.\s)/.test(text)

  let description = ''

  for (let index = titleIndex + 1; index < lines.length; index += 1) {
    const text = (lines[index] ?? '').trim()

    if (!text) continue
    if (startsBlock(text)) continue

    // Collect the whole paragraph, so a wrapped sentence is not cut mid-clause.
    const paragraph: string[] = []
    while (index < lines.length) {
      const current = (lines[index] ?? '').trim()
      if (!current || startsBlock(current)) break
      paragraph.push(current)
      index += 1
    }

    const plain = plainText(paragraph.join(' '))

    // A line that is essentially only a link (plus an emoji or a short flag) carries no
    // description. That is the banner in `QuickStart.md`: a language link between the `#`
    // title and the real first sentence.
    const withoutLinks = paragraph
      .join(' ')
      .replace(/!?\[[^\]]*\]\([^)]*\)/g, '')
      .replace(/[*_~`>#]/g, '')
      .trim()

    if (!plain) continue
    if (withoutLinks.replace(/[^\p{L}]/gu, '').length < 12) continue

    description = truncate(plain, 200)
    break
  }

  return { title, description }
}

/** Trim to a word boundary with an ellipsis, so a card never ends mid-word. */
function truncate(text: string, limit: number): string {
  if (text.length <= limit) return text

  const cut = text.slice(0, limit)
  const lastSpace = cut.lastIndexOf(' ')
  return `${(lastSpace > limit * 0.6 ? cut.slice(0, lastSpace) : cut).replace(/[,;:.]$/, '')}…`
}

async function mtime(...segments: string[]): Promise<number> {
  try {
    const stat = await fs.stat(path.join(REPO_ROOT, ...segments))
    return stat.mtimeMs
  } catch {
    return 0
  }
}

/**
 * URL segment for a document, derived from its file name.
 *
 * Lower-cased on purpose: GitHub Pages serves from a case-sensitive origin, so a folder
 * emitted as `QuickStart/` is unreachable from a link written as `/docs/quickstart/`.
 * Windows checkouts and local dev servers are case-insensitive, which is exactly why this
 * bug survives local testing and 404s only in production.
 *
 * `WhatsNew-v10.5.2.md` -> `10.5.2`; `-RU` is kept so translations get their own URL rather
 * than overwriting the English page at build time.
 */
function slugFor(fileName: string): string {
  return fileName
    .replace(/\.md$/i, '')
    .replace(/^WhatsNew-v/i, '')
    .toLowerCase()
}

/** `WhatsNew-v10.5.2.md` -> `10.5.2`. Used to build the release-note index. */
export function versionFromFileName(fileName: string): string | null {
  const match = fileName.match(/^WhatsNew-v(\d+(?:\.\d+)*)/i)
  return match?.[1] ?? null
}

/**
 * URL slug for a Markdown file under `docs/`.
 *
 * The slug mirrors the directory layout, so a link written as `docs/TASKS/README.md` and one
 * written as `/docs/tasks/` resolve to the same page. A directory's `README.md` collapses to
 * the directory itself — `docs/TASKS/README.md` becomes `/docs/tasks/`, which is the
 * conventional index URL and what the previous landing page linked to by name.
 *
 * Lower-cased throughout, including directory names: GitHub Pages serves from a
 * case-sensitive origin, so an emitted `TASKS/` folder is unreachable from a link written
 * as `/docs/tasks/`. Windows checkouts and local dev servers are case-insensitive, which is
 * exactly why that bug survives local testing and 404s only in production.
 */
export function slugForSource(source: string): string {
  const relative = source.replace(/^docs\//, '').replace(/\.md$/i, '')
  const segments = relative.split('/').filter(Boolean)

  const last = segments[segments.length - 1] ?? ''
  if (last.toLowerCase() === 'readme') segments.pop()

  return segments
    .map((segment, index) => {
      // Only the file-name segment carries the `WhatsNew-v` prefix; a directory that happens
      // to be named `WhatsNew-v…` is not a release note.
      const stripped =
        index === segments.length - 1 ? segment.replace(/^WhatsNew-v/i, '') : segment
      return stripped.toLowerCase()
    })
    .join('/')
}

async function describeDoc(source: string): Promise<DocPage> {
  const markdown = await fs.readFile(path.join(REPO_ROOT, source), 'utf8')
  const { title, description } = extractHeadings(markdown)

  return {
    source,
    slug: slugForSource(source),
    title,
    description,
    lang: /-RU\.md$/i.test(source) ? 'ru' : 'en',
    updatedAt: await mtime(source),
  }
}

/**
 * Every Markdown file under `docs/`, at any depth, minus the documents that get their own
 * section.
 *
 * Depth matters: the work log in `docs/TASKS/` and the planning notes in `docs/scope/` are
 * committed, public, and were linked from the previous landing page — leaving them out would
 * be a coverage regression dressed up as a curation decision.
 *
 * Excluded on purpose:
 *   - `WhatsNew-*.md` — published under `/blog/`;
 *   - `performance-measurements.md` — published under `/measurements/`;
 *   - `v10.2.6.md` — the prose export of a release note whose canonical form is
 *     `WhatsNew-v10.2.6.md`, so publishing both duplicates the whole article;
 *   - `docs/habr/` — a draft being written for Habr, carrying editorial notes about which
 *     headline to use. Same category as `.articles/`, which is gitignored for the same
 *     reason: working material for another platform, not documentation.
 */
export async function listDocPages(): Promise<DocPage[]> {
  const files = await collectMarkdown(path.join(REPO_ROOT, 'docs'), 'docs')

  const published = files
    .filter((source) => !/^docs\/WhatsNew-/i.test(source))
    .filter((source) => source !== 'docs/performance-measurements.md')
    .filter((source) => !/^docs\/v\d/i.test(source))
    .filter((source) => !source.startsWith('docs/habr/'))
    .sort((a, b) => a.localeCompare(b))

  return Promise.all(published.map(describeDoc))
}

/** Recursively collect `.md` paths under `dir`, returned as forward-slash repo-relative paths. */
async function collectMarkdown(dir: string, prefix: string): Promise<string[]> {
  const entries = await fs.readdir(dir, { withFileTypes: true })
  const found: string[] = []

  for (const entry of entries) {
    const name = entry.name
    if (name.startsWith('.')) continue

    if (entry.isDirectory()) {
      found.push(...(await collectMarkdown(path.join(dir, name), `${prefix}/${name}`)))
      continue
    }

    if (entry.isFile() && name.endsWith('.md')) {
      found.push(`${prefix}/${name}`)
    }
  }

  return found
}

/** Release notes, newest first. */
export async function listReleaseNotes(): Promise<DocPage[]> {
  const entries = await fs.readdir(path.join(REPO_ROOT, 'docs'), { withFileTypes: true })
  const files = entries
    .filter((entry) => entry.isFile() && /^WhatsNew-v\d/i.test(entry.name))
    .map((entry) => `docs/${entry.name}`)

  const pages = await Promise.all(files.map(describeDoc))

  // Newest first. `compareVersionStrings` already orders descending, so the operands go in
  // as-is — passing them swapped silently produces an ascending list, which on the home
  // page reads as "the newest release is 10.2.6".
  return pages.sort((a, b) =>
    compareVersionStrings(
      versionFromFileName(path.basename(a.source)),
      versionFromFileName(path.basename(b.source)),
    ),
  )
}

/** `null` sorts last, so an unversioned draft never outranks a shipped release. */
function compareVersionStrings(a: string | null, b: string | null): number {
  if (a === null && b === null) return 0
  if (a === null) return 1
  if (b === null) return -1

  const pa = a.split('.').map(Number)
  const pb = b.split('.').map(Number)

  for (let i = 0; i < 3; i += 1) {
    const diff = (pb[i] ?? 0) - (pa[i] ?? 0)
    if (diff !== 0) return diff
  }

  return 0
}

export interface DocSource {
  source: string
  markdown: string
}

/** Load one document by its repository-relative path. */
export async function loadDoc(source: string): Promise<DocSource> {
  return { source, markdown: await readText(source) }
}

export async function getDocPage(source: string): Promise<DocPage> {
  return describeDoc(source)
}

/**
 * `docs/performance-measurements.md` is the measured-behaviour report. It is a first-class
 * section of the site rather than an ordinary guide because every claim on it is a measured
 * number, and the project treats that distinction as part of its contract.
 */
export async function getMeasurements(): Promise<DocSource & { title: string; description: string }> {
  const source = 'docs/performance-measurements.md'
  const { markdown } = await loadDoc(source)
  const { title, description } = extractHeadings(markdown)

  return { source, markdown, title, description }
}

export interface DebateTemplateInfo {
  /** Property name on `DebateTemplate`, e.g. `ArchitectureReview`. */
  name: string
  /** Display name derived from the property, e.g. `Architecture review`. */
  title: string
  /** Member roles in the order they are added. */
  members: string[]
  /** Debate strategy class, e.g. `CritiqueDebate`. */
  strategy: string
  /** `Standard` or `Strict` — the chairman's stance. */
  chairman: string
  maxRounds: number
  /** The persona/role prompt for the first member, trimmed. */
  summary: string
}

const MEMBER_RE = /\.AddMember\(\s*"[a-z0-9-]+"\s*,\s*provider\s*,\s*"([^"]+)"/g
const PERSONA_MEMBER_RE = /\.AddMember\(\s*"[a-z0-9-]+"\s*,\s*provider\s*,\s*"([^"]+)"\s*,\s*Persona\.(\w+)\s*\)/g
const STRATEGY_RE = /\.WithStrategy\(new (\w+)\(\)\)/
const CHAIRMAN_RE = /\.SetChairman\(Chairman\.Create(\w+)\("chairman"/
const MAX_ROUNDS_RE = /\.WithMaxRounds\((\d+)\)/
const CLASS_RE = /public sealed class (\w+)Template : DebateTemplateBase/g

/**
 * Read the built-in debate templates out of `DebateTemplate.cs`.
 *
 * Parsed rather than hand-written: a list on a website that disagrees with the code is the
 * exact failure this project documents elsewhere (see the measured-behaviour page, where a
 * documented claim was corrected to what the harness actually produced). The roles, strategy
 * and round count all come from the source that ships in the NuGet package.
 *
 * If `DebateTemplate.cs` ever moves or is refactored away from this shape, the parse yields
 * nothing and the templates page says so — instead of shipping a stale list.
 */
export async function getDebateTemplates(): Promise<DebateTemplateInfo[]> {
  const source = await readText('src', 'Delibera.Core', 'Templates', 'DebateTemplate.cs')

  const starts = [...source.matchAll(CLASS_RE)]
  if (starts.length === 0) return []

  return starts.map((match, index) => {
    const className = match[1] ?? ''
    const bodyStart = match.index ?? 0
    const bodyEnd = starts[index + 1]?.index ?? source.length
    const body = source.slice(bodyStart, bodyEnd)

    const members: string[] = []
    for (const member of body.matchAll(MEMBER_RE)) {
      if (member[1]) members.push(member[1])
    }

    const strategy = body.match(STRATEGY_RE)?.[1] ?? 'StandardDebate'
    const chairman = body.match(CHAIRMAN_RE)?.[1] ?? 'Standard'
    const maxRounds = Number(body.match(MAX_ROUNDS_RE)?.[1] ?? 4)

    // Prefer the first inline persona; fall back to naming the named Persona constants so
    // the card still says something for templates that reference shared personas.
    const inline = body.match(/\.AddMember\([^,]+,\s*provider\s*,\s*"[^"]+"\s*,\s*\n?\s*"""\n\s*([^\n]+)/)

    // The type predicate is what narrows `(string | undefined)[]` to `string[]`;
    // `.filter(Boolean)` leaves the union in place and `Intl.ListFormat` rejects it.
    const personas = [
      ...new Set([...body.matchAll(PERSONA_MEMBER_RE)].map((m) => m[2]).filter((name): name is string => Boolean(name))),
    ]

    const summary =
      inline?.[1]?.trim() ??
      (personas.length > 0 ? `Uses the shared ${new Intl.ListFormat('en').format(personas)} personas.` : '')

    return {
      name: className,
      title: humanize(className),
      members,
      strategy,
      chairman,
      maxRounds,
      summary,
    }
  })
}

/** `ArchitectureReview` -> `Architecture review`. */
function humanize(name: string): string {
  const spaced = name.replace(/([a-z0-9])([A-Z])/g, '$1 $2').toLowerCase()
  return spaced.charAt(0).toUpperCase() + spaced.slice(1)
}