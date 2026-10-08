# Delibera.Grpc

gRPC transport for [Delibera](https://github.com/techbuzzz/Delibera) — the multi-model AI council framework.

The REST surface already speaks HTTP/1.1 with SSE for streaming debates. This package adds the same
capability over HTTP/2 and protobuf, for callers that want a typed client or a bidirectional stream
rather than an event stream.

## What it gives you

| Service | RPCs |
|---|---|
| `DebateService` | `CreateDebate`, `GetDebate`, `ListDebates`, `CancelDebate`, `StreamDebate` (server streaming) |
| `ScenarioService` | `RunScenario`, `ValidateScenario` |
| `CorpusService` | `ListCorpora`, `ListDocuments`, `IndexDocument` |

`Protos/delibera.proto` is the source of truth. It is derived from the contracts the REST host already
validates, so the two transports cannot disagree about request shape.

## Hosting

```csharp
using Delibera.Core.Council;
using Delibera.Grpc.Services;
using Delibera.Grpc.V1;

builder.Services.AddGrpc();
builder.Services.AddSingleton<IDebateOrchestrator, LocalDebateOrchestrator>();
builder.Services.AddSingleton(sp => new DebateServiceImpl(
    sp.GetRequiredService<IDebateOrchestrator>(),
    () => BuildCouncil(),                     // a builder per call: the orchestrator owns execution
    sp.GetRequiredService<ILogger<DebateServiceImpl>>()));

var app = builder.Build();
app.MapGrpcService<DebateServiceImpl>();
app.Run();
```

## Calling it

```csharp
using var channel = GrpcChannel.ForAddress("http://localhost:5200");
var client = new DebateService.DebateServiceClient(channel);

var created = await client.CreateDebateAsync(new CreateDebateRequest
{
    Question = "Should we migrate the monolith?",
    Options = new DebateOptionsOverride { MaxRounds = 4, Temperature = 0.7f }
});

using var stream = client.StreamDebate(new GetDebateRequest { DebateId = created.DebateId });

await foreach (var evt in stream.ResponseStream.ReadAllAsync())
{
    switch (evt.KindCase)
    {
        case DebateStreamEvent.KindOneofCase.Round:
            Console.WriteLine($"round {evt.Round.RoundNumber}: {evt.Round.Responses.Count} answers");
            break;

        case DebateStreamEvent.KindOneofCase.Completed:
            Console.WriteLine("done");
            break;

        case DebateStreamEvent.KindOneofCase.Error:
            Console.WriteLine($"failed: {evt.Error.Message}");
            break;
    }
}
```

## Two things worth knowing

**The service sits on top of `IDebateOrchestrator`, not beside it.** That is the point of the
abstraction: a debate executed over gRPC takes the same distributed-execution and result-caching
path as one executed over REST. A parallel gRPC pipeline would silently ignore `WithOrchestrator`
and `WithCache`, and the same council would behave differently depending on the transport.

**Streaming ends with exactly one terminal event.** Either `completed` or `error`. A client must be
able to distinguish a finished debate from a dropped connection, and it can, because the service
writes `completed` or `error` before returning — never both, never neither. Iteration is a plain
`await foreach`: one `MoveNextAsync` in flight at a time. The SSE writer shipped a defect for exactly
that reason — it compared a winner against a second `ValueTask.AsTask()` call, which returns a
*different* `Task` instance, so every genuine event was misread as a heartbeat and a debate streamed
zero rounds.

`GetDebate` returns the status only. Round bodies are not returned there: the orchestrator exposes
progress as a stream and the durable transcript through the debate store, which the REST surface
reads. A response whose `rounds` list was always empty would read as "this debate produced no rounds".

## Status ordinals

`DebateStatus` values are pinned explicitly in the proto. REST serialises the same states as
**strings**, so nothing in either build would stop the two transports from disagreeing about which
number means which state — which is why the pairing is covered by a test.

## Dependency

Depends on `Delibera.Core`. Client stubs live in the separate `Delibera.Grpc.Client` package; both
compile the same `.proto`, which keeps them in step at build time rather than at runtime.

## Licence

MIT. See the repository root for details.