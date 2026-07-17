using Delibera.Server.Api.Contracts;
using Delibera.Server.Templates.Registry;
using FluentValidation;

namespace Delibera.Server.Api.Validators;

public sealed class CreateDebateRequestValidator : AbstractValidator<CreateDebateRequest>
{
   public CreateDebateRequestValidator(ITemplateRegistry registry)
   {
      RuleFor(x => x.TemplateId)
         .NotEmpty()
         .Must(id => registry.Exists(id))
         .WithMessage(x => $"Template '{x.TemplateId}' is not registered.");

      RuleFor(x => x.Question)
         .NotEmpty()
         .MaximumLength(4096)
         .WithMessage("Question must be between 1 and 4096 characters.");

      When(x => x.Options != null, () =>
      {
         RuleFor(x => x.Options!.MaxRounds)
            .InclusiveBetween(1, 20)
            .When(x => x.Options?.MaxRounds.HasValue == true);

         RuleFor(x => x.Options!.Temperature)
            .InclusiveBetween(0f, 2f)
            .When(x => x.Options?.Temperature.HasValue == true);
      });
   }
}
