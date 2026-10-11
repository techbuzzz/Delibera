import type { Metadata } from 'next'
import { getVersion, getDebateTemplates } from '@/lib/content'
import { templateUsageCode } from '@/lib/template-code'
import { PageShell, PageHead } from '@/components/layout'
import { Reveal } from '@/components/reveal'
import { CodeBlock } from '@/components/code-block'
import { githubBlob } from '@/lib/site'

export const metadata: Metadata = {
  title: 'Debate templates',
  description:
    'The built-in Delibera councils — roles, strategy and round count, parsed from DebateTemplate.cs.',
}

/**
 * Built-in debate templates, read out of `DebateTemplate.cs`.
 *
 * The roles, strategy, chairman stance and round count on this page are extracted from the
 * source that ships inside `Delibera.Core`. A hand-written list would be a second place to
 * update every time a template changes — and this project has a documented history of
 * claims drifting from what the code actually does.
 *
 * Each template card carries its own ready-to-use C# snippet with a copy button, so a
 * visitor can go from "this council looks useful" to "running it" in one click.
 */
export default async function TemplatesPage() {
  const version = await getVersion()
  const templates = await getDebateTemplates()

  return (
    <PageShell version={version} current="/templates/">
      <PageHead
        title="Debate templates"
        description="Ready-to-run councils. Pick one, override anything through the fluent API, and build."
      />

      <section className="band">
        <div className="wrap">
          <p className="band__hint">
            Each template is a pre-configured <code>CouncilBuilder</code>: participants, strategy
            and persona prompts are set for you, and every <code>With*</code> call still applies.
            Every card has a copy-ready snippet.
          </p>

          {templates.length === 0 ? (
            <div className="callout">
              <p>
                No templates were found in <code>src/Delibera.Core/Templates/DebateTemplate.cs</code>.
                The page says so rather than showing a stale list — see{' '}
                <a href={githubBlob('src/Delibera.Core/Templates/DebateTemplate.cs')}>the source</a>.
              </p>
            </div>
          ) : (
            <div className="tpl-grid">
              {templates.map((template, index) => (
                <Reveal key={template.name} delay={(index % 3 + 1) as 1 | 2 | 3}>
                  <article className="tpl">
                    <h3>{template.name}</h3>
                    <div className="role-list">
                      {template.members.map((member) => (
                        <span key={member} className="pill">
                          {member}
                        </span>
                      ))}
                    </div>
                    <dl>
                      <dt>Strategy</dt>
                      <dd>{template.strategy}</dd>
                      <dt>Chairman</dt>
                      <dd>{template.chairman}</dd>
                      <dt>Rounds</dt>
                      <dd>{template.maxRounds}</dd>
                    </dl>
                    {template.summary ? <p className="summary">{template.summary}</p> : null}

                    <div className="tpl__code">
                      <CodeBlock code={templateUsageCode(template.name, template.maxRounds)} />
                    </div>
                  </article>
                </Reveal>
              ))}
            </div>
          )}

          <Reveal>
            <h2 className="band__title" style={{ marginTop: 48 }}>
              Using one
            </h2>
          </Reveal>
          <Reveal delay={1}>
            <div style={{ maxWidth: 'var(--measure)' }}>
              <CodeBlock
                code={`var executor = DebateTemplate.${templates[0]?.name ?? 'Custom'}()
    .WithQuestion("Should we migrate to event-driven architecture?")
    .WithProvider(provider)
    .WithMaxRounds(4)
    .Build();

var result = await executor.ExecuteAsync(cancellationToken);`}
              />
            </div>
          </Reveal>

          <Reveal delay={2}>
            <p className="band__hint" style={{ marginTop: 20 }}>
              <code>DebateTemplate.Custom()</code> is the escape hatch — it returns a fresh{' '}
              <code>CouncilBuilder</code> with no presets. Full API:{' '}
              <a href={githubBlob('docs/QuickStart.md')}>Quick Start</a>.
            </p>
          </Reveal>
        </div>
      </section>
    </PageShell>
  )
}
