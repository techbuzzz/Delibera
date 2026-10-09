using Delibera.Server.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
// Aliased because importing the framework namespace above also brings its own CorsOptions into
// scope, and this project's own option type is CorsGateOptions for exactly that reason.
using FrameworkCorsOptions = Microsoft.AspNetCore.Cors.Infrastructure.CorsOptions;

namespace Delibera.Server.Tests.Infrastructure;

/// <summary>
///    Guards the <c>Delibera:Server:Cors:AllowedOrigins</c> gate.
/// </summary>
/// <remarks>
///    <para>
///       The default path is the one that matters, and it is asserted rather than assumed:
///       a deployment that never configured CORS must send no Access-Control-Allow-Origin at
///       all. That header being absent is what stops a random web page from reading this
///       unauthenticated, unthrottled API â€” a cross-origin <c>fetch</c> still fires the
///       request without it, it simply cannot see the answer.
///    </para>
///    <para>
///       The second half is that there is no wildcard escape hatch. The API spends real
///       credits per debate, so "allow any origin" would hand that to every page on the
///       internet through somebody else's browser.
///    </para>
/// </remarks>
public sealed class CorsGateTests
{
   private static IConfiguration ConfigWith(params (string Key, string? Value)[] entries)
   {
      var dict = entries.ToDictionary(e => e.Key, e => e.Value);
      return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
   }

   /// <summary>
   ///    Returns null when <c>AddCors</c> was never called at all â€” which is the stronger
   ///    statement for the default path: no CORS service is registered, so there is nothing
   ///    that could emit an <c>Access-Control-Allow-Origin</c> header at request time.
   /// </summary>
   private static FrameworkCorsOptions? TryResolvePolicies(IServiceCollection services)
      => services.BuildServiceProvider()
                .GetService<IOptions<FrameworkCorsOptions>>()?.Value;

   private static CorsPolicy? ResolvePolicy(IServiceCollection services)
      => TryResolvePolicies(services)?.GetPolicy(ServerServiceExtensions.CorsPolicyName);

   [Fact]
   public void Gate_Is_Off_By_Default_When_No_Cors_Section_Exists()
   {
      var config = ConfigWith(("Delibera:Strategy", "Standard"));

      var services = new ServiceCollection();
      services.AddDeliberaCors(config);

      config.IsCorsEnabled().Should().BeFalse();
      TryResolvePolicies(services).Should().BeNull();
   }

   [Fact]
   public void Empty_Origin_List_Registers_No_Policy()
   {
      var config = ConfigWith(
         ("Delibera:Server:Cors:AllowedOrigins:0", ""));

      var services = new ServiceCollection();
      services.AddDeliberaCors(config);

      config.IsCorsEnabled().Should().BeFalse();
      TryResolvePolicies(services).Should().BeNull();
   }

   [Theory]
   [InlineData(" ")]
   [InlineData("\t")]
   public void Whitespace_Only_Origins_Count_As_Not_Configured(string origin)
   {
      // A blank entry is what an unset compose variable leaves behind. Treating it as an
      // origin would register a policy matching the literal string " ", which matches nothing
      // while making IsCorsEnabled report true â€” the pipeline would then call UseCors on a
      // policy that can never match.
      var config = ConfigWith(("Delibera:Server:Cors:AllowedOrigins:0", origin));

      config.IsCorsEnabled().Should().BeFalse();
   }

   [Fact]
   public void Configured_Origin_Registers_The_Policy_And_Enables_Cors()
   {
      const string origin = "https://techbuzzz.github.io";
      var config = ConfigWith(("Delibera:Server:Cors:AllowedOrigins:0", origin));

      var services = new ServiceCollection();
      services.AddDeliberaCors(config);

      config.IsCorsEnabled().Should().BeTrue();
      ResolvePolicy(services).Should().NotBeNull();
   }

   [Fact]
   public void Origins_Are_Trimmed_And_Deduplicated()
   {
      var config = ConfigWith(
         ("Delibera:Server:Cors:AllowedOrigins:0", "  https://techbuzzz.github.io  "),
         ("Delibera:Server:Cors:AllowedOrigins:1", "https://techbuzzz.github.io"));

      var services = new ServiceCollection();
      services.AddDeliberaCors(config);

      config.IsCorsEnabled().Should().BeTrue();
      var policy = ResolvePolicy(services);
      policy.Should().NotBeNull();
   }

   [Fact]
   public void Wildcard_With_Credentials_Throws_Naming_The_Config_Key()
   {
      var config = ConfigWith(
         ("Delibera:Server:Cors:AllowedOrigins:0", "*"),
         ("Delibera:Server:Cors:AllowCredentials", "true"));

      var services = new ServiceCollection();

      var act = () => services.AddDeliberaCors(config);

      // The framework rejects this combination too, but with a message that names no config
      // key â€” an operator would be left grepping. Failing here points straight at the setting.
      act.Should().Throw<InvalidOperationException>()
         .WithMessage("*Delibera:Server:Cors:AllowCredentials*");
   }

   [Fact]
   public void Wildcard_Without_Credentials_Is_Still_Registered()
   {
      // Documented as the single sharp edge: usable, but it does make the API readable from any
      // origin. The gate's contract is explicit configuration, not a curated list of safe values.
      var config = ConfigWith(("Delibera:Server:Cors:AllowedOrigins:0", "*"));

      var services = new ServiceCollection();
      services.AddDeliberaCors(config);

      config.IsCorsEnabled().Should().BeTrue();
      ResolvePolicy(services).Should().NotBeNull();
   }
}