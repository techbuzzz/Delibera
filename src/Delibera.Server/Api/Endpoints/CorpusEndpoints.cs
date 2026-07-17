using Delibera.Server.Api.Contracts;
using Delibera.Server.Services;

namespace Delibera.Server.Api.Endpoints;

public static class CorpusEndpoints
{
   public static IEndpointRouteBuilder MapCorpusEndpoints(this IEndpointRouteBuilder routes)
   {
      var group = routes.MapGroup("/corpora")
         .WithTags("Corpora")
         .WithOpenApi();

      group.MapGet("/", ListCorpora)
         .WithName("ListCorpora")
         .WithSummary("List all available RAG corpora.");

      group.MapPost("/", CreateCorpus)
         .WithName("CreateCorpus")
         .WithSummary("Create a new RAG corpus (vector store collection).")
         .Produces<CorpusDto>(StatusCodes.Status201Created);

      group.MapPost("/{id}/documents", IndexDocument)
         .WithName("IndexDocument")
         .WithSummary("Index a text document into a corpus.")
         .Produces<DocumentDto>(StatusCodes.Status201Created)
         .ProducesProblem(StatusCodes.Status404NotFound);

      group.MapGet("/{id}/documents", ListDocuments)
         .WithName("ListDocuments")
         .WithSummary("List documents indexed in a corpus.")
         .Produces<DocumentDto[]>()
         .ProducesProblem(StatusCodes.Status404NotFound);

      group.MapDelete("/{id}/documents/{docId}", DeleteDocument)
         .WithName("DeleteDocument")
         .WithSummary("Remove a document from a corpus.")
         .Produces(StatusCodes.Status204NoContent);

      return routes;
   }

   private static Ok<CorpusDto[]> ListCorpora(ICorpusService svc)
      => TypedResults.Ok(svc.ListCorpora().ToArray());

   private static Created<CorpusDto> CreateCorpus(
      CreateCorpusRequest req, ICorpusService svc)
   {
      var dto = svc.CreateCorpus(req);
      return TypedResults.Created($"/api/v1/corpora/{dto.CorpusId}", dto);
   }

   private static async Task<Results<Created<DocumentDto>, NotFound>>
      IndexDocument(
         string id, IndexDocumentRequest req,
         ICorpusService svc, CancellationToken ct)
   {
      var dto = await svc.IndexDocumentAsync(id, req, ct);
      return dto is null
         ? TypedResults.NotFound()
         : TypedResults.Created(
            $"/api/v1/corpora/{id}/documents/{dto.DocumentId}", dto);
   }

   private static Results<Ok<DocumentDto[]>, NotFound>
      ListDocuments(string id, ICorpusService svc)
   {
      var docs = svc.ListDocuments(id);
      return docs is null
         ? TypedResults.NotFound()
         : TypedResults.Ok(docs.ToArray());
   }

   private static NoContent DeleteDocument(
      string id, string docId, ICorpusService svc)
   {
      svc.DeleteDocument(id, docId);
      return TypedResults.NoContent();
   }
}
