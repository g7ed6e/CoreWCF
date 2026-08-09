# AOT-safe configuration hydration — design notes and risk register

Companion to `aot-datacontractserializer-risks.md`, and deliberately shaped like it. That work makes a
CoreWCF service *serialize* without dynamic code; this one makes it *start* without dynamic code when
its services and endpoints are declared in `IConfiguration`.

## Why

`CoreWCF.Extensions.Configuration` (PR #1762) lets bindings, services and endpoints be declared in
`IConfiguration`:

```jsonc
"ServiceModel": {
  "Bindings": { "internal": { "Type": "CoreWCF.NetTcpBinding, CoreWCF.NetTcp", "Security": { "Mode": "Transport" } } },
  "Services": { "Contoso.EchoService, Contoso.Services": { "Endpoints": [ { "Contract": "...", "Binding": "internal", "Address": "..." } ] } }
}
```

Every mechanism that makes that work is one a trimmer cannot follow, and one of them NativeAOT cannot
execute at all:

| Site | Call | Verdict |
|---|---|---|
| Type resolution | `Type.GetType(assemblyQualifiedName)` | IL2057 — the type is named only by a string, so nothing references it and the trimmer removes it before the string is read |
| Creation | `Activator.CreateInstance(Type)` | IL2067 |
| Property binding | `GetProperty` + `GetValue`/`SetValue` | IL2075 |
| Collection append | `typeof(ICollection<>).MakeGenericType(t).GetMethod("Add").Invoke` | **IL3050 — a hard failure under AOT**, not a warning |
| Value conversion | `TypeDescriptor.GetConverter` | IL2026 |
| Vocabulary values | static `GetProperty`/`GetField` on the target type | IL2075 |

None of it can be annotated into safety. The goal is therefore not that the feature be AOT-*compatible*
by annotation, but that it not be the reason CoreWCF cannot be: every call above gets a generated,
statically-rooted counterpart, and the reflective one survives as an explicit, reportable fallback.

## The shape

Modelled on `DataContractSerializerContext` next door, so the repository has one convention rather than
two. A user declares a partial class listing the types their configuration names:

```csharp
[ServiceModelConfigurable(typeof(NetTcpBinding), Name = "netTcp")]
[ServiceModelConfigurable(typeof(EchoService))]
[ServiceModelConfigurable(typeof(IEchoService))]
public partial class MyServiceModel : ServiceModelConfigurationContext { }

services.AddServiceModelConfiguration(configuration.GetSection("ServiceModel"), new MyServiceModel());
```

`ResolveType` and `GetConfiguredType` on the base are `virtual … => null`, never `abstract`. The
generator is gated to `.NETCoreApp >= 8.0`, so on net472 and netstandard it never runs — and the same
source still compiles, contributes nothing, and falls back. No conditional compilation in user code.

### One traversal, two sources

`ConfiguredType` and `ConfiguredMember` are the currency. `ConfigurationObjectBinder` walks the
configuration keys and never learns whether the members it is using came from the generator or from
reflection. That is what stops this from becoming a generated implementation and a reflective one that
have to be kept agreeing.

### Listing a binding is the whole ceremony

The parser walks a listed type's settable property graph transitively. `NetTcpBinding` alone reaches
`NetTcpSecurity`, `TcpTransportSecurity`, `MessageSecurityOverTcp`, `XmlDictionaryReaderQuotas`,
`MessageVersion`, `EnvelopeVersion` and `AddressingVersion`. The user lists what a *configuration file*
names; the walk finds what *hydrating it* touches.

Only bindings and binding elements are walked. A service implementation is never hydrated — it is a
`Type` handed to `ServiceModelOptions.ConfigureService` — so it is rooted and otherwise left alone.
Walking a service class's properties would generate metadata nobody reads.

### The four types with no converter, generalised

`MessageVersion`, `EnvelopeVersion`, `SecurityAlgorithmSuite` and `MessageSecurityVersion` have no
`TypeConverter` at all, which is why `CoreWCF.ConfigurationManager` carries a hand-written converter per
type. They share a trait: their well-known values are public static members on the type itself. The
generator enumerates the public static members whose type is assignable to the declaring type and emits
a lookup table, so those four and anything shaped like them are covered without anyone writing a
converter — including types added later.

### `[DynamicDependency]` is not decoration

`TypeLoader` finds a contract's operations by reflecting over the interface, and nothing calls those
methods statically — that is what a dispatcher is for. The `aot-datacontractserializer` work established
that without a `[DynamicDependency]` the interface arrives with **zero operations** and the host refuses
to start. The generator emits one per listed service and contract, on the `ResolveType` override,
because a `DynamicDependency` only takes effect if the member carrying it is itself kept.

## Falling back is visible, and still a fallback

Adopted verbatim from the sibling doc, for the same reason: the reflective path is correct where dynamic
code exists and *broken* where it does not, so a silent fallback is a build that looks clean and throws
at run time.

- **Build time.** `COREWCF_0605` (graph truncated), `COREWCF_0606` (no compile-time conversion) and
  `COREWCF_0607` (a `CustomBinding` with no listed `BindingElement`) are warnings, not errors. A
  fallback is correct behaviour and failing the build would make adopting this one type at a time
  impossible.
- **Run time.** `ServiceModelConfigurationOptions.RequireGeneratedMetadata` turns a miss into an
  exception naming the `[ServiceModelConfigurable]` line to add. It defaults to true exactly when the
  runtime does not support dynamic code, probed through the same `AppContext` switch `InvokerUtil` and
  `DispatchOperationRuntimeHelpers` already use.

The message has to carry the fix because the situation it reports cannot be reproduced on the machine
someone reads it on: it only arises where dynamic code is unavailable.

## Diagnostics

`COREWCF_06XX`. 01XX–03XX are CoreWCF.BuildTools, 04XX the DataContractSerializer generator, and 05XX is
left clear for `feat/aot-servicedescription-generator`.

| Id | Severity | Fires when |
|---|---|---|
| `COREWCF_0600` | Error | A class carrying `[ServiceModelConfigurable]` is not partial |
| `COREWCF_0601` | Error | It does not derive from `ServiceModelConfigurationContext` |
| `COREWCF_0602` | Error | Two listed types claim one configuration name |
| `COREWCF_0603` | Warning | A listed type is not accessible from the context |
| `COREWCF_0604` | Warning | A listed type has no accessible parameterless constructor |
| `COREWCF_0605` | Warning | The property graph was truncated |
| `COREWCF_0606` | Warning | A member type has no compile-time conversion |
| `COREWCF_0607` | Warning | `CustomBinding` is listed but no concrete `BindingElement` is |

`COREWCF_0602` is worth singling out. Every listed type is registered under its bare full name as well
as its assembly qualified one, and CoreWCF ships client and server halves of the queue transports as
deliberate homonyms — `CoreWCF.Channels.KafkaBinding` against
`CoreWCF.ServiceModel.Channels.KafkaBinding`. Two types claiming one name is therefore reachable, and
answering it by load order is precisely what this package's design refuses to do. Moving that question
to compile time, where it names both types and asks for a `Name`, is strictly better than the runtime
answer the package started with.

## The premise, executed

`src/CoreWCF.Extensions.Configuration.AotSmokeTest` publishes a host described only by configuration
with `PublishAot` and runs it. Not part of `dotnet test`; its README says how.

**It passes.** `IsDynamicCodeSupported` false, the service named only by a string reached the service
model, the contract arrived with its operation, and `RequireGeneratedMetadata` was on throughout — so no
reflective hydration happened anywhere.

### The quarantine held

`TrimmerSingleWarn` off, so every dependency is reported against its call site. **353** trim and AOT
warnings. **10** are attributed to `CoreWCF.Extensions.Configuration`, and all ten are in
`ReflectionFallback.cs`: five IL2070, two IL2067, one IL2026, one IL2057, one IL3050. Nothing on the
generated path warns.

That is the result the single-file arrangement exists for. The library targets `netstandard2.0`, where
`RequiresUnreferencedCodeAttribute` does not exist and `EnableTrimAnalyzer` does nothing, so no build
gate would have reported a reflection call appearing somewhere else.

### `CoreWCF.NetTcp` cannot be compiled by ILC

Referencing it fails the publish outright with
`Code generation failed for method 'CoreWCF.NetTcpBinding.CreateBindingElements()'`, a
`BadImageFormatException: Read out of bounds` thrown from ILC's own
`LazyGenericsSupport.GenericCycleDetector`. Not a warning, not a run-time failure — the publish does not
complete. That is why the smoke test is HTTP only, and it is a CoreWCF bug rather than a limitation of
AOT.

---

## Risk register

Ordered by how much each threatens the goal.

### 1. The graph walk is a heuristic, and its stop rules are hand-picked

`s_leafTypes` and `s_opaqueTypes` are lists someone wrote. A binding property whose type is not in
either and is not worth walking generates metadata nothing reads; one that *is* worth walking and gets
excluded falls back silently on that member. `System.Xml.XmlDictionaryString` was already found and
added to the opaque set this way — reached from `EnvelopeVersion.DictionaryNamespace`, hydrated by
nobody.

The failure mode is not correctness — the fallback still works — but the strict-mode promise is only as
complete as these lists. What would close it: assert in the strict end-to-end run that the emitted type
set is exactly the set the binder asks for. Nothing does that today.

### 2. `COREWCF_0603` has no test

Accessibility can only be violated across an assembly boundary, and the generator's test harness compiles
a single file into a single assembly. The diagnostic is exercised by nothing. A second compilation added
as a metadata reference would fix it, and is the obvious next piece of harness work.

### 3. The depth cap is arbitrary

`MaxGraphDepth = 8`, chosen to be comfortably past the three or four levels a binding actually reaches.
Nothing measures the real depths, so it is a guess with a diagnostic attached rather than a bound with a
reason. Hitting it is at least reported (`COREWCF_0605`) on the listed type rather than silently.

### 4. Hand-written conversions are near-parity, not parity

`ConfigurationValueConverter` converts a closed set of BCL types directly, which is what keeps
`TypeDescriptor` off the common path. One known narrowing: the numeric `TypeConverter`s also accept a
`0x`- or `#`-prefixed hexadecimal literal, and these do not. No binding property is configured that way,
but this is a behaviour change rather than a refactor, and it is not covered by a test.

`TimeSpan` is `TimeSpan.Parse`, matching what `TypeDescriptor` did — which means `"Infinite"` is still
not accepted, as it was not before. `CoreWCF.ConfigurationManager` has a `TimeSpanOrInfiniteConverter`
for exactly this; adopting it here would be an improvement and a separate decision.

### 5. Public API permanence

`ServiceModelConfigurationContext`, `ConfiguredType`, `ConfiguredMember`,
`ServiceModelConfigurableAttribute` and `ServiceModelConfigurationOptions` become public API on first
release. Verified there is **no** public-API baseline or approval test in this repository, so nothing
enforces or records the surface — which cuts both ways: no baseline to update, and no guard against
accidental change later.

Two shapes here are load-bearing and expensive to change afterwards. `ConfiguredType`'s delegates are
the contract between the generator and the binder; and `ServiceModelTypeRegistry` was reduced to a name
map, with resolution moved to an internal `ServiceModelTypeResolver`, specifically so that its public
surface no longer promises `Type.GetType` semantics.

### 6. `Lazy<bool>` caches on first read

`RuntimeFeatureSwitches.IsDynamicCodeSupported` is read once per process. A test that sets the
`AppContext` switch after something else has already read it gets the old value and presents as a
**pass**, not a skip. The same risk the sibling doc records, and the same mitigation: set it once per
process, before the first configuration is read. `RequireGeneratedMetadata` is settable explicitly for
exactly this reason, and the strict tests set it rather than relying on the probe.

### 7. Incremental generator caching is unverified

Parsing happens inside `transform` and no `ISymbol` travels in a spec, which is what CoreWCF.BuildTools'
generators get wrong. But nothing asserts it. Getting this wrong costs build time silently rather than
failing a test.

### 8. The smoke test does not call the service

Scoped to startup deliberately — see above — so it says nothing about whether a configured endpoint can
answer under AOT. It cannot, today, for reasons that belong to serialization. When
`feat/aot-datacontractserializer` lands, the call-through version is a few lines in the same file and
should be added.

## Open questions

- Whether `AddServiceModelConfiguration` should take the context or read it from DI. It currently does
  both, with the parameter winning; that is convenient and slightly redundant.
- Whether a `CustomBinding` with no listed elements should be an error rather than a warning. It is
  generated metadata that hydrates nothing, which is closer to a mistake than to a fallback.
- Whether to root the `IServiceConfiguration<TService>` and `ServiceHostObjectModel<TService>` generic
  instantiations the `ConfigureService(Type, …)` path reaches through `MakeGenericType`. The smoke test
  passes without it, so nothing forces the question yet, but it is CoreWCF's `MakeGenericType` rather
  than this package's and the fix may belong there.
