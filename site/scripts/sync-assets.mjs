// Copies the repository's brand images into `public/img`.
//
// The banner is one file with two consumers - the GitHub README and the website - and
// duplicating a binary in git is how they drift apart. `public/` is Next's only static
// asset root, so the copy has to happen before every dev/build; that is what the
// `predev` / `prebuild` npm hooks are for. The destination is gitignored.
import { copyFileSync, mkdirSync, readdirSync } from 'node:fs'
import { dirname, join } from 'node:path'
import { fileURLToPath } from 'node:url'

const siteRoot = dirname(dirname(fileURLToPath(import.meta.url)))
const sourceDir = join(siteRoot, '..', 'img')
const targetDir = join(siteRoot, 'public', 'img')

mkdirSync(targetDir, { recursive: true })

const copied = []
for (const entry of readdirSync(sourceDir, { withFileTypes: true })) {
  if (!entry.isFile() || !/\.png$/i.test(entry.name)) continue
  copyFileSync(join(sourceDir, entry.name), join(targetDir, entry.name))
  copied.push(entry.name)
}

console.log(`[sync-assets] ${copied.length} image(s) -> public/img: ${copied.join(', ')}`)