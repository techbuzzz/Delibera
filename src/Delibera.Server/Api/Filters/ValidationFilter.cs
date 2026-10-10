using System.Collections.Concurrent;

namespace Delibera.Server.Api.Filters;

/// <summary>
///    Endpoint filter that runs FluentValidation on the first argument of any
///    endpoint handler that accepts a validatable request type.
/// </summary>
public sealed class ValidationFilter(IServiceProvider sp) : IEndpointFilter
{
   /// <summary>
   ///    Argument runtime type -> closed <c>IValidator&lt;&gt;</c> type.
   ///    <para>
   ///       Building a closed generic type is not free: <c>MakeGenericType</c> walks the
   ///       generic parameters and allocates a fresh runtime type on every call. Doing that
   ///       once per argument per request repeated work whose result never changes, so the
   ///       mapping is cached. The key set is finite — arguments come from endpoint
   ///       contracts, which are sealed records fixed at compile time.
   ///    </para>
   /// </summary>
   private static readonly ConcurrentDictionary<Type, Type> ValidatorTypes = new();

   public async ValueTask<object?> InvokeAsync(
      EndpointFilterInvocationContext ctx,
      EndpointFilterDelegate next)
   {
      foreach (var arg in ctx.Arguments)
      {
         if (arg is null) continue;
         var validatorType = ValidatorTypes.GetOrAdd(
            arg.GetType(),
            static argumentType => typeof(IValidator<>).MakeGenericType(argumentType));
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
