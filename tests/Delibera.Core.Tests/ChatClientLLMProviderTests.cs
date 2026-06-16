using Delibera.Core.Providers.LLM;
using Delibera.Core.Tests.Fakes;
using Microsoft.Extensions.AI;

namespace Delibera.Core.Tests;

public class ChatClientLLMProviderTests
{
   [Fact]
   public void ProviderName_TakenFromClientMetadata_WhenNotSpecified()
   {
      using var client = new FakeChatClient(providerName: "Contoso");
      using var provider = new ChatClientLLMProvider(client);

      Assert.Equal("Contoso", provider.ProviderName);
   }

   [Fact]
   public void ProviderName_ExplicitOverridesMetadata()
   {
      using var client = new FakeChatClient(providerName: "Contoso");
      using var provider = new ChatClientLLMProvider(client, "MyName");

      Assert.Equal("MyName", provider.ProviderName);
   }

   [Fact]
   public async Task ChatAsync_ReturnsClientText_AndForwardsModelAndTemperature()
   {
      var client = new FakeChatClient(reply: "hello world");
      using var provider = new ChatClientLLMProvider(client);

      var answer = await provider.ChatAsync("gpt-test", "system", "user", temperature: 0.42f);

      Assert.Equal("hello world", answer);
      Assert.Equal("gpt-test", client.LastOptions?.ModelId);
      Assert.Equal(0.42f, client.LastOptions?.Temperature);
   }

   [Fact]
   public async Task ChatAsync_PutsSystemAndUserMessagesInOrder()
   {
      var client = new FakeChatClient();
      using var provider = new ChatClientLLMProvider(client);

      await provider.ChatAsync("m", "the-system", "the-user");

      Assert.NotNull(client.LastMessages);
      Assert.Equal(2, client.LastMessages!.Count);
      Assert.Equal(ChatRole.System, client.LastMessages[0].Role);
      Assert.Equal("the-system", client.LastMessages[0].Text);
      Assert.Equal(ChatRole.User, client.LastMessages[1].Role);
      Assert.Equal("the-user", client.LastMessages[1].Text);
   }

   [Fact]
   public async Task ChatAsync_OmitsSystemMessage_WhenSystemPromptEmpty()
   {
      var client = new FakeChatClient();
      using var provider = new ChatClientLLMProvider(client);

      await provider.ChatAsync("m", "", "only-user");

      Assert.Single(client.LastMessages!);
      Assert.Equal(ChatRole.User, client.LastMessages![0].Role);
   }

   [Fact]
   public async Task ChatStreamAsync_YieldsChunks_ThatConcatenateToFullReply()
   {
      var client = new FakeChatClient(reply: "alpha beta gamma");
      using var provider = new ChatClientLLMProvider(client);

      var chunks = new List<string>();
      await foreach (var chunk in provider.ChatStreamAsync("m", "s", "u"))
         chunks.Add(chunk);

      Assert.True(chunks.Count > 1, "Streaming should produce multiple chunks.");
      Assert.Equal("alpha beta gamma", string.Concat(chunks).Trim());
   }

   [Fact]
   public async Task ChatAsync_Throws_OnEmptyReply()
   {
      var client = new FakeChatClient(reply: "   ");
      using var provider = new ChatClientLLMProvider(client);

      await Assert.ThrowsAsync<InvalidOperationException>(() => provider.ChatAsync("m", "s", "u"));
   }

   [Fact]
   public void Dispose_DisposesClient_WhenOwned()
   {
      var client = new FakeChatClient();
      var provider = new ChatClientLLMProvider(client, ownsClient: true);
      provider.Dispose();
      Assert.True(client.Disposed);
   }

   [Fact]
   public void Dispose_LeavesClient_WhenNotOwned()
   {
      var client = new FakeChatClient();
      var provider = new ChatClientLLMProvider(client, ownsClient: false);
      provider.Dispose();
      Assert.False(client.Disposed);
   }

   [Fact]
   public async Task ListModelsAsync_ReturnsDefaultModel()
   {
      using var client = new FakeChatClient(defaultModel: "default-x");
      using var provider = new ChatClientLLMProvider(client);

      var models = await provider.ListModelsAsync();

      Assert.Contains("default-x", models);
   }
}
