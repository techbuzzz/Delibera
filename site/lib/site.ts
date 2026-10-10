/**
 * Repository-wide constants for the site.
 *
 * Every external URL is derived from `REPO_OWNER`/`REPO_NAME` instead of being written
 * out per link: the site is deployed from a fork-friendly workflow, and a hardcoded
 * `techbuzzz/...` in 20 places is 20 places to fix when the repo moves.
 */
export const REPO_OWNER = 'techbuzzz'
export const REPO_NAME = 'Delibera'

/** Public origin of the deployed site. Used for canonical URLs and OpenGraph tags. */
export const SITE_URL = `https://${REPO_OWNER}.github.io`

/** Markdown documentation and release notes live here. */
export const DOCS_DIR = 'docs'

/**
 * The NuGet packages that ship from this repository. Kept as data rather than markup so
 * the install block and the package grid cannot drift apart.
 */
export const NUGET_PACKAGES = [
  {
    id: 'Delibera.Core',
    description: 'The framework — councils, chairman, RAG, compression, CLI, gRPC.',
  },
  {
    id: 'Delibera.Server',
    description: 'ASP.NET Core 10 Minimal API host: REST, SSE streaming, MCP.',
  },
  {
    id: 'Delibera.Redis',
    description: 'Distributed debates plus a shared result cache over Redis.',
  },
] as const

/** Container images published on every `v*` tag. */
export const DOCKER_IMAGES = [
  {
    id: 'delibera-server',
    hub: 'https://hub.docker.com/r/techbuzzz/delibera-server',
    description: 'The API host, with the Web UI BFF alongside it.',
  },
  {
    id: 'delibera-webui',
    hub: 'https://hub.docker.com/r/techbuzzz/delibera-webui',
    description: 'Nuxt 4 browser front end for the debate workflow.',
  },
] as const

/** A link to a file as GitHub will render it, anchored to a branch or a tag. */
export function githubBlob(repoPath: string, ref = 'main'): string {
  return `https://github.com/${REPO_OWNER}/${REPO_NAME}/blob/${ref}/${repoPath.replace(/^\/+/, '')}`
}

export function githubTree(repoPath: string, ref = 'main'): string {
  return `https://github.com/${REPO_OWNER}/${REPO_NAME}/tree/${ref}/${repoPath.replace(/^\/+/, '')}`
}

export const REPO_URL = `https://github.com/${REPO_OWNER}/${REPO_NAME}`
export const ISSUES_URL = `${REPO_URL}/issues`
export const RELEASES_URL = `${REPO_URL}/releases/latest`
export const NUGET_URL = (id: string) => `https://www.nuget.org/packages/${id}`