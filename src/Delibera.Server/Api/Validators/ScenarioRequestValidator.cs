namespace Delibera.Server.Api.Validators;

/// <summary>
///    Validates <c>POST /api/v1/scenarios</c>.
/// </summary>
/// <remarks>
///    This contract previously had no validator at all, so a request missing
///    <c>Members</c> reached <c>ScenarioBuilder</c> and came back as a 500 rather than a 400.
///    <para>
///       <c>Strategy</c> is deliberately <b>not</b> restricted: the documented and tested
///       behaviour for an unknown strategy is a graceful fallback to "Standard", and a
///       validator here would turn that into a rejection.
///    </para>
/// </remarks>
public sealed class ScenarioRequestValidator : AbstractValidator<ScenarioRequest>
{
   public ScenarioRequestValidator()
   {
      RuleFor(x => x.Question)
         .NotEmpty()
         .MaximumLength(4096)
         .WithMessage("Question must be between 1 and 4096 characters.");

      RuleFor(x => x.Members)
         .NotEmpty()
         .WithMessage("At least one council member is required.");

      RuleForEach(x => x.Members)
         .SetValidator(new ScenarioMemberValidator());

      // Unbounded, this is a paid operation: each round is a request to an LLM.
      RuleFor(x => x.MaxRounds)
         .InclusiveBetween(1, 20)
         .WithMessage("MaxRounds must be between 1 and 20.");

      RuleFor(x => x.Temperature)
         .InclusiveBetween(0f, 2f)
         .WithMessage("Temperature must be between 0.0 and 2.0.");
   }
}

/// <summary>Validates a single member of a <see cref="ScenarioRequest" />.</summary>
public sealed class ScenarioMemberValidator : AbstractValidator<ScenarioMember>
{
   public ScenarioMemberValidator()
   {
      RuleFor(x => x.Role)
         .NotEmpty()
         .MaximumLength(200)
         .WithMessage("Each member needs a non-empty role.");
   }
}
