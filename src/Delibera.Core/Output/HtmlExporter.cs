using System.Text;

namespace Delibera.Core.Output;

/// <summary>
///    Theme for the HTML export produced by <see cref="HtmlExporter"/>.
/// </summary>
public enum HtmlTheme
{
    /// <summary>Light background, dark text — default for printing and email.</summary>
    Light,

    /// <summary>Dark background, light text — default for developer consoles.</summary>
    Dark
}

/// <summary>
///    Options controlling the HTML export produced by <see cref="HtmlExporter"/>.
/// </summary>
public sealed class HtmlExportOptions
{
    /// <summary>Colour theme. Default is <see cref="HtmlTheme.Dark"/>.</summary>
    public HtmlTheme Theme { get; set; } = HtmlTheme.Dark;

    /// <summary>
    ///    When <c>true</c>, each round's participant responses are wrapped in a
    ///    <c>&lt;details&gt;</c> element so the page loads collapsed and the reader
    ///    can expand individual rounds. Default is <c>true</c>.
    /// </summary>
    public bool CollapsibleRounds { get; set; } = true;

    /// <summary>
    ///    Inline a small CSS block (no external stylesheet). When <c>false</c>,
    ///    a <c>&lt;link rel="stylesheet"&gt;</c> referencing <c>delibera.css</c>
    ///    is emitted instead — useful when the caller hosts the stylesheet themselves.
    ///    Default is <c>true</c> (self-contained file).
    /// </summary>
    public bool InlineCss { get; set; } = true;

    /// <summary>Optional page title. Defaults to <c>"Delibera Debate Result"</c>.</summary>
    public string Title { get; set; } = "Delibera Debate Result";
}

/// <summary>
///    Renders a <see cref="DebateResult"/> to a self-contained HTML document
///    with a styled transcript, collapsible rounds, and a printable layout.
///    Used by <see cref="DebateResult.ToHtml(HtmlExportOptions?)"/> and
///    <see cref="DebateResult.SaveToHtmlAsync(string, HtmlExportOptions?, CancellationToken)"/>.
/// </summary>
/// <remarks>
///    The exporter is intentionally dependency-free: it emits a small inline CSS
///    block (no Bootstrap/Tailwind/etc.) so the resulting file can be opened in any
///    browser, attached to an email, or rendered inside a Blazor <c>iframe</c>
///    without external network access.
/// </remarks>
public static class HtmlExporter
{
    /// <summary>
    ///    Renders <paramref name="result"/> as a self-contained HTML string.
    /// </summary>
    public static string ToHtml(DebateResult result, HtmlExportOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(result);
        options ??= new HtmlExportOptions();

        var sb = new StringBuilder(8192);
        sb.Append("<!DOCTYPE html>");
        sb.Append("<html lang=\"en\">");
        sb.Append("<head>");
        sb.Append("<meta charset=\"utf-8\"/>");
        sb.Append($"<title>{Escape(options.Title)} — {Escape(result.DebateId)}</title>");
        sb.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"/>");
        if (options.InlineCss)
            sb.Append(BuildCss(options.Theme));
        else
            sb.Append("<link rel=\"stylesheet\" href=\"delibera.css\"/>");
        sb.Append("</head>");
        sb.Append($"<body class=\"{(options.Theme == HtmlTheme.Dark ? "theme-dark" : "theme-light")}\">");

        // ── Header ──
        sb.Append("<header class=\"debate-header\">");
        sb.Append($"<h1>{Escape(options.Title)}</h1>");
        sb.Append("<div class=\"meta\">");
        sb.Append($"<span><strong>Debate ID:</strong> <code>{Escape(result.DebateId)}</code></span>");
        sb.Append($"<span><strong>Strategy:</strong> {Escape(result.StrategyName)}</span>");
        sb.Append($"<span><strong>Duration:</strong> {result.TotalDuration:mm\\:ss}</span>");
        sb.Append($"<span><strong>Participants:</strong> {result.Participants.Count}</span>");
        sb.Append($"<span><strong>Rounds:</strong> {result.Rounds.Count}</span>");
        sb.Append("</div>");
        sb.Append("</header>");

        // ── Input context ──
        sb.Append("<section class=\"context\">");
        sb.Append("<h2>Input Context</h2>");
        sb.Append("<dl>");
        sb.Append($"<dt>System Prompt</dt><dd>{Escape(result.Context.SystemPrompt)}</dd>");
        sb.Append($"<dt>User Prompt</dt><dd>{Escape(result.Context.UserPrompt)}</dd>");
        sb.Append("</dl>");
        if (result.Context.KnowledgeFiles is { Count: > 0 })
        {
            sb.Append("<p><strong>Knowledge Files:</strong> ");
            sb.Append(string.Join(", ", result.Context.KnowledgeFiles.Select(Escape)));
            sb.Append("</p>");
        }
        sb.Append("</section>");

        // ── Opening statement ──
        if (!string.IsNullOrWhiteSpace(result.OpeningStatement))
        {
            sb.Append("<section class=\"opening\">");
            sb.Append("<h2>Opening Statement (Chairman)</h2>");
            sb.Append("<blockquote>").Append(Escape(result.OpeningStatement)).Append("</blockquote>");
            sb.Append("</section>");
        }

        // ── Rounds ──
        sb.Append("<section class=\"rounds\">");
        sb.Append("<h2>Rounds</h2>");
        foreach (var round in result.Rounds)
        {
            var wrapperTag = options.CollapsibleRounds ? "details" : "section";
            var summaryTag = options.CollapsibleRounds ? "summary" : "h3";
            sb.Append($"<{wrapperTag} class=\"round\">");
            sb.Append($"<{summaryTag} class=\"round-title\">Round {round.RoundNumber}: {Escape(round.RoundName)}");
            sb.Append($" <span class=\"round-duration\">({round.Duration.TotalSeconds:F1}s)</span>");
            sb.Append($"</{summaryTag}>");

            if (!string.IsNullOrEmpty(round.Description))
                sb.Append($"<p class=\"round-desc\"><em>{Escape(round.Description)}</em></p>");

            // Knowledge interactions
            if (round.KnowledgeInteractions is { Count: > 0 })
            {
                sb.Append("<div class=\"knowledge\">");
                sb.Append("<h4>📚 Knowledge Keeper Interactions</h4>");
                foreach (var ki in round.KnowledgeInteractions)
                {
                    sb.Append("<div class=\"ki\">");
                    sb.Append($"<p><strong>Q:</strong> {Escape(ki.Query)}</p>");
                    sb.Append($"<p><strong>A:</strong> {Escape(ki.Answer)} <em>({ki.SourceChunks} chunks)</em></p>");
                    sb.Append("</div>");
                }
                sb.Append("</div>");
            }

            // Operator interactions
            if (round.OperatorInteractions is { Count: > 0 })
            {
                sb.Append("<div class=\"operator\">");
                sb.Append("<h4>🛠️ Operator Interactions</h4>");
                foreach (var oi in round.OperatorInteractions)
                {
                    var tools = oi.ToolCalls.Count > 0
                        ? string.Join(", ", oi.ToolCalls.Select(c => $"{c.ServerName}.{c.ToolName}"))
                        : "none";
                    sb.Append("<div class=\"oi\">");
                    sb.Append($"<p><strong>{Escape(oi.RequesterName)}</strong> requested: {Escape(oi.Task)}</p>");
                    sb.Append($"<p><strong>Tools:</strong> <code>{Escape(tools)}</code></p>");
                    sb.Append($"<p><strong>Answer:</strong> {Escape(oi.Answer)}{(oi.Compressed ? " <em>(compressed)</em>" : "")}</p>");
                    sb.Append("</div>");
                }
                sb.Append("</div>");
            }

            // Participant responses
            sb.Append("<div class=\"responses\">");
            foreach (var (member, response) in round.Responses)
            {
                sb.Append("<article class=\"response\">");
                sb.Append($"<h4>{Escape(member)}</h4>");
                sb.Append("<div class=\"response-body\">").Append(Escape(response)).Append("</div>");
                sb.Append("</article>");
            }
            sb.Append("</div>");

            sb.Append($"</{wrapperTag}>");
        }
        sb.Append("</section>");

        // ── Final verdict ──
        if (!string.IsNullOrWhiteSpace(result.FinalVerdict))
        {
            sb.Append("<section class=\"verdict\">");
            sb.Append("<h2>Final Verdict (Chairman)</h2>");
            sb.Append("<div class=\"verdict-body\">").Append(Escape(result.FinalVerdict)).Append("</div>");
            sb.Append("</section>");
        }

        // ── Token statistics ──
        if (result.TokenStats is not null)
        {
            sb.Append("<section class=\"stats\">");
            sb.Append("<h2>📊 Token Statistics</h2>");
            sb.Append("<table>");
            sb.Append("<thead><tr><th>Metric</th><th>Value</th></tr></thead>");
            sb.Append("<tbody>");
            sb.Append(Row("Original Tokens", result.TokenStats.TotalOriginalTokens));
            sb.Append(Row("Compressed Tokens", result.TokenStats.TotalCompressedTokens));
            sb.Append(Row("Response Tokens", result.TokenStats.TotalResponseTokens));
            sb.Append(Row("Tokens Saved", $"{result.TokenStats.TokensSaved:N0} ({result.TokenStats.SavedPercent:F1}%)"));
            sb.Append(Row("Compression Ratio", result.TokenStats.OverallCompressionRatio.ToString("P1")));
            sb.Append(Row("Grand Total", result.TokenStats.GrandTotal));
            sb.Append("</tbody></table>");
            sb.Append("</section>");
        }

        // ── Footer ──
        sb.Append("<footer>");
        sb.Append($"<p>Generated by <strong>Delibera</strong> at {result.CompletedAt?.ToString("yyyy-MM-dd HH:mm:ss UTC") ?? DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss UTC")}</p>");
        sb.Append("</footer>");

        sb.Append("</body></html>");
        return sb.ToString();
    }

    private static string Row(string label, object value) =>
        $"<tr><td>{Escape(label)}</td><td>{Escape(value.ToString() ?? "")}</td></tr>";

    private static string Escape(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            sb.Append(c switch
            {
                '<' => "&lt;",
                '>' => "&gt;",
                '&' => "&amp;",
                '"' => "&quot;",
                '\'' => "&#39;",
                _ => c
            });
        }
        return sb.ToString();
    }

    private static string BuildCss(HtmlTheme theme)
    {
        var (bg, fg, accent, muted, cardBg, border) = theme switch
        {
            HtmlTheme.Dark => (
                "#1a1a1a", "#e6e6e6", "#7ab7ff", "#888",
                "#242424", "#333"),
            _ => (
                "#ffffff", "#1a1a1a", "#0066cc", "#666",
                "#f7f7f7", "#ddd")
        };

        var sb = new StringBuilder(2048);
        sb.Append("<style>");
        sb.Append(":root { --bg:").Append(bg).Append("; --fg:").Append(fg)
          .Append("; --accent:").Append(accent).Append("; --muted:").Append(muted)
          .Append("; --card:").Append(cardBg).Append("; --border:").Append(border).Append("; }");
        sb.Append("body { background:var(--bg); color:var(--fg); font-family:system-ui,-apple-system,Segoe UI,Roboto,sans-serif;")
          .Append(" line-height:1.6; max-width:960px; margin:0 auto; padding:2rem; }");
        sb.Append("h1,h2,h3,h4 { color:var(--accent); }");
        sb.Append("a { color:var(--accent); }");
        sb.Append("code { background:var(--card); padding:0.1rem 0.35rem; border-radius:3px; font-size:0.9em; }");
        sb.Append("header.debate-header { border-bottom:2px solid var(--accent); padding-bottom:1rem; margin-bottom:1.5rem; }");
        sb.Append("header .meta { display:flex; flex-wrap:wrap; gap:1rem; color:var(--muted); font-size:0.9rem; }");
        sb.Append("section { margin:1.5rem 0; }");
        sb.Append("dl dt { font-weight:600; color:var(--accent); margin-top:0.75rem; }");
        sb.Append("dl dd { margin:0.25rem 0 0.5rem 1rem; }");
        sb.Append("blockquote { border-left:4px solid var(--accent); margin:0; padding:0.5rem 1rem; background:var(--card); }");
        sb.Append("details.round, section.round { background:var(--card); border:1px solid var(--border);")
          .Append(" border-radius:6px; padding:0.75rem 1rem; margin:0.75rem 0; }");
        sb.Append("summary.round-title, h3.round-title { cursor:pointer; color:var(--accent); font-size:1.1rem; }");
        sb.Append(".round-duration { color:var(--muted); font-size:0.85rem; }");
        sb.Append(".round-desc { color:var(--muted); }");
        sb.Append(".knowledge, .operator { background:var(--bg); border:1px solid var(--border);")
          .Append(" border-radius:4px; padding:0.5rem 0.75rem; margin:0.5rem 0; }");
        sb.Append(".response { border-left:3px solid var(--accent); padding:0.5rem 0.75rem; margin:0.5rem 0; }");
        sb.Append(".response-body { white-space:pre-wrap; }");
        sb.Append(".verdict { background:var(--card); border:2px solid var(--accent); border-radius:8px; padding:1rem 1.5rem; }");
        sb.Append(".verdict-body { white-space:pre-wrap; font-size:1.05rem; }");
        sb.Append("table { border-collapse:collapse; width:100%; }");
        sb.Append("th,td { border:1px solid var(--border); padding:0.4rem 0.7rem; text-align:left; }");
        sb.Append("th { background:var(--card); color:var(--accent); }");
        sb.Append("footer { margin-top:2rem; padding-top:1rem; border-top:1px solid var(--border); color:var(--muted); font-size:0.85rem; }");
        sb.Append("@media print {");
        sb.Append("  body { max-width:none; padding:0; }");
        sb.Append("  details { display:block; }");
        sb.Append("  summary { display:none; }");
        sb.Append("}");
        sb.Append("</style>");
        return sb.ToString();
    }
}