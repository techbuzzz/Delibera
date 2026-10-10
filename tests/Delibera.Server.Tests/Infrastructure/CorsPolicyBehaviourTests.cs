using System.Net;
using Delibera.Server.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Delibera.Server.Tests.Infrastructure;

/// <summary>
///    The CORS gate measured over HTTP, against the real pipeline.
/// </summary>
/// <remarks>
///    <para>
///       <c>CorsGateTests</c> asserts what got registered in the container. That is not the
///       same question as whether a browser would ever see the header, and it is the second one
///       that matters: a policy can be registered correctly and still emit nothing if
///       <c>UseCors</c> sits in the wrong place in the pipeline or is never called.
///    </para>
///    <para>
///       The first test is the important one. It asserts the shipped default produces no
///       <c>Access-Control-Allow-Origin</c> for a cross-origin request, which is what stops an
///       arbitrary page from reading a response from an unauthenticated API that spends real
///       credits.
///    </para>
/// </remarks>
public sealed class CorsPolicyBehaviourTests : IClassFixture<WebApplicationFactory<Program>>
{
   private const string AllowedOrigin = "https://techbuzzz.github.io";
   private readonly WebApplicationFactory<Program> _factory;

   public CorsPolicyBehaviourTests(WebApplicationFactory<Program> factory) => _factory = factory;

   private WebApplicationFactory<Program> HostWithCors(params (string Key, string? Value)[] settings) =>
      _factory.WithWebHostBuilder(b =>
      {
         b.UseEnvironment("Production");

         // UseSetting, not ConfigureAppConfiguration. This app is built by
         // CreateSlimBuilder, which captures its configuration before the factory's
         // app-configuration delegates are applied — so a source added that way is
         // invisible to AddDeliberaServer, and the gate silently stays off. Host settings
         // do reach builder.Configuration.
         foreach (var (key, value) in settings)
            if (value is not null)
               b.UseSetting(key, value);
      });

   private static HttpRequestMessage Get(string origin)
   {
      var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/health");
      request.Headers.Add("Origin", origin);
      return request;
   }

   [Fact]
   public async Task Default_Host_Returns_No_AccessControlAllowOrigin_For_A_Cross_Origin_Call()
   {
      using var factory = _factory.WithWebHostBuilder(b => b.UseEnvironment("Production"));
      using var client = factory.CreateClient();
      using var request = Get("https://evil.example");
      using var response = await client.SendAsync(request);

      response.StatusCode.Should().Be(HttpStatusCode.OK, "the request itself must still succeed");
      response.Headers.Contains("Access-Control-Allow-Origin")
         .Should().BeFalse("CORS is off unless origins are configured explicitly");
   }

   [Fact]
   public async Task Configured_Origin_Receives_The_Header_Echoing_Its_Origin()
   {
      using var factory = HostWithCors(("Delibera:Server:Cors:AllowedOrigins:0", AllowedOrigin));
      using var client = factory.CreateClient();
      using var request = Get(AllowedOrigin);
      using var response = await client.SendAsync(request);

      response.Headers.GetValues("Access-Control-Allow-Origin")
         .Should().ContainSingle()
         .Which.Should().Be(AllowedOrigin,
            "the middleware echoes the exact origin rather than a wildcard, so a static client " +
            "is allowed without opening the API to every origin on the internet");
   }

   [Fact]
   public async Task Unlisted_Origin_Receives_No_Header_Even_When_The_Policy_Is_Active()
   {
      using var factory = HostWithCors(("Delibera:Server:Cors:AllowedOrigins:0", AllowedOrigin));
      using var client = factory.CreateClient();
      using var request = Get("https://evil.example");
      using var response = await client.SendAsync(request);

      // The request still executes — CORS gates what a browser may READ, not what it may send.
      // Asserting only "no header" here would pass even if the whole middleware were absent.
      response.Headers.Contains("Access-Control-Allow-Origin").Should().BeFalse();
   }

   [Fact]
   public async Task Preflight_From_A_Configured_Origin_Is_Answered()
   {
      using var factory = HostWithCors(("Delibera:Server:Cors:AllowedOrigins:0", AllowedOrigin));
      using var client = factory.CreateClient();
      using var request = new HttpRequestMessage(HttpMethod.Options, "/api/v1/debates");
      request.Headers.Add("Origin", AllowedOrigin);
      request.Headers.Add("Access-Control-Request-Method", "POST");
      using var response = await client.SendAsync(request);

      response.StatusCode.Should().Be(HttpStatusCode.NoContent);
      response.Headers.GetValues("Access-Control-Allow-Origin").Should().ContainSingle();
      response.Headers.Contains("Access-Control-Allow-Methods")
         .Should().BeTrue("the static client must be able to discover which verbs are allowed");
   }

   [Fact]
   public async Task CorrelationId_Is_Exposed_To_Cross_Origin_Clients_When_Cors_Is_On()
   {
      using var factory = HostWithCors(("Delibera:Server:Cors:AllowedOrigins:0", AllowedOrigin));
      using var client = factory.CreateClient();
      using var request = Get(AllowedOrigin);
      using var response = await client.SendAsync(request);

      // Without this in Allow-Expose-Headers the browser hides the value from JavaScript, and
      // the id that finds the entry in the server log becomes unusable to the caller.
      response.Headers.Contains("Access-Control-Expose-Headers")
         .Should().BeTrue();
   }
}