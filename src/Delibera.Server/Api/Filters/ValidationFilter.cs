using FluentValidation;

namespace Delibera.Server.Api.Filters;

/// <summary>
/// Endpoint filter that runs FluentValidation on the first argument of any
/// endpoint handler that accepts a validatable request type.
/// </summary>
public sealed class ValidationFilter(IServiceProvider sp) : IEndpointFilter
{
   public async ValueTask<object?> InvokeAsync(
      EndpointFilterInvocationContext ctx,
      EndpointFilterDelegate next)
   {
      foreach (var arg in ctx.Arguments)
      {
         if (arg is null) continue;
         var validatorType = typeof(IValidator<>).MakeGenericType(arg.GetType());
         if (sp.GetService(validatorType) is not IValidator validator) continue;

         var result = await validator.ValidateAsync(
            new ValidationContext<object>(arg), ctx.HttpContext.RequestAborted);

         if (!result.IsValid)
            return Results.ValidationProblem(
               result.ToDictionary(),
               // 400, not 422: every endpoint in this API declares
               // .ProducesValidationProblem(), which is documented as 400, so returning 422
               // meant generated clients were built against a status the server never
               // returns. 400 is also the ASP.NET Core convention.
               statusCode: StatusCodes.Status400BadRequest);
      }

      return await next(ctx);
   }
}
