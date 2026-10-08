using System.CommandLine;
using System.CommandLine.Invocation;
using System.Text.Json;
using Delibera.Core.Debate;
using Delibera.Core.Models;

namespace Delibera.Cli;

/// <summary>
///    Entry point for the <c>delibera</c> command-line tool.
/// </summary>
public static class Program
{
   /// <summary>Process entry point.</summary>
   /// <param name="args">Command-line arguments.</param>
   /// <returns>0 on success, 1 on a handled failure, 2 on a usage error.</returns>
   public static async Task<int> Main(string[] args)
   {
      var root = new RootCommand("delibera — run, compare and benchmark AI council debates from the shell.");
      root.Subcommands.Add(BuildRunCommand());
      root.Subcommands.Add(BuildResumeCommand());
      root.Subcommands.Add(BuildCompareCommand());
      root.Subcommands.Add(BuildBenchmarkCommand());

      var parse = root.Parse(args);
      return await parse.InvokeAsync().ConfigureAwait(false);
   }

   private static Command BuildRunCommand()
   {
      var question = new Argument<string>("question") { Description = "The question to put to the council." };
      var strategy = new Option<string>("--strategy", "-s") { Description = "Debate strategy.", DefaultValueFactory = _ => "standard" };
      var rounds = new Option<int>("--rounds", "-r") { Description = "Number of rounds.", DefaultValueFactory = _ => 3 };
      var temperature = new Option<float>("--temperature", "-t") { Description = "Sampling temperature 0..1.", DefaultValueFactory = _ => 0.7f };
      var output = new Option<string?>("--output", "-o") { Description = "Write the Markdown result to this path." };
      var stream = new Option<bool>("--stream") { Description = "Print each round as it completes." };
      var json = new Option<bool>("--json") { Description = "Emit machine-readable JSON instead of Markdown." };
      var verbose = new Option<bool>("--verbose", "-v") { Description = "Print execution logs." };

      var command = new Command("run", "Run a debate and print the result.")
      {
         question, strategy, rounds, temperature, output, stream, json, verbose
      };

      command.SetAction(async (parseResult, ct) =>
      {
         var questionText = parseResult.GetValue(question) ?? string.Empty;
         if (string.IsNullOrWhiteSpace(questionText))
         {
            Console.Error.WriteLine("A question is required.");
            return 2;
         }

         var opts = parseResult.GetValue(strategy) ?? "standard";
         var roundCount = parseResult.GetValue(rounds);
         var temp = parseResult.GetValue(temperature);
         var outputPath = parseResult.GetValue(output);
         var doStream = parseResult.GetValue(stream);
         var asJson = parseResult.GetValue(json);
         var isVerbose = parseResult.GetValue(verbose);

         try
         {
            var result = await DebateRunner.RunAsync(new DebateRunner.Request
            {
               Question = questionText,
               Strategy = opts,
               MaxRounds = roundCount,
               Temperature = temp
            }, doStream, isVerbose, ct).ConfigureAwait(false);

            if (asJson)
               Console.WriteLine(DebateRunner.ToJson(result));
            else
               Console.WriteLine(result.ToMarkdown());

            if (!string.IsNullOrWhiteSpace(outputPath))
            {
               await result.SaveToMarkdownAsync(outputPath, ct).ConfigureAwait(false);
               Console.Error.WriteLine($"Written to {outputPath}");
            }

            return 0;
         }
         catch (OperationCanceledException)
         {
            Console.Error.WriteLine("Debate cancelled.");
            return 1;
         }
         catch (Exception ex)
         {
            Console.Error.WriteLine($"Debate failed: {ex.Message}");
            return 1;
         }
      });

      return command;
   }

   private static Command BuildResumeCommand()
   {
      var debateId = new Argument<string>("debate-id") { Description = "Identifier of the debate to resume." };
      var storePath = new Option<string>("--store") { Description = "Checkpoint store directory.", DefaultValueFactory = _ => "debate_checkpoints" };
      var json = new Option<bool>("--json") { Description = "Emit machine-readable JSON instead of Markdown." };

      var command = new Command("resume", "Resume a debate from a stored checkpoint.")
      {
         debateId, storePath, json
      };

      command.SetAction(async (parseResult, ct) =>
      {
         var id = parseResult.GetValue(debateId) ?? string.Empty;
         if (string.IsNullOrWhiteSpace(id))
         {
            Console.Error.WriteLine("A debate id is required.");
            return 2;
         }

         try
         {
            var result = await DebateRunner.ResumeAsync(
               id,
               (parseResult.GetValue(storePath) ?? "debate_checkpoints"),
               ct).ConfigureAwait(false);

            if (result is null)
            {
               Console.Error.WriteLine($"No checkpoint found for debate '{id}' under '{(parseResult.GetValue(storePath) ?? "debate_checkpoints")}'.");
               return 1;
            }

            Console.WriteLine(parseResult.GetValue(json) ? DebateRunner.ToJson(result) : result.ToMarkdown());
            return 0;
         }
         catch (OperationCanceledException)
         {
            Console.Error.WriteLine("Resume cancelled.");
            return 1;
         }
         catch (Exception ex)
         {
            Console.Error.WriteLine($"Resume failed: {ex.Message}");
            return 1;
         }
      });

      return command;
   }

   private static Command BuildCompareCommand()
   {
      var left = new Argument<string>("baseline") { Description = "Path to the baseline result JSON." };
      var right = new Argument<string>("comparison") { Description = "Path to the result JSON to compare against it." };
      var output = new Option<string?>("--output", "-o") { Description = "Write the comparison to this path." };
      var html = new Option<bool>("--html") { Description = "Emit HTML instead of Markdown." };

      var command = new Command("compare", "Diff two saved debate results.")
      {
         left, right, output, html
      };

      command.SetAction(async (parseResult, ct) =>
      {
         var baselinePath = parseResult.GetValue(left) ?? string.Empty;
         var comparisonPath = parseResult.GetValue(right) ?? string.Empty;

         foreach (var path in new[] { baselinePath, comparisonPath })
         {
            if (!File.Exists(path))
            {
               Console.Error.WriteLine($"No such file: {path}");
               return 2;
            }
         }

         var a = DebateRunner.LoadResult(baselinePath);
         var b = DebateRunner.LoadResult(comparisonPath);
         if (a is null || b is null)
         {
            Console.Error.WriteLine("Both files must hold a serialized DebateResult. Produce them with `delibera run --json > file`.");
            return 2;
         }

         var diff = a.Diff(b);
         var text = parseResult.GetValue(html) ? diff.ToHtml() : diff.ToMarkdown();
         Console.WriteLine(text);

         var outputPath = parseResult.GetValue(output);
         if (!string.IsNullOrWhiteSpace(outputPath))
         {
            ct.ThrowIfCancellationRequested();
            await File.WriteAllTextAsync(outputPath, text, ct).ConfigureAwait(false);
            Console.Error.WriteLine($"Written to {outputPath}");
         }

         return 0;
      });

      return command;
   }

   private static Command BuildBenchmarkCommand()
   {
      var question = new Argument<string>("question") { Description = "The question to benchmark." };
      var iterations = new Option<int>("--iterations", "-n") { Description = "How many times to run.", DefaultValueFactory = _ => 3 };
      var rounds = new Option<int>("--rounds", "-r") { Description = "Rounds per run.", DefaultValueFactory = _ => 3 };

      var command = new Command("benchmark", "Run the same debate repeatedly and report timings.")
      {
         question, iterations, rounds
      };

      command.SetAction(async (parseResult, ct) =>
      {
         var questionText = parseResult.GetValue(question) ?? string.Empty;
         if (string.IsNullOrWhiteSpace(questionText))
         {
            Console.Error.WriteLine("A question is required.");
            return 2;
         }

         var runs = Math.Max(1, parseResult.GetValue(iterations));
         var report = await DebateRunner.BenchmarkAsync(
            questionText, runs, parseResult.GetValue(rounds), ct).ConfigureAwait(false);

         Console.WriteLine(report.ToMarkdown());
         return 0;
      });

      return command;
   }
}