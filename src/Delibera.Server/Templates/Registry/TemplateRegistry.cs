using Delibera.Server.Templates.Governance;

namespace Delibera.Server.Templates.Registry;

/// <summary>
/// In-process registry of all server-side council templates.
/// New templates are registered here; the DI container provides this as a singleton.
/// </summary>
public sealed class TemplateRegistry : ITemplateRegistry
{
    private readonly Dictionary<string, IServerTemplate> _templates;

    public TemplateRegistry()
    {
        // Register all built-in templates
        var builtIn = new IServerTemplate[]
        {
            new RiskCommitteeTemplate(),
            new ArchitectureDecisionTemplate(),
        };
        _templates = builtIn.ToDictionary(t => t.TemplateId, StringComparer.OrdinalIgnoreCase);
    }

    public int  Count                      => _templates.Count;
    public bool Exists(string id)          => _templates.ContainsKey(id);
    public IServerTemplate? Get(string id) => _templates.GetValueOrDefault(id);
    public IEnumerable<IServerTemplate> GetAll() => _templates.Values;
}
