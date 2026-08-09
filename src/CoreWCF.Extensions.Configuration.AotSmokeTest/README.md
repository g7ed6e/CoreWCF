# CoreWCF.Extensions.Configuration AOT smoke test

Publishes a CoreWCF host described entirely by `IConfiguration` with Native AOT and runs it, so the
premise `CoreWCF.Extensions.Configuration.Generator` exists for is executed rather than assumed.

Everything else in this area is verified under a normal runtime, where dynamic code is available and
nothing is trimmed. That is where a gap hides: without a generated context the reflective path answers,
every test passes, and the claim that a configured host needs no reflection goes unchecked until
somebody publishes one.

Not part of `dotnet test`. It proves something only once published.

## Running it

```
dotnet publish src/CoreWCF.Extensions.Configuration.AotSmokeTest/src -r win-x64 -c Release
./bin/Release/CoreWCF.Extensions.Configuration.AotSmokeTest/net10.0/win-x64/publish/CoreWCF.Extensions.Configuration.AotSmokeTest.exe
```

Use the runtime identifier for the machine you are on. On Windows the native link step needs
`vswhere.exe` on `PATH` — it ships at
`C:\Program Files (x86)\Microsoft Visual Studio\Installer` and a plain shell will not have it, which
presents as `error MSB3073` from `link.exe` rather than as anything to do with AOT.

Expected output:

```
IsDynamicCodeSupported: False
service: CoreWCF.Extensions.Configuration.AotSmokeTest.EchoService
contract: IEchoService, operations: 1
PASS
```

Exit code 0 on pass, 1 on failure.

## What it checks, and what it deliberately does not

It builds the application, lets `UseServiceModel` drain the configured endpoints into CoreWCF's service
model, and reads them back. That distinguishes *the host started* from *the configuration was
understood*: a host with no endpoints starts perfectly well.

Two assertions carry the weight.

**The service reached the service model.** Its type was named by a string in configuration and rooted
only by a `[ServiceModelConfigurable]` attribute, so it arriving at all is the whole type-resolution
story working with `Type.GetType` unavailable to it.

**The contract arrived with its operation.** `TypeLoader` finds a contract's operations by reflecting
over the interface, and nothing calls those methods statically — that is what a dispatcher is for.
Without the `[DynamicDependency]` the generator emits, the interface is trimmed to nothing and the
description comes back empty. This assertion is the one that fails if that emission is ever dropped.

`RequireGeneratedMetadata` is set explicitly, so a type the context does not cover throws rather than
falling back. It would already default to true here; saying so means a regression that reintroduces the
JIT is reported rather than quietly passing on the reflective path.

**It does not call the service.** Answering a SOAP call under Native AOT needs a serializer, and the
reflection-based `DataContractSerializer` is not one: the `feat/aot-datacontractserializer` work found
that it silently writes a truncated document before the contract types are rooted, and throws
`NullReferenceException` once they are. Calling through here would measure that gap rather than this
one. The call-through version belongs in this file once that branch lands.

## What it found

`TrimmerSingleWarn` is off, so every reflection dependency is reported against its call site rather
than rolled into one line. As of writing, publishing this app produces **353** trim and AOT warnings.
**10** of them are attributed to `CoreWCF.Extensions.Configuration`, and all ten are in
`ReflectionFallback.cs`:

| Id | Count | Call |
| --- | --- | --- |
| IL2070 | 5 | `GetProperties`, `GetInterfaces`, `GetProperty`/`GetField` over an unannotated `Type` |
| IL2067 | 2 | `Activator.CreateInstance` and the converter's target type |
| IL2026 | 1 | `TypeDescriptor.GetConverter` |
| IL2057 | 1 | `Type.GetType(string)` |
| IL3050 | 1 | `MakeGenericType` over `ICollection<T>` |

That the count is ten and the file is one is the result worth keeping. Nothing on the generated path
warns, and the library targets `netstandard2.0`, where `EnableTrimAnalyzer` does nothing and no build
gate would have said so.

The remaining 343 come from CoreWCF itself — the security stack, the channel proxy and the dispatcher —
and are not this area's to fix. Two are worth naming because they are load-bearing:

- **`CoreWCF.NetTcp` cannot be compiled by ILC at all.** Referencing it fails the publish with
  `Code generation failed for method 'CoreWCF.NetTcpBinding.CreateBindingElements()'`, a
  `BadImageFormatException` thrown from ILC's own generic cycle detector. That is why this app is HTTP
  only. The cause is `ConnectionIdWrappingLogger.Log<TState>`, which forwards to `ILogger.Log<T>` with
  `T` = `(TState, string, Func<TState, Exception, string>)` — its own type parameter inside a tuple, so
  the instantiation never bottoms out. The same file is in `CoreWCF.NetNamedPipe` and
  `CoreWCF.UnixDomainSocket`. Removing the recursion makes net.tcp publish and run; the investigation is
  written up in `Documentation/DesignDocs/aot-configuration-risks.md`.
- **Four `IL3054` for generic recursion** in `AndMessageFilterTable<T>`, aborted rather than expanded.
  ILC's own message says an exception will be thrown at run time if that path is reached. This app does
  not reach it.
