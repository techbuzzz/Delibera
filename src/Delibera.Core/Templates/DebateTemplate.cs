using Delibera.Core.Council;
using Delibera.Core.Debate;
using Delibera.Core.Interfaces;

namespace Delibera.Core.Templates;

/// <summary>
///    A fluent facade over <see cref="CouncilBuilder"/> that pre-configures a council
///    for a specific use case (participants, strategy, persona prompts). Templates are
///    ready-to-run starting points — every aspect can still be overridden via the
///    fluent <c>With*</c> methods before <see cref="DebateTemplateBase.Build"/> is called.
/// </summary>
/// <remarks>
///    <para>
///       Each template derives from <see cref="DebateTemplateBase"/> and only overrides
///       <see cref="ConfigureCore"/>. The base class handles question / provider /
///       max-rounds wiring and exposes the same fluent API as <see cref="ICouncilBuilder"/>,
///       so callers can refine a template without dropping to a raw builder:
///       <code>
/// var executor = DebateTemplate.ArchitectureReview
///     .WithQuestion("Should we migrate to event-driven architecture?")
///     .WithProvider(ollama)
///     .WithMaxRounds(4)
///     .Build();
///       </code>
///    </para>
///    <para>
///       Use <see cref="DebateTemplate.Custom"/> as an escape hatch when none of the
///       built-in templates fit — it returns a fresh <see cref="CouncilBuilder"/>.
///    </para>
/// </remarks>
public abstract class DebateTemplateBase
{
    private readonly CouncilBuilder _builder = new();
    private bool _configured;

    /// <summary>
    ///    The LLM provider used by every member of the template. Must be set via
    ///    <see cref="WithProvider"/> before <see cref="Build"/> is called.
    /// </summary>
    protected ILLMProvider? Provider { get; private set; }

    /// <summary>
    ///    Sets the question that the templated council will debate.
    /// </summary>
    public DebateTemplateBase WithQuestion(string question)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(question);
        _builder.WithUserPrompt(question);
        return this;
    }

    /// <summary>
    ///    Sets the LLM provider used by every member of the template.
    ///    Must be called before <see cref="Build"/>. Triggers
    ///    <see cref="ConfigureCore"/> immediately so subsequent fluent overrides
    ///    (max-rounds, temperature, system prompt, …) take precedence over the
    ///    template's defaults.
    /// </summary>
    public DebateTemplateBase WithProvider(ILLMProvider provider)
    {
       ArgumentNullException.ThrowIfNull(provider);
       Provider = provider;
       // Configure the template's preset members / strategy / chairman now so the
       // caller's later With* calls override the template defaults rather than vice-versa.
       if (!_configured)
       {
          ConfigureCore(_builder, provider);
          _configured = true;
       }
       return this;
    }

    /// <summary>Overrides the maximum number of debate rounds.</summary>
    public DebateTemplateBase WithMaxRounds(int maxRounds)
    {
        _builder.WithMaxRounds(maxRounds);
        return this;
    }

    /// <summary>Overrides the generation temperature.</summary>
    public DebateTemplateBase WithTemperature(float temperature)
    {
        _builder.WithTemperature(temperature);
        return this;
    }

    /// <summary>Forces every response into the specified human language.</summary>
    public DebateTemplateBase WithResponseLanguage(string? language)
    {
        _builder.WithResponseLanguage(language);
        return this;
    }

    /// <summary>Overrides the system prompt shared by all participants.</summary>
    public DebateTemplateBase WithSystemPrompt(string systemPrompt)
    {
        _builder.WithSystemPrompt(systemPrompt);
        return this;
    }

    /// <summary>Sets the output path for the result Markdown file.</summary>
    public DebateTemplateBase SaveResultTo(string outputPath)
    {
        _builder.SaveResultTo(outputPath);
        return this;
    }

    /// <summary>Enables OpenTelemetry-style observability (F-08).</summary>
    public DebateTemplateBase WithTelemetry(Telemetry.TelemetryOptions? options = null)
    {
        _builder.WithTelemetry(options);
        return this;
    }

    /// <summary>
    ///    Sets a hard wall-clock timeout for the debate (F-10b).
    /// </summary>
    public DebateTemplateBase WithTimeout(TimeSpan timeout)
    {
        _builder.WithTimeout(timeout);
        return this;
    }

    /// <summary>
    ///    Caps the maximum number of participants (F-10e). Templates pre-add a fixed
    ///    set, so this is mostly useful when callers add extra members on top.
    /// </summary>
    public DebateTemplateBase WithParticipantLimit(int maxParticipants)
    {
        _builder.WithParticipantLimit(maxParticipants);
        return this;
    }

    /// <summary>
    ///    Attaches a Knowledge Keeper built from a RAG provider (F-04 / existing).
    /// </summary>
    public DebateTemplateBase WithKnowledgeKeeper(IRagProvider ragProvider, string modelName, string collectionName = "council_knowledge")
    {
        if (Provider is null)
            throw new InvalidOperationException("Call WithProvider(...) before WithKnowledgeKeeper(...).");
        _builder.WithKnowledgeKeeper(ragProvider, modelName, Provider, collectionName);
        return this;
    }

    /// <summary>
    ///    Allows the template to add extra members on top of the preset ones.
    ///    Useful for callers who want to extend a template without dropping to a raw builder.
    /// </summary>
    public DebateTemplateBase AddMember(string modelName, string role, string persona)
    {
        if (Provider is null)
            throw new InvalidOperationException("Call WithProvider(...) before AddMember(...).");
        _builder.AddMember(modelName, Provider, role, persona);
        return this;
    }

    /// <summary>
    ///    Replaces the Chairman. The template's default chairman (if any) is discarded.
    /// </summary>
    public DebateTemplateBase WithChairman(string modelName, string? persona = null)
    {
        if (Provider is null)
            throw new InvalidOperationException("Call WithProvider(...) before WithChairman(...).");
        _builder.SetChairman(modelName, Provider, persona);
        return this;
    }

    /// <summary>
    ///    Accesses the underlying <see cref="CouncilBuilder"/> for advanced customisation
    ///    that the template facade does not expose directly. Use sparingly — the template's
    ///    invariants (strategy, member roles) may be lost. <see cref="WithProvider"/> must
    ///    have been called first.
    /// </summary>
    public CouncilBuilder Advanced(Action<CouncilBuilder> configure)
    {
       ArgumentNullException.ThrowIfNull(configure);
       if (!_configured)
          throw new InvalidOperationException(
             "Call WithProvider(...) before Advanced(...) so the template can be configured first.");
       configure(_builder);
       return _builder;
    }

    /// <summary>
    ///    Builds the configured <see cref="CouncilExecutor"/>. Throws if
    ///    <see cref="WithProvider"/> was not called.
    /// </summary>
    public CouncilExecutor Build()
    {
       if (Provider is null)
          throw new InvalidOperationException(
             "A template requires an ILLMProvider. Call WithProvider(...) before Build().");
       // ConfigureCore was already invoked by WithProvider; later fluent overrides
       // have already been applied to _builder.
       return _builder.Build();
    }

    /// <summary>
    ///    Implemented by each concrete template to add members, set the strategy,
    ///    and configure the chairman. Called once, eagerly, inside
    ///    <see cref="WithProvider"/> so subsequent fluent overrides take precedence.
    /// </summary>
    /// <param name="builder">The underlying builder to configure.</param>
    /// <param name="provider">The provider set via <see cref="WithProvider"/>.</param>
    protected abstract void ConfigureCore(CouncilBuilder builder, ILLMProvider provider);
}

/// <summary>
///    Built-in debate templates — ready-to-run council configurations for common
///    use cases. Each property returns a fresh template instance that can be refined
///    via the fluent <c>With*</c> methods before <see cref="DebateTemplateBase.Build"/>.
/// </summary>
/// <remarks>
///    <para>
///       <b>Available templates</b>:
///       <list type="table">
///          <item><term><see cref="ArchitectureReview"/></term><description>Architect, SecurityExpert, PerfEngineer — CritiqueDebate — system design.</description></item>
///          <item><term><see cref="RiskAssessment"/></term><description>Optimist, Pessimist, Realist, RiskManager — ConsensusDebate — business risks.</description></item>
///          <item><term><see cref="CodeReview"/></term><description>Reviewer, Defender, QA, TechLead — CritiqueDebate — PR analysis.</description></item>
///          <item><term><see cref="ProductDecision"/></term><description>PM, TechLead, UXDesigner — StandardDebate — feature prioritisation.</description></item>
///          <item><term><see cref="SecurityAudit"/></term><description>RedTeam, BlueTeam, Auditor — CritiqueDebate — threat modelling.</description></item>
///          <item><term><see cref="DataArchitecture"/></term><description>DataEngineer, DBA, MLEngineer — ConsensusDebate — data platform.</description></item>
///       </list>
///    </para>
///    <para>
///       Use <see cref="Custom"/> as an escape hatch when no template fits — it returns
///       a fresh <see cref="CouncilBuilder"/> with no presets.
///    </para>
/// </remarks>
public static class DebateTemplate
{
    /// <summary>
    ///    Architecture review council — three specialist roles that critique each
    ///    other's positions on a system design question. Uses
    ///    <see cref="CritiqueDebate"/> so every position is challenged before the
    ///    Chairman synthesises a verdict.
    /// </summary>
    public static ArchitectureReviewTemplate ArchitectureReview => new();

    /// <summary>
    ///    Risk assessment council — four roles covering the optimism/pessimism spectrum
    ///    plus a dedicated Risk Manager. Uses <see cref="ConsensusDebate"/> so the
    ///    council converges on a balanced risk verdict.
    /// </summary>
    public static RiskAssessmentTemplate RiskAssessment => new();

    /// <summary>
    ///    Code review council — Reviewer, Defender, QA, TechLead. Uses
    ///    <see cref="CritiqueDebate"/> so every position is challenged.
    /// </summary>
    public static CodeReviewTemplate CodeReview => new();

    /// <summary>
    ///    Product decision council — PM, TechLead, UXDesigner. Uses
    ///    <see cref="StandardDebate"/> for a balanced 4-round flow.
    /// </summary>
    public static ProductDecisionTemplate ProductDecision => new();

    /// <summary>
    ///    Security audit council — RedTeam, BlueTeam, Auditor. Uses
    ///    <see cref="CritiqueDebate"/> for adversarial threat modelling.
    /// </summary>
    public static SecurityAuditTemplate SecurityAudit => new();

    /// <summary>
    ///    Data architecture council — DataEngineer, DBA, MLEngineer. Uses
    ///    <see cref="ConsensusDebate"/> for a convergent data-platform verdict.
    /// </summary>
    public static DataArchitectureTemplate DataArchitecture => new();

    /// <summary>
    ///    Escape hatch — returns a fresh, unconfigured <see cref="CouncilBuilder"/>.
    ///    Use when no built-in template fits and full control is needed.
    /// </summary>
    public static CouncilBuilder Custom() => new();
}

/// <summary>
///    Architecture review council template. Three specialists (Architect,
/// SecurityExpert, PerfEngineer) debate a system design question using the
/// adversarial <see cref="CritiqueDebate"/> strategy.
/// </summary>
public sealed class ArchitectureReviewTemplate : DebateTemplateBase
{
    /// <inheritdoc />
    protected override void ConfigureCore(CouncilBuilder builder, ILLMProvider provider)
    {
        builder
            .AddMember("architect", provider, "Architect",
                """
                You are a senior software architect. Focus on structural soundness,
                separation of concerns, and long-term maintainability. When critiquing
                others, point out architectural smells and propose concrete alternatives.
                """)
            .AddMember("security-expert", provider, "SecurityExpert",
                """
                You are a security expert. Examine the proposal for attack surface,
                threat vectors, and least-privilege violations. When critiquing others,
                surface security risks they overlooked.
                """)
            .AddMember("perf-engineer", provider, "PerfEngineer",
                """
                You are a performance engineer. Focus on latency, throughput, resource
                utilisation, and scalability. When critiquing others, point out
                bottlenecks and propose measurement plans.
                """)
            .SetChairman(Chairman.CreateStandard("chairman", provider))
            .WithStrategy(new CritiqueDebate())
            .WithSystemPrompt("You are participating in an architecture review council.")
            .WithMaxRounds(4);
    }
}

/// <summary>
///    Risk assessment council template. Four roles (Optimist, Pessimist, Realist,
/// RiskManager) deliberate over a business risk question using
/// <see cref="ConsensusDebate"/> so the council converges on a balanced verdict.
/// </summary>
public sealed class RiskAssessmentTemplate : DebateTemplateBase
{
    /// <inheritdoc />
    protected override void ConfigureCore(CouncilBuilder builder, ILLMProvider provider)
    {
        builder
            .AddMember("optimist", provider, "Optimist", Persona.CautiousOptimist)
            .AddMember("pessimist", provider, "Pessimist", Persona.DevilsAdvocate)
            .AddMember("realist", provider, "Realist", Persona.Pragmatist)
            .AddMember("risk-manager", provider, "RiskManager", Persona.RiskManager)
            .SetChairman(Chairman.CreateStandard("chairman", provider))
            .WithStrategy(new ConsensusDebate())
            .WithSystemPrompt("You are participating in a business risk assessment council.")
            .WithMaxRounds(4);
    }
}

/// <summary>
///    Code review council template. Reviewer, Defender, QA, TechLead debate a PR
/// or code change using the adversarial <see cref="CritiqueDebate"/> strategy.
/// </summary>
public sealed class CodeReviewTemplate : DebateTemplateBase
{
    /// <inheritdoc />
    protected override void ConfigureCore(CouncilBuilder builder, ILLMProvider provider)
    {
        builder
            .AddMember("reviewer", provider, "Reviewer",
                """
                You are a meticulous code reviewer. Surface bugs, edge cases, naming
                issues, and deviations from team conventions. Cite specific line ranges
                when possible.
                """)
            .AddMember("defender", provider, "Defender",
                """
                You are the code author's defender. Argue why the change is correct,
                necessary, and minimal. Push back on review comments that are stylistic
                rather than substantive.
                """)
            .AddMember("qa", provider, "QA",
                """
                You are a QA engineer. Focus on test coverage, regressions, and failure
                modes. Identify missing test cases and propose concrete assertions.
                """)
            .AddMember("tech-lead", provider, "TechLead",
                """
                You are the tech lead. Weigh long-term impact, alignment with the
                roadmap, and team capacity. Resolve disagreements between Reviewer and
                Defender with concrete recommendations.
                """)
            .SetChairman(Chairman.CreateStandard("chairman", provider))
            .WithStrategy(new CritiqueDebate())
            .WithSystemPrompt("You are participating in a pull-request review council.")
            .WithMaxRounds(4);
    }
}

/// <summary>
///    Product decision council template. PM, TechLead, UXDesigner deliberate over
/// a feature prioritisation question using the standard 4-round strategy.
/// </summary>
public sealed class ProductDecisionTemplate : DebateTemplateBase
{
    /// <inheritdoc />
    protected override void ConfigureCore(CouncilBuilder builder, ILLMProvider provider)
    {
        builder
            .AddMember("pm", provider, "PM",
                """
                You are a product manager. Frame the question in terms of user value,
                business impact, and roadmap fit. Distinguish must-haves from nice-to-haves.
                """)
            .AddMember("tech-lead", provider, "TechLead",
                """
                You are a tech lead. Translate product asks into engineering effort,
                risk, and dependencies. Flag speculative complexity.
                """)
            .AddMember("ux-designer", provider, "UXDesigner",
                """
                You are a UX designer. Advocate for the end-user's mental model,
                accessibility, and clarity. Surface friction the engineering and product
                perspectives tend to overlook.
                """)
            .SetChairman(Chairman.CreateStandard("chairman", provider))
            .WithStrategy(new StandardDebate())
            .WithSystemPrompt("You are participating in a product decision council.")
            .WithMaxRounds(4);
    }
}

/// <summary>
///    Security audit council template. RedTeam, BlueTeam, Auditor engage in
/// adversarial threat modelling using the <see cref="CritiqueDebate"/> strategy.
/// </summary>
public sealed class SecurityAuditTemplate : DebateTemplateBase
{
    /// <inheritdoc />
    protected override void ConfigureCore(CouncilBuilder builder, ILLMProvider provider)
    {
        builder
            .AddMember("red-team", provider, "RedTeam",
                """
                You are a red-team operator. Enumerate attack paths, exploitation
                techniques, and abuse cases. Assume the adversary is sophisticated and
                motivated.
                """)
            .AddMember("blue-team", provider, "BlueTeam",
                """
                You are a blue-team defender. For each red-team proposal, identify
                detection, prevention, and response controls. Quantify residual risk.
                """)
            .AddMember("auditor", provider, "Auditor",
                """
                You are a compliance auditor. Map the discussion to common frameworks
                (NIST, ISO 27001, SOC 2). Flag missing evidence and procedural gaps.
                """)
            .SetChairman(Chairman.CreateStrict("chairman", provider))
            .WithStrategy(new CritiqueDebate())
            .WithSystemPrompt("You are participating in a security audit council.")
            .WithMaxRounds(4);
    }
}

/// <summary>
///    Data architecture council template. DataEngineer, DBA, MLEngineer deliberate
/// over a data-platform question using the consensus strategy.
/// </summary>
public sealed class DataArchitectureTemplate : DebateTemplateBase
{
    /// <inheritdoc />
    protected override void ConfigureCore(CouncilBuilder builder, ILLMProvider provider)
    {
        builder
            .AddMember("data-engineer", provider, "DataEngineer",
                """
                You are a data engineer. Focus on pipelines, batching/streaming trade-offs,
                schema evolution, and operational complexity.
                """)
            .AddMember("dba", provider, "DBA",
                """
                You are a DBA. Focus on storage layout, indexing, query plans, vacuum /
                compaction, and capacity planning. Surface operational risks.
                """)
            .AddMember("ml-engineer", provider, "MLEngineer",
                """
                You are an ML engineer. Focus on feature availability, lineage, training
                /serving skew, and reproducibility. Push back on proposals that block
                iteration speed.
                """)
            .SetChairman(Chairman.CreateStandard("chairman", provider))
            .WithStrategy(new ConsensusDebate())
            .WithSystemPrompt("You are participating in a data architecture council.")
            .WithMaxRounds(4);
    }
}