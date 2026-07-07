using Delibera.Server.Api.Contracts;
using Delibera.Server.Templates.Registry;

namespace Delibera.Server.Api.Endpoints;

public static class TemplateEndpoints
{
    public static IEndpointRouteBuilder MapTemplateEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/templates")
            .WithTags("Templates")
            .WithOpenApi();

        group.MapGet("/", ListTemplates)
            .WithName("ListTemplates")
            .WithSummary("Return all registered council templates.");

        group.MapGet("/{id}", GetTemplate)
            .WithName("GetTemplate")
            .WithSummary("Return details for a single template.")
            .Produces<TemplateDto>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        return routes;
    }

    private static Ok<TemplateDto[]> ListTemplates(ITemplateRegistry registry)
        => TypedResults.Ok(registry.GetAll().Select(ToDto).ToArray());

    private static Results<Ok<TemplateDto>, NotFound> GetTemplate(
        string id, ITemplateRegistry registry)
    {
        var t = registry.Get(id);
        return t is null ? TypedResults.NotFound() : TypedResults.Ok(ToDto(t));
    }

    private static TemplateDto ToDto(IServerTemplate t)
        => new()
        {
            TemplateId       = t.TemplateId,
            DisplayName      = t.DisplayName,
            Description      = t.Description,
            Strategy         = t.Strategy,
            DefaultMaxRounds = t.DefaultMaxRounds,
            MemberRoles      = t.MemberRoles,
            VotingStrategy   = t.VotingStrategy,
            RagEnabled       = t.RagEnabled,
            OperatorEnabled  = t.OperatorEnabled,
        };
}
