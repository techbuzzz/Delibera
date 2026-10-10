import type { Metadata } from 'next'
import { getVersion, getMeasurements } from '@/lib/content'
import { buildRouteIndex, MEASUREMENTS_ROUTE } from '@/lib/routes'
import { renderMarkdown } from '@/lib/markdown'
import { Breadcrumbs, PageHead, PageShell } from '@/components/layout'
import { DocLayout } from '@/components/markdown'
import { githubBlob } from '@/lib/site'

export const metadata: Metadata = {
  title: 'Measured behaviour',
  description:
    'Latency, throughput and token costs for Delibera, measured against a live provider with the harness that produced them.',
}

/**
 * The measured-behaviour report gets its own top-level page rather than living under /docs.
 *
 * The project treats "measured" as a claim with obligations attached — numbers are reported
 * as observed, and a documented claim that measurement contradicted is flagged rather than
 * quietly updated. Giving it a distinct URL keeps that contract visible.
 */
export default async function MeasurementsPage() {
  const version = await getVersion()
  const report = await getMeasurements()

  const [routes] = await Promise.all([buildRouteIndex()])
  const { html, headings } = await renderMarkdown(report.markdown, {
    source: report.source,
    routes,
    stripPreamble: true,
  })

  return (
    <PageShell version={version} current="/measurements/">
      {/* No visible description: the report opens with that same sentence, and showing it
          twice reads as a rendering bug. It still feeds the page metadata below. */}
      <PageHead title={report.title || 'Measured behaviour'} />
      <Breadcrumbs items={[{ href: '/', label: 'Home' }, { label: 'Measurements' }]} />

      <DocLayout html={html} headings={headings} />

      <div className="wrap" style={{ paddingBottom: 48, fontSize: '0.875rem' }}>
        <p>
          Source:{' '}
          <a href={githubBlob(report.source)}>
            <code>{report.source}</code>
          </a>{' '}
          on GitHub. Re-run the harnesses described at the bottom of the report to reproduce
          these numbers. Route: <code>{MEASUREMENTS_ROUTE}</code>
        </p>
      </div>
    </PageShell>
  )
}