using Delibera.Core.Attachments;
using Delibera.Core.Attachments.Readers;
using Delibera.Core.Council;
using Delibera.Core.Models;
using Delibera.Core.Tests.Fakes;
using FluentAssertions;

namespace Delibera.Core.Tests;

/// <summary>
///    Tests for the F-06 Multi-Modal Council feature
///    (<see cref="IFileContentReader"/>, <see cref="FileContentReaderRegistry"/>,
///    <see cref="PlainTextFileReader"/>, <see cref="ImageFileReader"/>,
///    <see cref="FallbackFileReader"/>, <see cref="DelegateFileContentReader"/>,
///    <see cref="MemberCapabilities"/>,
///    <see cref="ModelContextWindowRegistry.SupportsVision"/>,
///    <see cref="ICouncilBuilder.WithAttachment(string)"/>,
///    <see cref="ICouncilBuilder.WithFileReader(string, IFileContentReader)"/>).
/// </summary>
public class MultiModalTests : IDisposable
{
    private readonly string _tempDir;

    public MultiModalTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "delibera_mm_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best-effort */ }
    }

    // ── PlainTextFileReader ──

    [Fact]
    public async Task PlainTextReader_Reads_Text_File()
    {
        var path = Path.Combine(_tempDir, "test.md");
        await File.WriteAllTextAsync(path, "# Hello\nWorld");
        var reader = new PlainTextFileReader();

        var result = await reader.ReadAsync(path);

        result.TextContent.Should().Contain("Hello");
        result.TextContent.Should().Contain("World");
        result.BinaryParts.Should().BeNull();
        result.Metadata.Should().NotBeNull();
        result.Metadata!["extension"].Should().Be(".md");
    }

    [Fact]
    public async Task PlainTextReader_SupportedExtensions_Includes_Common_Formats()
    {
        var reader = new PlainTextFileReader();
        reader.SupportedExtensions.Should().Contain([".txt", ".md", ".json", ".xml", ".cs", ".yml", ".csv", ".html"]);
    }

    [Fact]
    public async Task PlainTextReader_Missing_File_Returns_Placeholder()
    {
        var reader = new PlainTextFileReader();
        var result = await reader.ReadAsync(Path.Combine(_tempDir, "nonexistent.txt"));
        result.TextContent.Should().Contain("file not found");
        result.BinaryParts.Should().BeNull();
    }

    // ── ImageFileReader ──

    [Fact]
    public async Task ImageReader_Reads_Png_As_BinaryAttachment()
    {
        var path = Path.Combine(_tempDir, "test.png");
        await File.WriteAllBytesAsync(path, [0x89, 0x50, 0x4E, 0x47]); // PNG header bytes
        var reader = new ImageFileReader();

        var result = await reader.ReadAsync(path);

        result.BinaryParts.Should().NotBeNull();
        result.BinaryParts!.Should().HaveCount(1);
        result.BinaryParts[0].MediaType.Should().Be("image/png");
        result.BinaryParts[0].Content.Should().NotBeEmpty();
        result.TextContent.Should().BeNull();
        result.Metadata.Should().NotBeNull();
        result.Metadata!["mediaType"].Should().Be("image/png");
    }

    [Fact]
    public async Task ImageReader_SupportedExtensions_Includes_Common_Formats()
    {
        var reader = new ImageFileReader();
        reader.SupportedExtensions.Should().Contain([".png", ".jpg", ".jpeg", ".webp", ".gif"]);
    }

    [Fact]
    public async Task ImageReader_Missing_File_Returns_Placeholder()
    {
        var reader = new ImageFileReader();
        var result = await reader.ReadAsync(Path.Combine(_tempDir, "nonexistent.png"));
        result.TextContent.Should().Contain("file not found");
        result.BinaryParts.Should().BeNull();
    }

    [Fact]
    public void ImageReader_GetMediaType_Returns_Correct_Mime()
    {
        ImageFileReader.GetMediaType(".png").Should().Be("image/png");
        ImageFileReader.GetMediaType(".jpg").Should().Be("image/jpeg");
        ImageFileReader.GetMediaType(".webp").Should().Be("image/webp");
        ImageFileReader.GetMediaType(".unknown").Should().Be("application/octet-stream");
    }

    // ── FallbackFileReader ──

    [Fact]
    public async Task FallbackReader_Returns_Placeholder_With_Instructions()
    {
        var result = await FallbackFileReader.Instance.ReadAsync("requirements.pdf");
        result.TextContent.Should().Contain("[Attachment: requirements.pdf]");
        result.TextContent.Should().Contain("No reader is registered");
        result.TextContent.Should().Contain(".WithFileReader");
        result.BinaryParts.Should().BeNull();
    }

    [Fact]
    public void FallbackReader_Is_Singleton()
    {
        FallbackFileReader.Instance.Should().BeSameAs(FallbackFileReader.Instance);
    }

    // ── DelegateFileContentReader ──

    [Fact]
    public async Task DelegateReader_Wraps_Lambda()
    {
        var reader = new DelegateFileContentReader(".pdf", (path, ct) =>
            Task.FromResult(new FileReadResult(path, "extracted text", null, null)));

        var result = await reader.ReadAsync("test.pdf");
        result.TextContent.Should().Be("extracted text");
        reader.SupportedExtensions.Should().Contain(".pdf");
    }

    // ── FileContentReaderRegistry ──

    [Fact]
    public void Registry_PreRegisters_PlainText_And_Image_Readers()
    {
        var registry = new FileContentReaderRegistry();
        registry.IsRegistered(".txt").Should().BeTrue();
        registry.IsRegistered(".md").Should().BeTrue();
        registry.IsRegistered(".png").Should().BeTrue();
        registry.IsRegistered(".jpg").Should().BeTrue();
    }

    [Fact]
    public void Registry_GetReader_Returns_PlainText_For_Text_Extensions()
    {
        var registry = new FileContentReaderRegistry();
        var reader = registry.GetReader("test.md");
        reader.Should().BeOfType<PlainTextFileReader>();
    }

    [Fact]
    public void Registry_GetReader_Returns_Image_For_Image_Extensions()
    {
        var registry = new FileContentReaderRegistry();
        var reader = registry.GetReader("test.png");
        reader.Should().BeOfType<ImageFileReader>();
    }

    [Fact]
    public void Registry_GetReader_Returns_Fallback_For_Unknown_Extensions()
    {
        var registry = new FileContentReaderRegistry();
        var reader = registry.GetReader("test.pdf");
        reader.Should().BeOfType<FallbackFileReader>();
    }

    [Fact]
    public void Registry_Register_Instance_Overwrites_BuiltIn()
    {
        var registry = new FileContentReaderRegistry();
        var custom = new DelegateFileContentReader(".md", (path, ct) =>
            Task.FromResult(new FileReadResult(path, "custom", null, null)));
        registry.Register(".md", custom);

        var reader = registry.GetReader("test.md");
        reader.Should().BeSameAs(custom);
    }

    [Fact]
    public void Registry_Register_Lambda_Works()
    {
        var registry = new FileContentReaderRegistry();
        registry.Register(".pdf", (path, ct) =>
            Task.FromResult(new FileReadResult(path, "lambda text", null, null)));

        registry.IsRegistered(".pdf").Should().BeTrue();
        var reader = registry.GetReader("test.pdf");
        reader.Should().BeOfType<DelegateFileContentReader>();
    }

    [Fact]
    public void Registry_GetReader_With_No_Extension_Returns_Fallback()
    {
        var registry = new FileContentReaderRegistry();
        var reader = registry.GetReader("noextension");
        reader.Should().BeOfType<FallbackFileReader>();
    }

    // ── MemberCapabilities ──

    [Fact]
    public void MemberCapabilities_Flags_Combine()
    {
        var caps = MemberCapabilities.Text | MemberCapabilities.Vision;
        caps.HasFlag(MemberCapabilities.Text).Should().BeTrue();
        caps.HasFlag(MemberCapabilities.Vision).Should().BeTrue();
    }

    [Fact]
    public void CouncilMember_SupportsVision_Reflects_Capabilities()
    {
        var provider = new FakeLLMProvider();
        var member = new CouncilMember("llava:13b", provider, "Analyst")
        {
            Capabilities = MemberCapabilities.Text | MemberCapabilities.Vision
        };
        member.SupportsVision.Should().BeTrue();

        var textOnly = new CouncilMember("qwen2.5", provider, "Strategist");
        textOnly.SupportsVision.Should().BeFalse();
    }

    // ── ModelContextWindowRegistry vision detection ──

    [Theory]
    [InlineData("llava:13b")]
    [InlineData("gpt-4o")]
    [InlineData("gpt-4o-mini")]
    [InlineData("claude-3.5-sonnet")]
    [InlineData("qwen2-vl-7b")]
    [InlineData("gemma3-4b")]
    [InlineData("minicpm-v")]
    [InlineData("internvl2")]
    [InlineData("pixtral-12b")]
    [InlineData("llama4-scout")]
    public void ModelContextWindowRegistry_Detects_Vision_Models(string modelName)
    {
        ModelContextWindowRegistry.SupportsVision(modelName).Should().BeTrue();
    }

    [Theory]
    [InlineData("qwen2.5:7b")]
    [InlineData("llama3.2:1b")]
    [InlineData("mistral:7b")]
    [InlineData("phi3.5")]
    [InlineData("deepseek-r1")]
    public void ModelContextWindowRegistry_Does_Not_Detect_TextOnly_Models(string modelName)
    {
        ModelContextWindowRegistry.SupportsVision(modelName).Should().BeFalse();
    }

    [Fact]
    public void ModelContextWindowRegistry_GetCapabilities_Combines_ContextWindow_And_Vision()
    {
        // gpt-4o is in both the context-window registry and the vision patterns.
        var caps = ModelContextWindowRegistry.GetCapabilities("gpt-4o");
        caps.SupportsVision.Should().BeTrue();
        caps.ContextWindowTokens.Should().NotBeNull();
        caps.ContextWindowTokens.Should().Be(131_072);
    }

    [Fact]
    public void ModelContextWindowRegistry_RegisterVisionPattern_Works()
    {
        ModelContextWindowRegistry.RegisterVisionPattern("my-custom-vision-model");
        ModelContextWindowRegistry.SupportsVision("my-custom-vision-model-v2").Should().BeTrue();
    }

    [Fact]
    public void ModelContextWindowRegistry_GetVisionPatterns_Returns_Registered()
    {
        var patterns = ModelContextWindowRegistry.GetVisionPatterns();
        patterns.Should().Contain("llava");
        patterns.Should().Contain("gpt-4o");
    }

    // ── CouncilBuilder integration ──

    [Fact]
    public void CouncilBuilder_WithAttachment_Stamps_Attachments_On_Executor()
    {
        var provider = new FakeLLMProvider();
        var executor = new CouncilBuilder()
            .AddMember("fake", provider, "A")
            .WithStandardDebate()
            .WithUserPrompt("q")
            .WithMaxRounds(1)
            .WithAttachment("./test.md")
            .Build();

        executor.Attachments.Should().HaveCount(1);
        executor.Attachments[0].FilePath.Should().Be("./test.md");
    }

    [Fact]
    public void CouncilBuilder_WithAttachment_And_Description()
    {
        var provider = new FakeLLMProvider();
        var executor = new CouncilBuilder()
            .AddMember("fake", provider, "A")
            .WithStandardDebate()
            .WithUserPrompt("q")
            .WithMaxRounds(1)
            .WithAttachment("./diagram.png", "System Architecture diagram")
            .Build();

        executor.Attachments[0].Description.Should().Be("System Architecture diagram");
    }

    [Fact]
    public void CouncilBuilder_Without_Attachments_Has_Empty_List()
    {
        var provider = new FakeLLMProvider();
        var executor = new CouncilBuilder()
            .AddMember("fake", provider, "A")
            .WithStandardDebate()
            .WithUserPrompt("q")
            .WithMaxRounds(1)
            .Build();

        executor.Attachments.Should().BeEmpty();
    }

    [Fact]
    public void CouncilBuilder_WithFileReader_Registers_In_Registry()
    {
        var provider = new FakeLLMProvider();
        var executor = new CouncilBuilder()
            .AddMember("fake", provider, "A")
            .WithStandardDebate()
            .WithUserPrompt("q")
            .WithMaxRounds(1)
            .WithFileReader(".pdf", (path, ct) =>
                Task.FromResult(new FileReadResult(path, "custom pdf text", null, null)))
            .Build();

        executor.FileReaders.IsRegistered(".pdf").Should().BeTrue();
    }

    [Fact]
    public void CouncilBuilder_WithFileReader_Instance_Registers()
    {
        var provider = new FakeLLMProvider();
        var custom = new DelegateFileContentReader(".docx", (path, ct) =>
            Task.FromResult(new FileReadResult(path, "docx", null, null)));
        var executor = new CouncilBuilder()
            .AddMember("fake", provider, "A")
            .WithStandardDebate()
            .WithUserPrompt("q")
            .WithMaxRounds(1)
            .WithFileReader(".docx", custom)
            .Build();

        executor.FileReaders.GetReader("test.docx").Should().BeSameAs(custom);
    }

    [Fact]
    public void CouncilBuilder_AddMember_With_Capabilities_AutoDetects_Vision()
    {
        var provider = new FakeLLMProvider();
        var executor = new CouncilBuilder()
            .AddMember("llava:13b", provider, "Visual Analyst",
                capabilities: MemberCapabilities.Text)
            .WithUserPrompt("q")
            .WithMaxRounds(1)
            .Build();

        // llava is a known vision pattern → auto-detection should add Vision
        executor.Members[0].SupportsVision.Should().BeTrue();
    }

    [Fact]
    public void CouncilBuilder_AddMember_Without_Capabilities_AutoDetects_Vision()
    {
        var provider = new FakeLLMProvider();
        var executor = new CouncilBuilder()
            .AddMember("gpt-4o", provider, "Analyst")
            .WithUserPrompt("q")
            .WithMaxRounds(1)
            .Build();

        // gpt-4o is a known vision pattern → auto-detection should add Vision
        executor.Members[0].SupportsVision.Should().BeTrue();
    }

    [Fact]
    public void CouncilBuilder_AddMember_TextOnly_Does_Not_Get_Vision()
    {
        var provider = new FakeLLMProvider();
        var executor = new CouncilBuilder()
            .AddMember("qwen2.5:7b", provider, "Strategist")
            .WithUserPrompt("q")
            .WithMaxRounds(1)
            .Build();

        executor.Members[0].SupportsVision.Should().BeFalse();
    }

    // ── End-to-end execution with attachment ──

    [Fact]
    public async Task ExecuteAsync_With_Text_Attachment_Injects_Into_Context()
    {
        var path = Path.Combine(_tempDir, "requirements.md");
        await File.WriteAllTextAsync(path, "# Requirements\nMust support 1000 users");
        var provider = new FakeLLMProvider(reply: "ok");
        var executor = new CouncilBuilder()
            .AddMember("fake-model", provider, "Analyst")
            .WithStandardDebate()
            .WithUserPrompt("Review the requirements")
            .WithMaxRounds(1)
            .WithAttachment(path, "Requirements document")
            .Build();

        var result = await executor.ExecuteAsync();

        result.Should().NotBeNull();
        result.ExecutionLogs.Should().Contain(l => l.Source == "Attachments");
        result.ExecutionLogs.Should().Contain(l => l.Message.Contains("Injected") && l.Message.Contains("attachment"));
    }

    [Fact]
    public async Task ExecuteAsync_With_Fallback_Reader_Logs_Placeholder()
    {
        var provider = new FakeLLMProvider(reply: "ok");
        var executor = new CouncilBuilder()
            .AddMember("fake-model", provider, "Analyst")
            .WithStandardDebate()
            .WithUserPrompt("q")
            .WithMaxRounds(1)
            .WithAttachment("requirements.pdf", "Requirements PDF")
            .Build();

        var result = await executor.ExecuteAsync();

        result.ExecutionLogs.Should().Contain(l => l.Source == "Attachments");
        // The fallback reader's placeholder text should be in the context.
        result.Context.KnowledgeContent.Should().Contain("No reader is registered");
    }

    [Fact]
    public async Task ExecuteAsync_With_Custom_Reader_Uses_It()
    {
        var provider = new FakeLLMProvider(reply: "ok");
        var executor = new CouncilBuilder()
            .AddMember("fake-model", provider, "Analyst")
            .WithStandardDebate()
            .WithUserPrompt("q")
            .WithMaxRounds(1)
            .WithAttachment("spec.pdf", "Specification PDF")
            .WithFileReader(".pdf", (path, ct) =>
                Task.FromResult(new FileReadResult(path, "Custom extracted PDF content", null, null)))
            .Build();

        var result = await executor.ExecuteAsync();

        result.Context.KnowledgeContent.Should().Contain("Custom extracted PDF content");
    }
}