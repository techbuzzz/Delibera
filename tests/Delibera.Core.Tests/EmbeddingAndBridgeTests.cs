using Delibera.Core.Extensions;
using Delibera.Core.Interfaces;
using Delibera.Core.Providers;
using Delibera.Core.Providers.LLM;
using Delibera.Core.Tests.Fakes;
using Microsoft.Extensions.AI;

namespace Delibera.Core.Tests;

public class EmbeddingGeneratorProviderTests
{
   [Fact]
   public void VectorSize_ReadFromMetadata()
   {
      using var gen = new FakeEmbeddingGenerator(dimensions: 8);
      using var provider = new EmbeddingGeneratorProvider(gen);

      Assert.Equal(8, provider.VectorSize);
      Assert.Equal("fake-embed", provider.EmbeddingModelName);
   }

   [Fact]
   public async Task EmbedAsync_ReturnsVectorOfCorrectSize()
   {
      using var gen = new FakeEmbeddingGenerator(dimensions: 5);
      using var provider = new EmbeddingGeneratorProvider(gen);

      var vector = await provider.EmbedAsync("hello");

      Assert.Equal(5, vector.Length);
   }

   [Fact]
   public async Task EmbedBatchAsync_ReturnsOneVectorPerInput()
   {
      using var gen = new FakeEmbeddingGenerator(dimensions: 3);
      using var provider = new EmbeddingGeneratorProvider(gen);

      var vectors = await provider.EmbedBatchAsync(["a", "bb", "ccc"]);

      Assert.Equal(3, vectors.Count);
      Assert.All(vectors, v => Assert.Equal(3, v.Length));
   }

   [Fact]
   public async Task EmbedBatchAsync_EmptyInput_ReturnsEmpty()
   {
      using var gen = new FakeEmbeddingGenerator();
      using var provider = new EmbeddingGeneratorProvider(gen);

      var vectors = await provider.EmbedBatchAsync([]);

      Assert.Empty(vectors);
   }
}

public class MicrosoftAIExtensionsTests
{
   [Fact]
   public void AsLLMProvider_WrapsChatClient()
   {
      using var client = new FakeChatClient(providerName: "Z");
      using var provider = client.AsLLMProvider();

      Assert.IsType<ChatClientLLMProvider>(provider);
      Assert.Equal("Z", provider.ProviderName);
   }

   [Fact]
   public void AsChatClient_OnChatClientProvider_ReturnsUnderlyingClient()
   {
      using var client = new FakeChatClient();
      using var provider = new ChatClientLLMProvider(client);

      var roundTrip = provider.AsChatClient();

      Assert.Same(client, roundTrip);
   }

   [Fact]
   public async Task AsChatClient_OnArbitraryProvider_BridgesCalls()
   {
      // Use a custom provider that is NOT a ChatClientLLMProvider to force the adapter path.
      ILLMProvider custom = new EchoProvider();
      var chatClient = custom.AsChatClient("echo-model");

      var response = await chatClient.GetResponseAsync([new ChatMessage(ChatRole.User, "ping")]);

      Assert.Equal("echo:ping", response.Text);
      Assert.Equal("echo-model", response.ModelId);
   }

   [Fact]
   public async Task AsChatClient_Streaming_BridgesChunks()
   {
      ILLMProvider custom = new EchoProvider();
      var chatClient = custom.AsChatClient("echo-model");

      var text = "";
      await foreach (var update in chatClient.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "ping")]))
         text += update.Text;

      Assert.Equal("echo:ping", text);
   }

   /// <summary>Minimal provider that echoes the user prompt, to exercise the bridge adapter.</summary>
   private sealed class EchoProvider : ILLMProvider
   {
      public string ProviderName => "Echo";
      public Task<bool> IsAvailableAsync(CancellationToken ct = default) => Task.FromResult(true);
      public Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<string>>([]);
      public Task<string> ChatAsync(string model, string systemPrompt, string userPrompt, float temperature = 0.7f, CancellationToken ct = default)
         => Task.FromResult($"echo:{userPrompt}");
      public void Dispose() { }
   }
}

public class ProviderFactoryChatClientTests
{
   [Fact]
   public void CreateFromChatClient_CachesByName()
   {
      using var factory = new ProviderFactory();
      using var client = new FakeChatClient();

      var a = factory.CreateFromChatClient("x", client);
      var b = factory.CreateFromChatClient("x", client);

      Assert.Same(a, b);
   }

   [Fact]
   public void CreateFromChatClient_ReturnsChatClientProvider()
   {
      using var factory = new ProviderFactory();
      using var client = new FakeChatClient(providerName: "P");

      var provider = factory.CreateFromChatClient("y", client, "Custom");

      Assert.Equal("Custom", provider.ProviderName);
   }
}
