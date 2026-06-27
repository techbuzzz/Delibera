using System.Text;
using Delibera.Core.Chunking;
using Delibera.Core.Council;
using Delibera.Core.Debate;
using Delibera.Core.DependencyInjection;
using Delibera.Core.Interfaces;
using Delibera.Core.Knowledge;
using Delibera.Core.Models;
using Delibera.Core.Providers;
using Delibera.Core.Providers.LLM;

namespace Delibera.ConsoleApp.Examples;

/// <summary>
///    Demonstrates the AutoChunking feature — automatic splitting of large documents
///    into context-window-sized chunks distributed across debate rounds.
/// </summary>
/// <remarks>
///    <para>
///       This example shows three integration paths:
///    </para>
///    <list type="number">
///       <item><b>Fluent API</b> — <c>.WithAutoChunking()</c> on the builder.</item>
///       <item><b>CouncilOptions snapshot</b> — <c>.WithOptions(options)</c> with pre-built config.</item>
///       <item><b>Lambda configuration</b> — <c>.WithOptions(o => { ... })</c> inline.</item>
///    </list>
/// </remarks>
public static class AutoChunkingExample
{
   /// <summary>
   ///    Runs the AutoChunking example.
   /// </summary>
   public static async Task RunAsync()
   {
      Console.WriteLine("═══════════════════════════════════════");
      Console.WriteLine("  ✂️  AutoChunking Example");
      Console.WriteLine("═══════════════════════════════════════\n");

      // ═══════════════════════════════════════════════
      // 1. Prepare a large knowledge document
      // ═══════════════════════════════════════════════
      Console.WriteLine("📄 Preparing knowledge base...\n");

      var kb = new MarkdownKnowledgeBase("Contract Analysis");

      // Load a knowledge file if it exists, otherwise generate a synthetic large document.
      var knowledgePath = Path.Combine(Directory.GetCurrentDirectory(), "knowledge", "context.md");
      if (File.Exists(knowledgePath))
      {
         await kb.LoadAsync(knowledgePath);
         Console.WriteLine($"  ✅ Loaded: {knowledgePath} ({kb.TotalCharacters:N0} chars)");
      }
      else
      {
         // Generate a synthetic large document to demonstrate chunking.
         var syntheticDoc = GenerateSyntheticContract();
         var tempPath = Path.Combine(Path.GetTempPath(), "delibera_demo_contract.md");
         await File.WriteAllTextAsync(tempPath, syntheticDoc);
         await kb.LoadAsync(tempPath);
         Console.WriteLine($"  📝 Generated synthetic contract ({kb.TotalCharacters:N0} chars)");
         Console.WriteLine($"     Saved to: {tempPath}");
      }

      Console.WriteLine($"  Documents: {kb.DocumentCount}, Total chars: {kb.TotalCharacters:N0}\n");

      // ═══════════════════════════════════════════════
      // 2. Initialise providers
      // ═══════════════════════════════════════════════
      Console.WriteLine("🔧 Initialising providers...\n");

      using var ollamaLocal = OllamaProvider.ForLocal("http://localhost:11434");
      using var ollamaCloud = OllamaProvider.ForCloud(
         "https://api.ollama.com",
         Environment.GetEnvironmentVariable("OLLAMA_CLOUD_API_KEY") ?? "");

      // Check availability
      var localAvailable = await IsAvailableSafe(ollamaLocal, "Ollama Local");
      var cloudAvailable = await IsAvailableSafe(ollamaCloud, "Ollama Cloud");

      if (!localAvailable && !cloudAvailable)
      {
         Console.WriteLine("\n⚠️  No Ollama providers available. Skipping live demo.");
         Console.WriteLine("   Start a local Ollama server: ollama serve");
         Console.WriteLine("   Or set OLLAMA_CLOUD_API_KEY environment variable.");
         await DemoChunkingPlanOnly(kb);
         return;
      }

      var provider = localAvailable ? ollamaLocal : ollamaCloud;
      var providerLabel = localAvailable ? "Ollama Local" : "Ollama Cloud";

      Console.WriteLine($"  Using: {providerLabel}\n");

      // List available models
      IReadOnlyList<string> models;
      try
      {
         models = await provider.ListModelsAsync();
         Console.WriteLine($"  Available models: {string.Join(", ", models.Take(5))}");
      }
      catch
      {
         models = ["llama3.2", "qwen2.5", "phi3:mini"];
         Console.WriteLine("  Using default model list (could not enumerate)");
      }

      // ═══════════════════════════════════════════════
      // 3. Demo: Fluent API with WithAutoChunking()
      // ═══════════════════════════════════════════════
      Console.WriteLine("\n── Path 1: Fluent API ──\n");

      var model1 = models.FirstOrDefault(m => m.Contains("phi", StringComparison.OrdinalIgnoreCase)) ?? models[0];
      var model2 = models.FirstOrDefault(m => m.Contains("llama", StringComparison.OrdinalIgnoreCase)) ?? models[0];
      var chairModel = models.FirstOrDefault(m => m.Contains("qwen", StringComparison.OrdinalIgnoreCase)) ?? models[0];

      Console.WriteLine($"  Small-context model: {model1}");
      Console.WriteLine($"  Large-context model: {model2}");
      Console.WriteLine($"  Chairman model:      {chairModel}\n");

      try
      {
         var executor1 = new CouncilBuilder()
            .AddMember(model1, provider, "Legal Expert",
               "You are a legal expert specialising in contract law. Analyse contracts for risks, liabilities, and compliance issues.")
            .AddMember(model2, provider, "Business Analyst",
               "You are a business analyst. Evaluate contracts from a commercial perspective — pricing, terms, obligations.")
            .SetChairman(chairModel, provider)
            .WithKnowledge(kb)
            .WithUserPrompt("Проанализируй данный договор на предмет рисков для заказчика. Выдели ключевые проблемные пункты.")
            .WithSystemPrompt("You are an expert participating in a contract review council. Be thorough and precise.")
            .WithMaxRounds(4)
            .WithTemperature(0.3f)
            .WithResponseLanguage("Russian")
            .WithAutoChunking(new AutoChunkingOptions
            {
               Strategy = ChunkingStrategy.SemanticBoundary,
               SafetyMargin = 0.15,
               MaxChunksPerRound = 3,
               EnableProgressiveDisclosure = true
            })
            .SaveResultTo(Path.Combine(Directory.GetCurrentDirectory(), "debate_results", "autochunking_fluent.md"))
            .Build();

         Console.WriteLine(executor1.GetInfo());
         Console.WriteLine("\n🎯 Running debate with AutoChunking (fluent API)...\n");

         var result1 = await executor1.ExecuteAsync();
         PrintResultSummary(result1);
      }
      catch (Exception ex)
      {
         Console.WriteLine($"  ⚠️  Fluent API demo failed: {ex.Message}");
         Console.WriteLine("  (This is expected if the models are not pulled locally)");
      }

      // ═══════════════════════════════════════════════
      // 4. Demo: CouncilOptions snapshot
      // ═══════════════════════════════════════════════
      Console.WriteLine("\n── Path 2: CouncilOptions snapshot ──\n");

      try
      {
         var options = new CouncilOptions
         {
            Strategy = "Standard",
            MaxRounds = 4,
            Temperature = 0.3f,
            SystemPrompt = "You are an expert participating in a contract review council.",
            ResponseLanguage = "Russian",
            AutoChunking = new AutoChunkingConfig
            {
               Enabled = true,
               Strategy = "SemanticBoundary",
               SafetyMargin = 0.15,
               MaxChunksPerRound = 3,
               EnableProgressiveDisclosure = true
            }
         };

         var executor2 = new CouncilBuilder()
            .WithOptions(options) // ← bulk configuration
            .AddMember(model1, provider, "Legal Expert")
            .AddMember(model2, provider, "Business Analyst")
            .SetChairman(chairModel, provider)
            .WithKnowledge(kb)
            .WithUserPrompt("Выдели топ-5 самых рискованных пунктов договора и предложи изменения.")
            .SaveResultTo(Path.Combine(Directory.GetCurrentDirectory(), "debate_results", "autochunking_options.md"))
            .Build();

         Console.WriteLine(executor2.GetInfo());
         Console.WriteLine("\n🎯 Running debate with AutoChunking (options snapshot)...\n");

         var result2 = await executor2.ExecuteAsync();
         PrintResultSummary(result2);
      }
      catch (Exception ex)
      {
         Console.WriteLine($"  ⚠️  Options snapshot demo failed: {ex.Message}");
      }

      // ═══════════════════════════════════════════════
      // 5. Demo: Lambda configuration
      // ═══════════════════════════════════════════════
      Console.WriteLine("\n── Path 3: Lambda configuration ──\n");

      try
      {
         var executor3 = new CouncilBuilder()
            .WithOptions(o =>
            {
               o.Strategy = "Critique";
               o.MaxRounds = 4;
               o.Temperature = 0.3f;
               o.ResponseLanguage = "Russian";
               o.AutoChunking.Enabled = true;
               o.AutoChunking.Strategy = "SemanticBoundary";
               o.AutoChunking.MaxChunksPerRound = 2;
               o.AutoChunking.EnableProgressiveDisclosure = true;
            })
            .AddMember(model1, provider, "Legal Expert")
            .AddMember(model2, provider, "Business Analyst")
            .SetChairman(chairModel, provider)
            .WithKnowledge(kb)
            .WithUserPrompt("Проведи адверсариальный анализ договора: один ищет риски, другой защищает условия.")
            .SaveResultTo(Path.Combine(Directory.GetCurrentDirectory(), "debate_results", "autochunking_lambda.md"))
            .Build();

         Console.WriteLine(executor3.GetInfo());
         Console.WriteLine("\n🎯 Running debate with AutoChunking (lambda config)...\n");

         var result3 = await executor3.ExecuteAsync();
         PrintResultSummary(result3);
      }
      catch (Exception ex)
      {
         Console.WriteLine($"  ⚠️  Lambda config demo failed: {ex.Message}");
      }

      // ═══════════════════════════════════════════════
      // 6. Model Context Window Registry demo
      // ═══════════════════════════════════════════════
      Console.WriteLine("\n── Model Context Window Registry ──\n");

      Console.WriteLine("  Known model context windows:");
      foreach (var testModel in new[] { "llama3.2", "phi3:mini", "qwen2.5:7b", "deepseek-r1", "gpt-4o", "unknown-model" })
      {
         var window = ModelContextWindowRegistry.GetContextWindow(testModel);
         Console.WriteLine($"    {testModel,-25} → {(window is { } w ? $"{w:N0} tokens" : "unknown")}");
      }

      Console.WriteLine("\n  ✅ AutoChunking example complete!");
   }

   /// <summary>
   ///    Demonstrates the chunking plan without running a live debate (offline mode).
   /// </summary>
   private static Task DemoChunkingPlanOnly(MarkdownKnowledgeBase kb)
   {
      Console.WriteLine("\n── Chunking Plan Demo (offline) ──\n");

      var content = kb.GetAllContent();
      if (string.IsNullOrWhiteSpace(content))
      {
         Console.WriteLine("  No knowledge content to chunk.");
         return Task.CompletedTask;
      }

      // Simulate different context window sizes.
      foreach (var windowSize in new[] { 4096, 8192, 32768, 131072 })
      {
         var overhead = 5000; // system prompt + user question + response buffer
         var plan = AutoChunker.CreatePlan(content, windowSize, overhead);

         Console.WriteLine($"  Context window: {windowSize:N0} tokens:");
         Console.WriteLine($"    Fits in single round: {plan.FitsInSingleRound}");
         Console.WriteLine($"    Total chunks:         {plan.TotalChunks}");
         Console.WriteLine($"    Tokens per chunk:     ~{plan.EstimatedTokensPerChunk:N0}");
         Console.WriteLine($"    Available per round:  {plan.AvailableTokensPerRound:N0}");
         Console.WriteLine($"    Recommended rounds:   {plan.RecommendedRounds}");
         Console.WriteLine();
      }

      return Task.CompletedTask;
   }

   private static void PrintResultSummary(DebateResult result)
   {
      Console.WriteLine($"\n  ✅ Debate completed: {result.Rounds.Count} rounds, {result.TotalDuration.TotalSeconds:F1}s");
      if (result.TokenStats is not null)
         Console.WriteLine($"  📊 Tokens: {result.TokenStats.TotalOriginalTokens:N0} original → {result.TokenStats.TotalCompressedTokens:N0} compressed");

      if (!string.IsNullOrWhiteSpace(result.FinalVerdict))
      {
         var preview = result.FinalVerdict.Length > 500
            ? result.FinalVerdict[..500] + "…"
            : result.FinalVerdict;
         Console.WriteLine($"\n  📝 Verdict preview:\n     {preview.Replace("\n", "\n     ")}");
      }
   }

   private static async Task<bool> IsAvailableSafe(ILLMProvider provider, string label)
   {
      try
      {
         var ok = await provider.IsAvailableAsync();
         Console.WriteLine($"  {label}: {(ok ? "✅ Available" : "❌ Unavailable")}");
         return ok;
      }
      catch
      {
         Console.WriteLine($"  {label}: ❌ Unavailable");
         return false;
      }
   }

   /// <summary>
   ///    Generates a synthetic contract document large enough to trigger chunking
   ///    on small-context models (4K–8K tokens).
   /// </summary>
   private static string GenerateSyntheticContract()
   {
      var sb = new StringBuilder();

      sb.AppendLine("# ДОГОВОР ОКАЗАНИЯ УСЛУГ ПО РАЗРАБОТКЕ ПРОГРАММНОГО ОБЕСПЕЧЕНИЯ");
      sb.AppendLine();
      sb.AppendLine("г. Москва                                          «___» ________ 2026 г.");
      sb.AppendLine();
      sb.AppendLine("## 1. ПРЕДМЕТ ДОГОВОРА");
      sb.AppendLine();
      sb.AppendLine("1.1. Исполнитель обязуется по заданию Заказчика оказать услуги по разработке программного обеспечения (далее — «ПО»), а Заказчик обязуется принять и оплатить эти услуги в порядке и на условиях, предусмотренных настоящим Договором.");
      sb.AppendLine();
      sb.AppendLine("1.2. Техническое задание (ТЗ) является неотъемлемой частью настоящего Договора (Приложение №1). Любые изменения ТЗ оформляются дополнительным соглашением сторон.");
      sb.AppendLine();
      sb.AppendLine("1.3. Срок оказания услуг: с даты подписания Договора до полного выполнения обязательств, но не позднее 6 (шести) месяцев с даты начала работ.");
      sb.AppendLine();

      // Generate many sections to make the document large.
      var sections = new (string Title, string[] Paragraphs)[]
      {
         ("## 2. СТОИМОСТЬ УСЛУГ И ПОРЯДОК РАСЧЁТОВ", [
            "2.1. Общая стоимость услуг по настоящему Договору составляет 5 000 000 (пять миллионов) рублей, НДС не облагается в связи с применением Исполнителем упрощённой системы налогообложения.",
            "2.2. Оплата производится поэтапно:",
            "2.2.1. Аванс в размере 30% от общей стоимости — 1 500 000 рублей — в течение 5 рабочих дней с даты подписания Договора.",
            "2.2.2. Промежуточный платёж в размере 40% — 2 000 000 рублей — после демонстрации MVP (минимально жизнеспособного продукта) и подписания акта приёмки промежуточного этапа.",
            "2.2.3. Окончательный расчёт в размере 30% — 1 500 000 рублей — в течение 10 рабочих дней после подписания итогового акта приёмки-сдачи.",
            "2.3. Все платежи осуществляются в безналичной форме на расчётный счёт Исполнителя. Датой оплаты считается дата списания денежных средств с расчётного счёта Заказчика.",
            "2.4. В случае задержки оплаты Заказчик уплачивает пени в размере 0.1% от неоплаченной суммы за каждый день просрочки, но не более 10% от общей стоимости Договора."
         ]),
         ("## 3. ПРАВА И ОБЯЗАННОСТИ СТОРОН", [
            "3.1. Исполнитель обязуется:",
            "3.1.1. Оказать услуги качественно и в срок, соответствующий требованиям ТЗ и профессиональным стандартам в области разработки ПО.",
            "3.1.2. Предоставлять Заказчику еженедельные отчёты о ходе работ.",
            "3.1.3. Обеспечить конфиденциальность всей информации, полученной от Заказчика.",
            "3.1.4. Передать Заказчику исключительные права на разработанное ПО в полном объёме с момента подписания итогового акта.",
            "3.1.5. Обеспечить соответствие ПО требованиям законодательства РФ о персональных данных (152-ФЗ).",
            "3.2. Заказчик обязуется:",
            "3.2.1. Предоставить Исполнителю всю необходимую информацию и доступы для выполнения работ.",
            "3.2.2. Своевременно принимать и оплачивать оказанные услуги.",
            "3.2.3. Назначить ответственное лицо для оперативного взаимодействия с Исполнителем.",
            "3.2.4. Не вмешиваться в операционную деятельность Исполнителя, за исключением согласования ключевых этапов."
         ]),
         ("## 4. ПОРЯДОК СДАЧИ-ПРИЁМКИ УСЛУГ", [
            "4.1. По завершении каждого этапа Исполнитель направляет Заказчику акт приёмки-сдачи и результаты работ.",
            "4.2. Заказчик в течение 10 рабочих дней обязан подписать акт или направить мотивированный отказ с перечнем замечаний.",
            "4.3. Если Заказчик не подписал акт и не направил мотивированный отказ в указанный срок, услуги считаются принятыми в полном объёме.",
            "4.4. Исполнитель обязан устранить обоснованные замечания в течение 15 рабочих дней с даты их получения.",
            "4.5. Приёмка итогового результата осуществляется комиссией, состоящей из представителей обеих сторон."
         ]),
         ("## 5. ГАРАНТИЙНЫЕ ОБЯЗАТЕЛЬСТВА", [
            "5.1. Исполнитель гарантирует качество оказанных услуг в соответствии с ТЗ и профессиональными стандартами.",
            "5.2. Гарантийный срок на разработанное ПО составляет 12 месяцев с даты подписания итогового акта приёмки-сдачи.",
            "5.3. В течение гарантийного срока Исполнитель обязуется безвозмездно устранять дефекты, допущенные по его вине.",
            "5.4. Гарантия не распространяется на дефекты, возникшие в результате:",
            "5.4.1. Неправильной эксплуатации ПО Заказчиком.",
            "5.4.2. Внесения изменений в ПО третьими лицами без согласования с Исполнителем.",
            "5.4.3. Действий непреодолимой силы.",
            "5.5. Срок реакции на гарантийный инцидент — не более 8 рабочих часов. Срок устранения — не более 5 рабочих дней."
         ]),
         ("## 6. ОТВЕТСТВЕННОСТЬ СТОРОН", [
            "6.1. За неисполнение или ненадлежащее исполнение обязательств стороны несут ответственность в соответствии с действующим законодательством РФ.",
            "6.2. Исполнитель несёт ответственность за:",
            "6.2.1. Нарушение сроков выполнения работ — пени в размере 0.1% от стоимости невыполненных работ за каждый день просрочки.",
            "6.2.2. Некачественное выполнение работ — устранение дефектов за свой счёт.",
            "6.2.3. Нарушение конфиденциальности — штраф в размере 500 000 рублей за каждый подтверждённый случай.",
            "6.3. Заказчик несёт ответственность за:",
            "6.3.1. Нарушение сроков оплаты — пени согласно п. 2.4.",
            "6.3.2. Непредоставление необходимой информации — продление сроков выполнения работ соразмерно задержке.",
            "6.4. Общий размер ответственности Исполнителя ограничен суммой, фактически уплаченной Заказчиком по Договору.",
            "6.5. Стороны освобождаются от ответственности за частичное или полное неисполнение обязательств, если оно явилось следствием обстоятельств непреодолимой силы."
         ]),
         ("## 7. ИНТЕЛЛЕКТУАЛЬНАЯ СОБСТВЕННОСТЬ", [
            "7.1. Исключительные права на разработанное ПО, включая исходный код, документацию, дизайн и иные результаты интеллектуальной деятельности, переходят к Заказчику в полном объёме с момента подписания итогового акта приёмки-сдачи.",
            "7.2. До момента перехода прав Исполнитель вправе использовать разработанное ПО исключительно в целях исполнения настоящего Договора.",
            "7.3. Исполнитель гарантирует, что разработанное ПО не нарушает прав третьих лиц на объекты интеллектуальной собственности.",
            "7.4. В случае предъявления претензий третьими лицами Исполнитель обязуется урегулировать их самостоятельно и за свой счёт.",
            "7.5. Исполнитель сохраняет право использовать общие методы, алгоритмы и ноу-хау, разработанные в ходе исполнения Договора, в других проектах, если это не нарушает конфиденциальность."
         ]),
         ("## 8. КОНФИДЕНЦИАЛЬНОСТЬ", [
            "8.1. Стороны обязуются сохранять конфиденциальность всей информации, полученной друг от друга в ходе исполнения Договора.",
            "8.2. К конфиденциальной информации относится: техническая документация, бизнес-процессы, коммерческая тайна, персональные данные, финансовая информация.",
            "8.3. Обязательства по конфиденциальности сохраняют силу в течение 3 лет после прекращения действия Договора.",
            "8.4. Стороны обязуются обеспечить режим конфиденциальности своими сотрудниками и привлечёнными третьими лицами.",
            "8.5. Разглашение конфиденциальной информации допускается только в случаях, предусмотренных законодательством РФ."
         ]),
         ("## 9. РАСТОРЖЕНИЕ ДОГОВОРА", [
            "9.1. Договор может быть расторгнут по соглашению сторон или в одностороннем порядке в случаях, предусмотренных законодательством РФ.",
            "9.2. Заказчик вправе расторгнуть Договор в одностороннем порядке в случаях:",
            "9.2.1. Просрочки Исполнителем сроков выполнения работ более чем на 30 календарных дней.",
            "9.2.2. Существенного несоответствия результатов работ требованиям ТЗ, не устранённого в разумный срок.",
            "9.3. Исполнитель вправе расторгнуть Договор в одностороннем порядке в случаях:",
            "9.3.1. Просрочки Заказчиком оплаты более чем на 30 календарных дней.",
            "9.3.2. Непредоставления Заказчиком информации, необходимой для выполнения работ, в течение 20 рабочих дней после запроса.",
            "9.4. При расторжении Договора Исполнитель возвращает Заказчику неотработанный аванс за вычетом стоимости фактически выполненных работ."
         ]),
         ("## 10. РАЗРЕШЕНИЕ СПОРОВ", [
            "10.1. Все споры и разногласия, возникающие из настоящего Договора, разрешаются путём переговоров.",
            "10.2. Срок ответа на претензию — 15 рабочих дней с даты её получения.",
            "10.3. При недостижении согласия спор передаётся на рассмотрение Арбитражного суда г. Москвы.",
            "10.4. Применимое право — законодательство Российской Федерации."
         ]),
         ("## 11. ЗАКЛЮЧИТЕЛЬНЫЕ ПОЛОЖЕНИЯ", [
            "11.1. Договор вступает в силу с даты его подписания обеими сторонами и действует до полного исполнения обязательств.",
            "11.2. Все изменения и дополнения к Договору действительны только в письменной форме и подписываются обеими сторонами.",
            "11.3. Стороны признают юридическую силу документов, переданных по электронной почте, при условии последующего обмена оригиналами.",
            "11.4. Договор составлен в двух экземплярах, имеющих равную юридическую силу — по одному для каждой из сторон."
         ])
      };

      foreach (var (title, paragraphs) in sections)
      {
         sb.AppendLine(title);
         sb.AppendLine();
         foreach (var p in paragraphs)
         {
            sb.AppendLine(p);
            sb.AppendLine();
         }
      }

      // Add detailed risk analysis commentary to make the document even larger.
      sb.AppendLine("## 12. АНАЛИЗ РИСКОВ (ПРИЛОЖЕНИЕ)");
      sb.AppendLine();
      sb.AppendLine("12.1. Ниже представлен детальный анализ потенциальных рисков для Заказчика по каждому разделу Договора.");
      sb.AppendLine();

      var riskItems = new[]
      {
         "12.2. Риск по п. 1.3 (Сроки): Отсутствие жёсткой даты завершения работ. Формулировка «не позднее 6 месяцев» без указания конкретной даты создаёт неопределённость. Рекомендуется указать точную дату или привязать срок к календарному плану.",
         "12.3. Риск по п. 2.2 (Поэтапная оплата): Аванс 30% является стандартным, однако промежуточный платёж 40% после MVP создаёт риск — критерии MVP должны быть чётко определены в ТЗ во избежание споров о готовности.",
         "12.4. Риск по п. 2.4 (Пени): Ограничение пеней 10% от стоимости Договора может быть недостаточным при длительной просрочке. Рекомендуется увеличить до 20% или исключить ограничение.",
         "12.5. Риск по п. 3.1.4 (Права на ПО): Переход прав с момента подписания акта — стандартная практика. Однако необходимо убедиться, что в ТЗ явно указано, что все компоненты ПО (включая сторонние библиотеки) либо созданы Исполнителем, либо имеют надлежащие лицензии.",
         "12.6. Риск по п. 4.3 (Молчаливая приёмка): Автоматическая приёмка при отсутствии мотивированного отказа в течение 10 дней — существенный риск. Рекомендуется увеличить срок до 20 рабочих дней и добавить требование повторного уведомления.",
         "12.7. Риск по п. 5.2 (Гарантийный срок): 12 месяцев — стандартный срок, однако для сложного ПО может быть недостаточным. Рекомендуется увеличить до 24 месяцев или предусмотреть возможность продления.",
         "12.8. Риск по п. 6.4 (Ограничение ответственности): Ограничение ответственности Исполнителя суммой Договора — критический риск. При срыве проекта убытки Заказчика могут многократно превышать стоимость Договора. Рекомендуется исключить это ограничение или увеличить лимит.",
         "12.9. Риск по п. 7.5 (Ноу-хау Исполнителя): Исполнитель сохраняет право использовать методы и алгоритмы в других проектах. Это может привести к передаче конкурентных преимуществ Заказчика третьим лицам. Рекомендуется ограничить использование ноу-хау проектами, не конкурирующими с бизнесом Заказчика.",
         "12.10. Риск по п. 8.3 (Срок конфиденциальности): 3 года после прекращения Договора — недостаточный срок для коммерческой тайны. Рекомендуется увеличить до 5 лет или сделать бессрочным для ключевой информации.",
         "12.11. Риск по п. 9.2.1 (Одностороннее расторжение): 30 дней просрочки — разумный срок, но необходимо добавить право Заказчика привлечь третье лицо для завершения работ за счёт Исполнителя.",
         "12.12. Риск по п. 10.3 (Подсудность): Арбитражный суд г. Москвы — стандартная практика для московских компаний. Если Заказчик находится в другом регионе, рекомендуется указать суд по месту нахождения Заказчика."
      };

      foreach (var item in riskItems)
      {
         sb.AppendLine(item);
         sb.AppendLine();
      }

      // Add more filler to ensure the document is large enough for chunking.
      sb.AppendLine("## 13. ТЕХНИЧЕСКИЕ ТРЕБОВАНИЯ (СПРАВОЧНО)");
      sb.AppendLine();
      sb.AppendLine("13.1. Разрабатываемое ПО должно соответствовать следующим техническим требованиям:");
      sb.AppendLine();
      sb.AppendLine("- Архитектура: микросервисная, контейнеризация Docker, оркестрация Kubernetes.");
      sb.AppendLine("- Стек: .NET 10, C# 15, PostgreSQL 17, Redis, RabbitMQ.");
      sb.AppendLine("- Фронтенд: React 19, TypeScript 5.7, Next.js 15.");
      sb.AppendLine("- CI/CD: GitHub Actions, автоматическое тестирование (unit, integration, e2e).");
      sb.AppendLine("- Мониторинг: OpenTelemetry, Prometheus, Grafana, ELK Stack.");
      sb.AppendLine("- Безопасность: OWASP Top-10, пентест перед релизом, SAST/DAST сканирование.");
      sb.AppendLine("- Нагрузка: до 10 000 RPS, время ответа API ≤ 200ms (p95).");
      sb.AppendLine("- Доступность: 99.95% (SLA), автоматическое восстановление после сбоев.");
      sb.AppendLine("- Локализация: русский, английский, китайский языки интерфейса.");
      sb.AppendLine("- Доступность: WCAG 2.1 Level AA.");
      sb.AppendLine();
      sb.AppendLine("13.2. Все требования должны быть верифицированы на этапе приёмки. Несоответствие любому из требований является основанием для отказа в приёмке.");
      sb.AppendLine();
      sb.AppendLine("---");
      sb.AppendLine();
      sb.AppendLine("**Подписи сторон:**");
      sb.AppendLine();
      sb.AppendLine("| Исполнитель | Заказчик |");
      sb.AppendLine("|-------------|----------|");
      sb.AppendLine("| ___________ | ___________ |");
      sb.AppendLine("| М.П.        | М.П.        |");

      return sb.ToString();
   }
}
