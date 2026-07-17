namespace Delibera.Server.Templates.Registry;

public interface ITemplateRegistry
{
   int Count { get; }
   bool Exists(string templateId);
   IServerTemplate? Get(string templateId);
   IEnumerable<IServerTemplate> GetAll();
}
