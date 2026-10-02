using Delibera.Server.Api.Contracts;
using FluentValidation;

namespace Delibera.Server.Api.Validators;

/// <summary>
///    Validates <c>POST /api/v1/corpora</c>.
/// </summary>
/// <remarks>
///    <c>VectorStore</c> is intentionally not validated: <see cref="CorpusService" /> never
///    reads it, so rejecting an unknown value would be enforcing a contract the server does
///    not implement. Making the field meaningful is tracked as its own task.
/// </remarks>
public sealed class CreateCorpusRequestValidator : AbstractValidator<CreateCorpusRequest>
{
   public CreateCorpusRequestValidator()
   {
      RuleFor(x => x.Name)
         .NotEmpty()
         .MaximumLength(200)
         .WithMessage("A corpus name is required and must be at most 200 characters.");

      RuleFor(x => x.Description)
         .MaximumLength(2000)
         .When(x => x.Description is not null);
   }
}

/// <summary>
///    Validates <c>POST /api/v1/corpora/{id}/documents</c>.
/// </summary>
/// <remarks>
///    <c>Content</c> is required: <c>CorpusService.EstimateChunks</c> splits it without a
///    null check, so a missing body produced a <c>NullReferenceException</c> and a 500. The
///    generous upper bound is a safety valve against a single request carrying an unbounded
///    payload, not a product limit.
/// </remarks>
public sealed class IndexDocumentRequestValidator : AbstractValidator<IndexDocumentRequest>
{
   /// <summary>Maximum accepted document size, in characters.</summary>
   public const int MaxContentLength = 2_000_000;

   public IndexDocumentRequestValidator()
   {
      RuleFor(x => x.Content)
         .NotEmpty()
         .MaximumLength(MaxContentLength)
         .WithMessage($"Document content is required and must be at most {MaxContentLength} characters.");

      RuleFor(x => x.Title)
         .MaximumLength(500)
         .When(x => x.Title is not null);
   }
}
