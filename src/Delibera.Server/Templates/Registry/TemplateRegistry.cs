using Delibera.Server.Templates.Engineering;
using Delibera.Server.Templates.Governance;
using Delibera.Server.Templates.Legal;
using Delibera.Server.Templates.Product;

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
         // Vertical 1 — Enterprise Decision Support & Governance
         new RiskCommitteeTemplate(),
         new ArchitectureDecisionTemplate(),

         // Vertical 2 — Software Engineering & Code Review
         new CodeReviewTemplate(),

         // Vertical 3 — Requirements Engineering & Product Discovery
         new RequirementsReviewTemplate(),

         // Vertical 4 — Legal / Policy / Compliance
         new LegalContractReviewTemplate(),
      };
      _templates = builtIn.ToDictionary(t => t.TemplateId, StringComparer.OrdinalIgnoreCase);
   }

   public int Count => _templates.Count;
   public bool Exists(string id) => _templates.ContainsKey(id);
   public IServerTemplate? Get(string id) => _templates.GetValueOrDefault(id);
   public IEnumerable<IServerTemplate> GetAll() => _templates.Values;
}
