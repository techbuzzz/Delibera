using System.Text;
using Delibera.Core.Council;
using Delibera.Core.Interfaces;
using Delibera.Core.Models;
using Delibera.Core.Providers;
using Delibera.Core.Providers.Mcp;

namespace Delibera.ConsoleApp.Examples;

/// <summary>
///    Расширенный пример работы 🛠️ Operator с реальными MCP-инструментами.
///    <para>
///       Демонстрирует два практических MCP-сервера:
///    </para>
///    <list type="bullet">
///       <item>
///          <description>
///             <b>Браузер</b> — официальный сервер <c>@playwright/mcp</c>, который даёт Operator'у
///             инструменты для навигации по сайтам, чтения содержимого страниц, кликов и снятия
///             скриншотов (browser_navigate, browser_snapshot, browser_click и т. д.).
///          </description>
///       </item>
///       <item>
///          <description>
///             <b>Marp-презентации</b> — сервер на базе Marp CLI (<c>@marp-team/marp-cli</c>),
///             запускаемый как stdio-MCP, который превращает Markdown в готовые презентации
///             (HTML/PDF/PPTX). Operator формирует Markdown по запросу участника и собирает слайды.
///          </description>
///       </item>
///    </list>
///    <para>
///       Пример показывает <b>оба</b> способа использования Operator:
///    </para>
///    <list type="number">
///       <item>
///          <description>Прямой вызов <see cref="Operator.ExecuteTaskAsync" /> (раздел A).</description>
///       </item>
///       <item>
///          <description>Делегирование внутри совета через маркер <c>[[OPERATOR: ...]]</c> (раздел B).</description>
///       </item>
///    </list>
/// </summary>
public static class OperatorMcpToolsExample
{
   public static async Task RunAsync()
   {
      Console.OutputEncoding = Encoding.UTF8;
      Console.WriteLine("🛠️  Operator + MCP-инструменты (браузер · Marp-презентации)\n");

      // ── LLM-провайдер ──
      // Для оркестрации инструментов достаточно «дешёвой» модели — её задача планировать
      // и интерпретировать вызовы, а не вести глубокие рассуждения.
      using var factory = new ProviderFactory();
      var ollama = factory.CreateOllama("http://localhost:11434");

      // ════════════════════════════════════════════════════════════════
      //  Конфигурация MCP-серверов
      // ════════════════════════════════════════════════════════════════

      // (1) 🌐 Браузер — Playwright MCP.
      //     Предоставляет инструменты управления реальным браузером.
      //     Запускается как дочерний процесс через npx (нужен Node.js).
      var browserServer = McpServerConfig.Stdio(
         "browser",
         "npx",
         ["-y", "@playwright/mcp@latest", "--headless"]);

      // (2) 🎯 Marp — генерация презентаций из Markdown.
      //     Каталог вывода монтируется через рабочую директорию; готовые файлы
      //     (например, deck.html / deck.pdf) появятся в ./results/presentations.
      var presentationsDir = Path.Combine(Directory.GetCurrentDirectory(), "results", "presentations");
      Directory.CreateDirectory(presentationsDir);

      var marpServer = McpServerConfig.Stdio(
         "marp",
         "npx",
         ["-y", "@marp-team/marp-cli", "--server", presentationsDir],
         workingDirectory: presentationsDir);

      // (3) 🌐 HTTP-вариант (закомментирован): удалённый MCP-сервер браузера/поиска.
      //     Передавайте заголовки авторизации при необходимости.
      // var remoteBrowser = McpServerConfig.Http(
      //    name: "browser-remote",
      //    endpoint: new Uri("https://mcp.example.com/sse"),
      //    additionalHeaders: new Dictionary<string, string> { ["Authorization"] = "Bearer <token>" });

      var servers = new[] { browserServer, marpServer };

      // ════════════════════════════════════════════════════════════════
      //  Раздел A. Прямое использование Operator (без совета)
      // ════════════════════════════════════════════════════════════════
      await RunDirectOperatorAsync(ollama, servers);

      // ════════════════════════════════════════════════════════════════
      //  Раздел B. Operator внутри совета (делегирование [[OPERATOR: ...]])
      // ════════════════════════════════════════════════════════════════
      await RunCouncilWithOperatorAsync(ollama, servers);
   }

   // ──────────────────────────────────────────────────────────────────
   //  A. Прямой Operator: формируем MCP-клиентов вручную и вызываем задачи
   // ──────────────────────────────────────────────────────────────────
   private static async Task RunDirectOperatorAsync(
      ILLMProvider ollama,
      IReadOnlyList<McpServerConfig> servers)
   {
      Console.WriteLine("══ Раздел A. Прямой вызов Operator ══\n");

      // Operator использует «дешёвую» модель для планирования инструментов.
      var operatorModel = new CouncilMember("llama3.2", ollama, "Operator");

      // Создаём по одному MCP-клиенту на каждый сервер.
      var clients = servers.Select(IMcpClientFor).ToArray();

      await using var @operator = new Operator(operatorModel, clients);

      try
      {
         await @operator.InitializeAsync();
         Console.WriteLine($"✅ Operator инициализирован: {@operator.DisplayName}");
         Console.WriteLine($"   Обнаружено инструментов: {@operator.AvailableTools.Count}");
         foreach (var tool in @operator.AvailableTools.Take(8))
            Console.WriteLine($"   • [{tool.ServerName}] {tool.Name} — {Preview(tool.Description, 60)}");

         // A.1 🌐 Браузерная задача — Operator сам выберет browser_navigate + browser_snapshot.
         Console.WriteLine("\n▶ Задача 1 (браузер): прочитать заголовок и краткое содержание страницы.");
         var browseResult = await @operator.ExecuteTaskAsync(
            "Аналитик",
            "Открой https://modelcontextprotocol.io и кратко перескажи, что такое MCP, в 3 предложениях.");
         PrintResult(browseResult);

         // A.2 🎯 Marp-задача — Operator сформирует Markdown и соберёт презентацию.
         Console.WriteLine("\n▶ Задача 2 (Marp): сгенерировать презентацию из Markdown.");
         var deckResult = await @operator.ExecuteTaskAsync(
            "Докладчик",
            "Создай Marp-презентацию из 3 слайдов о преимуществах .NET 10 и сохрани её как deck.html.");
         PrintResult(deckResult);
      }
      catch (Exception ex)
      {
         PrintFailureTips(ex);
      }
   }

   // ──────────────────────────────────────────────────────────────────
   //  B. Совет с Operator: участники делегируют задачи маркером [[OPERATOR: ...]]
   // ──────────────────────────────────────────────────────────────────
   private static async Task RunCouncilWithOperatorAsync(
      ILLMProvider ollama,
      IReadOnlyList<McpServerConfig> servers)
   {
      Console.WriteLine("\n══ Раздел B. Operator внутри совета ══\n");

      var executor = new CouncilBuilder()
         .AddMember("qwen2.5", ollama, "Исследователь")
         .AddMember("llama2", ollama, "Архитектор")
         .SetChairman(Chairman.CreateStandard("qwen2.5", ollama))
         .WithOperator(
            "llama3.2", // дешёвая модель для оркестрации инструментов
            ollama,
            servers)
         .WithStandardDebate()
         .WithSystemPrompt(
            "Вы — совет по технологической стратегии. Когда нужны свежие факты из интернета " +
            "или сборка презентации — делегируйте задачу Operator'у маркером [[OPERATOR: задача]].")
         .WithUserPrompt(
            "Подготовьте краткий обзор «Что нового в .NET 10 для high-performance кода». " +
            "Сначала проверьте актуальную информацию через браузер, затем соберите итоговую " +
            "Marp-презентацию из 4 слайдов.")
         .WithMaxRounds(4)
         .SaveResultTo("./results/operator_mcp_debate.md")
         .Build();

      Console.WriteLine(executor.GetInfo());

      // Наблюдаем за активностью Operator по раундам.
      executor.OnRoundCompleted += round =>
      {
         Console.WriteLine($"✅ Раунд {round.RoundNumber}: {round.RoundName}");
         foreach (var oi in round.OperatorInteractions)
         {
            Console.WriteLine($"   🛠️  {oi.RequesterName} → \"{Preview(oi.Task, 70)}\"");
            Console.WriteLine($"       инструментов: {oi.ToolCallCount}, ответ: {Preview(oi.Answer, 100)}");
         }
      };

      try
      {
         var result = await executor.ExecuteAsync();

         Console.WriteLine($"\n🏆 Дебаты завершены за {result.TotalDuration.TotalSeconds:F1} c");
         if (result.OperatorName is not null)
            Console.WriteLine($"   Operator: {result.OperatorName}");
         Console.WriteLine("📁 Отчёт: ./results/operator_mcp_debate.md");
         Console.WriteLine("📁 Презентации: ./results/presentations/");

         if (!string.IsNullOrWhiteSpace(result.FinalVerdict))
         {
            Console.WriteLine("\n══ ИТОГОВОЕ РЕШЕНИЕ ══\n");
            Console.WriteLine(result.FinalVerdict);
         }
      }
      catch (Exception ex)
      {
         PrintFailureTips(ex);
      }
   }

   // ──────────────────────────────────────────────────────────────────
   //  Вспомогательные методы
   // ──────────────────────────────────────────────────────────────────

   /// <summary>Создаёт MCP-клиент для конфигурации сервера.</summary>
   private static IMcpClient IMcpClientFor(McpServerConfig config)
   {
      return new McpClientAdapter(config);
   }

   private static void PrintResult(OperatorResult result)
   {
      Console.WriteLine($"   Вызвано инструментов: {result.ToolCalls.Count}" +
                        (result.Compressed
                           ? " (ответ сжат)"
                           : string.Empty));
      foreach (var call in result.ToolCalls)
      {
         var status = call.IsError
            ? "❌"
            : "✓";
         Console.WriteLine($"   {status} [{call.ServerName}] {call.ToolName}");
      }

      Console.WriteLine($"   💬 Ответ: {Preview(result.Answer, 240)}");
   }

   private static void PrintFailureTips(Exception ex)
   {
      Console.WriteLine($"\n❌ Пример завершился ошибкой: {ex.Message}");
      Console.WriteLine("\n💡 Подсказки:");
      Console.WriteLine("   • Установите Node.js + npx (нужны для npx-MCP-серверов).");
      Console.WriteLine("   • Для браузера: первый запуск Playwright скачает движки (npx playwright install).");
      Console.WriteLine("   • Запустите локальный Ollama: ollama serve (модели llama3.2, qwen2.5).");
      Console.WriteLine("   • Свои MCP-серверы подключаются через McpServerConfig.Stdio / .Http.");
   }

   private static string Preview(string? text, int max)
   {
      return string.IsNullOrEmpty(text) ? "(пусто)" :
         text.Length <= max ? text : text[..max] + "…";
   }
}