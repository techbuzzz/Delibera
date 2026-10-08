# Delibera.Grpc.Client

Generated gRPC client stubs for [Delibera](https://github.com/techbuzzz/Delibera) — the multi-model AI council framework.

Reference this package to talk to a Delibera server over gRPC without declaring the protobuf schema
yourself. The stubs are generated from the same `.proto` the server compiles, so a message added on one
side breaks the build on the other rather than failing at runtime.

## Install

```bash
dotnet add package Delibera.Grpc.Client
```

## Use

```csharp
using Delibera.Grpc.V1;
using Grpc.Net.Client;

using var channel = GrpcChannel.ForAddress("http://localhost:5200");
var debates = new DebateService.DebateServiceClient(channel);
var scenarios = new ScenarioService.ScenarioServiceClient(channel);
var corpora = new CorpusService.CorpusServiceClient(channel);

var created = await debates.CreateDebateAsync(new CreateDebateRequest
{
    Question = "Should we migrate the monolith?",
    Options = new DebateOptionsOverride { MaxRounds = 4 }
});

using var stream = debates.StreamDebate(new GetDebateRequest { DebateId = created.DebateId });

await foreach (var evt in stream.ResponseStream.ReadAllAsync())
{
    if (evt.KindCase == DebateStreamEvent.KindOneofCase.Round)
        Console.WriteLine($"round {evt.Round.RoundNumber}");
}
```

## A stream always ends with one terminal event

Either `Completed` or `Error` — never both, never neither. That is what lets you tell a finished debate
from a dropped connection, so always read until the stream ends rather than breaking after the last
round.

## Why this package is separate from `Delibera.Grpc`

The two compile the same `.proto` independently rather than one referencing the other. Referencing the
server package *and* generating here produces two copies of every message type, and a caller ends up
holding two structurally identical but nominally different `DebateResponse` types. Keeping them apart
avoids that and keeps the client free of server-side dependencies.

## Dependency

None — this package is contract only. `Delibera.Grpc` carries the server side and depends on
`Delibera.Core`.

## Licence

MIT. See the repository root for details.