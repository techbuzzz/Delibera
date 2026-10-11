/**
 * Generate a ready-to-use C# code snippet for a debate template.
 *
 * The snippet shows the minimal viable usage: create the template, set a question
 * and provider, build, and execute. Matches the fluent API in `CouncilBuilder`.
 *
 * Kept in `lib/` (not in a client component) so server components can call it
 * during static rendering.
 */
export function templateUsageCode(templateName: string, maxRounds: number): string {
  return `var executor = DebateTemplate.${templateName}()
    .WithQuestion("Your question here")
    .WithProvider(provider)
    .WithMaxRounds(${maxRounds})
    .Build();

var result = await executor.ExecuteAsync(cancellationToken);`
}
