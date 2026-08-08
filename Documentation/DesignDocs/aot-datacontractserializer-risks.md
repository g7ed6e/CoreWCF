# AOT-safe source-generated DataContractSerializer — design notes and risk register

Working notes for the `CoreWCF.DataContractSerialization` package. Kept in the repo rather than in a
pull request description so the reasoning survives the work.

## Why

`DataContractSerializer` discovers types by reflection and every one of its members is annotated
`[RequiresDynamicCode]` + `[RequiresUnreferencedCode]`, so a CoreWCF service cannot publish cleanly
with `PublishAot` or `PublishTrimmed`. The fix is a Roslyn generator that reads `[DataContract]` and
`[DataMember]` at compile time and emits a reflection-free serializer per type.

It is an **optimization with a fallback**, not a replacement. The generated path is selected by an
`AppContext` switch; with the switch off, CoreWCF behaves exactly as it does today.

## Milestone map

| | State |
| --- | --- |
| **M1 — the oracle** | Done (`55e77dfe3`, `5f7757409`). 75 corpus cases whose exact serialized bytes are recorded from the real serializer, a golden-record harness where adding a second serializer is one subclass, and the package/generator/corpus skeleton. |
| **M2 — first generator slice** | Done. `WriteObject` over flat contracts, behind the switch, gated to net8.0+. 3 of 75 corpus cases byte-match; the rest report unsupported and skip. |
| **M3 — capability by capability** | In progress. Nested contract members and inheritance, then enums, then arrays and `List<T>` of primitives, then `IsReference`, then `[KnownType]`/`i:type` and `object` members, then `[Serializable]`. **65 of 77** corpus cases byte-match; the rest report unsupported and skip. |
| **M4+ — deferred** | `Dictionary`/`ArrayList`/jagged arrays, `DateOnly`, enums in an `object` member, implicit (no-attribute) contracts, `[KnownType]` naming a method, `ISerializable`, `ReadObject`, and the seam gaps below. |

## What the switch does

`CoreWCF.Serialization.UseGeneratedDataContractSerializers`, resolved once into a `Lazy<bool>`:
explicit `AppContext` switch → assembly attribute emitted by the build targets → **default off,
except when `RuntimeFeature.IsDynamicCodeSupported` is false**, because under Native AOT the
reflection path is the broken one. This mirrors `Dispatcher/OperationInvokerBehavior.cs:18-42` and
`Dispatcher/InvokerUtil.cs:26-37`, which already gate the OperationInvoker generator the same way.

---

## The write algorithm, as it actually is

Recorded from `dotnet/runtime` at `bbfaee3bfa7edb0d556556bc32778d09a745134b`, under
`src/libraries/System.Private.DataContractSerialization/src/System/Runtime/Serialization/`. This is
reference material for reimplementation — nothing is copied. Where the generator implements one of
these rules it should cite the file in a comment.

### Member ordering — settled

`ClassDataContract.DataMemberComparer` (`ClassDataContract.cs:1476-1492`):

```csharp
int orderCompare = (int)(x.Order - y.Order);
if (orderCompare != 0) return orderCompare;
return string.CompareOrdinal(x.Name, y.Name);
```

So: **sort by `Order` ascending, then by `string.CompareOrdinal` on the *contract* name** (the
`Name=` override if present, otherwise the member name). Ordinal, not culture-aware.

`DataMemberAttribute._order` defaults to **-1**, and the setter throws `InvalidDataContractException`
for any negative value. Members without an explicit `Order` therefore always sort **before** every
ordered member, including `Order = 0` — a user cannot express -1. This closes an ambiguity the corpus
alone could not: it contains no case with an explicit `Order = 0`.

Base-class members come first because `ReflectionXmlClassWriter.ReflectionWriteMembers`
(`ReflectionXmlFormatWriter.cs:137-141`) recurses into `classContract.BaseClassContract` *before*
writing its own members. Ordering is per-contract, not across the whole flattened hierarchy.

### `EmitDefaultValue = false`

`ReflectionXmlFormatWriter.cs:160-176`. The member value is compared against the type's default; if
equal the element is skipped — **and if the member is also `IsRequired`, it throws** rather than
silently omitting. Worth an early diagnostic or a faithful runtime throw; silently omitting would be
a wire-level behaviour difference.

### Namespace prefixes are the writer's job, not the serializer's

The single most useful finding, and it removes most of the byte-exactness risk.

The serializer never allocates `a:`, `b:`, `d2p1:` itself. `ReflectionWriteStartElement`
(`ReflectionXmlFormatWriter.cs:206-218`) calls `WriteStartElement(nameLocal, namespaceLocal)` with no
prefix at all — the only exception is `XmlQualifiedName`, where `NeedsPrefix` forces
`Globals.ElementPrefix`. Prefix letters are assigned by `XmlDictionaryWriter` as namespaces come into
scope. **Emit the same calls in the same order and the prefixes fall out identically.**

The serializer explicitly declares a namespace in exactly two places:

1. **At the root**, via `XmlObjectSerializer.WriteRootElement` (`XmlObjectSerializer.cs:222-237`) —
   `contract.WriteRootElement(writer, name, ns)` followed by `writer.WriteNamespaceDecl(contract.Namespace)`
   when `CheckIfNeedsContractNsAtRoot` (`:240-255`) is true. That is true only when a root name was
   supplied explicitly (CoreWCF always supplies one, from the message part), the contract is not
   built-in, can contain references, is not `ISerializable`, and its namespace is non-empty and
   differs from the root namespace. This is the `xmlns:a="http://schemas.datacontract.org/2004/07/…"`
   seen on every fixture root.
2. **Per member**, via `classContract.ChildElementNamespaces[i + childElementIndex]` →
   `xmlWriter.WriteNamespaceDecl(...)` emitted *inside* the member's start element
   (`ReflectionXmlFormatWriter.cs:187-191`). This is why `xmlns:b` for collections, and `xmlns:z` in
   the nested-`IsReference` case, appear on the member element rather than the root.

`z:Id` / `z:Ref` are written as attributes with an explicit `Globals.SerPrefix`
(`XmlObjectSerializerWriteContext.cs:218,224`), which is why `z:` is stable rather than allocated.

### `[Serializable]`, and why it is not the same as an implicit contract

From the `else` branch of `hasDataContract` in `ClassDataContract.ImportDataMembers`:

- `[DataContract]` **wins** when a type carries both. `BaseSerializable` in the corpus is exactly
  that shape and is written from its `[DataMember]`s, not its fields.
- Otherwise every instance **field** takes part — public and non-public — excluding `[NonSerialized]`.
  Properties never do, at all.
- Each field keeps its own name, and the fields sort by the same `DataMemberComparer`. Serializable
  fields get `Order = 0` rather than the `-1` an unspecified `[DataMember]` gets; within a single
  contract they are all equal either way, so the sort reduces to ordinal by name.

This is not the implicit no-attribute contract that v1 excludes. `[Serializable]` is an explicit
opt-in with a defined member set; a bare POCO is inferred, and stays out of scope.

Two restrictions the generator adds, both to avoid claiming more than it can deliver:

- **Only types declared in source.** `[Serializable]` is everywhere in the framework — `Uri`,
  `ArrayList` and `Dictionary<,>` all carry it — and their wire format has nothing to do with their
  field layout. A type from metadata keeps whatever answer it had before.
- **Not `ISerializable`.** That takes over serialization entirely; it is a different write algorithm,
  not a different member list, and the fields are not what would go on the wire.

Non-public fields still decline, for the reason they always did: generated code lives in the
context's class and cannot reach another type's privates, however close by they are compiled.

### Object identity, as `IsReference` defines it

From `XmlObjectSerializerWriteContext.OnHandleIsReference` and `ObjectToIdCache`:

- Ids come from a **per-call** cache whose counter starts at **1**, so every document restarts at
  `i1`. Only objects whose contract is `IsReference` consume an id — which is why a
  reference-preserving member of a plain root is `i1` and not `i2`.
- Lookup is by **reference**, via `RuntimeHelpers.GetHashCode` and `==`, never `Equals`. A contract
  that overrides equality still gets one id per instance, and two equal-but-distinct instances must
  not collapse into a single `z:Ref`.
- First sight writes `z:Id` and the members; a later sight writes `z:Ref` and **nothing else** —
  `OnHandleIsReference` returning true means "already written". That, not a visited-set check, is
  what terminates a cycle.
- The decision belongs to the element that wraps the object, so it is taken once at the root or on
  the member element — never once per level of a base chain.
- `IsReference` is inherited, and a derived contract may restate it but not contradict its base:
  `ClassDataContractCriticalHelper.EnsureIsReferenceImported` throws `InvalidDataContractException`.
  It is also invalid on a value type. The generator declines both rather than emitting a serializer
  that would accept a contract the real one rejects.

The `z` prefix is never declared by the generator. Writing an attribute in the serialization
namespace makes the writer declare it wherever it first comes into scope, which reproduces both
fixture shapes on its own: `xmlns:z` on the root when the root contract is `IsReference`, and on the
member element when only a nested contract is.

---

## Risk register

Ordered by how much each threatens the milestone.

### 0. What the first slice actually proved

Reading the algorithm before writing the emitter worked: the first three cases matched byte for byte
on the first run, with no iteration on prefixes at all. The two findings that carried it were that
prefix allocation belongs to the writer, and that primitive formatting is `writer.WriteValue` with
exactly three exceptions (`char` as a number, `Guid`/`TimeSpan` via `WriteRaw`, `byte[]` as base64).

Two build-level traps cost more time than the serialization did, and are worth remembering:

- **The shipped `.targets` cannot be imported from a project body.** NuGet injects it after the SDK
  targets, where `TargetFrameworkIdentifier` is populated; an import in the body evaluates earlier,
  silently sees an empty value, and disables the generator with no error. In-repo consumers must
  mirror the gate conditioned on `$(TargetFramework)` instead.
- **`EmitCompilerGeneratedFiles` poisons the next build.** It writes under the project directory
  where the default glob compiles it as ordinary source, so every generated member is then declared
  twice. The test project excludes `generated/**`. Note also that cleanup can fail *silently* on
  these paths - they exceed `MAX_PATH`, and PowerShell's `Remove-Item` swallows it.

### 1. Byte-exactness — largely mitigated, not eliminated

The harness compares bytes with no canonicalisation, deliberately: a semantic XML diff would call
differently-prefixed but structurally identical documents equal, and wire compatibility is the whole
point. Reading the upstream algorithm converts most of this from reverse-engineering into porting,
and the finding that prefix allocation belongs to the writer removes the hardest part.

What remains: the emitted code must make the *same writer calls in the same order*. Any deviation —
an extra `WriteNamespaceDecl`, a differently-timed `WriteStartElement` — changes prefixes downstream.

### 2. ~~"Flat contracts" is a smaller slice than it sounds~~ — closed by M3

Roughly 10 of the 75 corpus cases. "Flat" does not mean "no nested types": `SanityCustomNaming` has a
contract-typed member, so the generator must already walk the transitive type graph and emit a
serializer per reachable contract. The milestone proves the loop end to end; it does not cover most
of the corpus.

M3 took it from 3 to 49 of 76 by adding one capability at a time. The prediction held: the
transitive-graph machinery built for the first slice is what everything since has been layered onto.

### 3. ~~Member ordering is under-determined~~ — closed

Settled above from `ClassDataContract.cs`. Add corpus cases pinning `Order = 0` and an ordinal-vs-
culture-sensitive name pair, so the oracle records the answer rather than only this document.

### 4. ~~Generated code must compile on net472~~ — closed by design

The generator is gated to net8.0+ by `CoreWCF.DataContractSerialization.targets`, so emitted code
never faces a net472 compiler and may use LangVersion 11. `DataContractSerializerContext.GetSerializer`
is `virtual` returning `null` rather than `abstract` so a user's `partial` context still compiles on
net472, where the generator never runs — falling back to reflection with no `#if` in user code.

Residual: on net472 the generated tests must report every case *unsupported*, not silently pass. A
pass because nothing ran looks identical to a pass because everything matched.

### 1a. A corpus case only tests the generator if the context lists its type

`SanityPrimitiveArrays` was added with the collections work and its fixture recorded, but the type
was never added to `GeneratedCorpusContext`. `GetSerializer` therefore returned null for it and
`GeneratedGoldenRecordTests` skipped it, with a skip reason that read exactly like every legitimately
unsupported case. The collections work looked verified and was not.

Worse, when the type was finally registered the generated code **did not compile**: `ulong` members
emitted `writer.WriteValue(value)`, which is ambiguous rather than missing, because ulong converts
implicitly to float, double and decimal and to none of them better than the others. Upstream avoids
it in `XmlWriterDelegator.WriteUnsignedLong` by going through `WriteRaw(XmlConvert.ToString(value))`
- a fourth `WriteRaw` case alongside Guid and TimeSpan.

Two things to keep in mind, neither of which the harness can currently catch on its own:

- **Registering a type in the corpus catalog is half the job.** Adding it to the context is the other
  half, and nothing fails if it is forgotten. A test asserting that every catalogued case's contract
  type appears in the context would close this, and should be written.
- **A compile error in generated code can present as csc exiting 1 with no diagnostic at all.**
  Building with `-p:EmitCompilerGeneratedFiles=true` made the same compilation succeed, which is what
  made the failure look like a compiler crash rather than a bug in the emitted source. When csc dies
  silently, dump the generated file and compile it as an ordinary source to get the real error.

### 4a. A cycle without `IsReference` overflows the stack instead of throwing

`DataContractSerializer` counts nesting depth and, past 512 levels, checks whether the object is
already on the stack and throws `CannotSerializeObjectWithCycles`
(`XmlObjectSerializerWriteContext.OnHandleReference`). The generator does not carry that counter, so
the same graph recurses until the stack runs out.

Only reachable when a **non**-`IsReference` contract holds a genuine cycle — a graph the real
serializer also refuses, so no valid document is affected and no corpus case exercises it. What
differs is the failure mode: a catchable `SerializationException` becomes a process-killing
`StackOverflowException`. Cheap to close (a depth counter threaded alongside the reference scope);
left open deliberately rather than by oversight, and worth closing before the package ships.

### 4b. An `object` member holding a collection throws rather than falling back

The generated switch for an `object` member covers the boxed primitives and whatever `[KnownType]`
names. DataContractSerializer is wider than that: arrays and collections are always allowed in an
object member without being declared. A graph that puts one there gets a `SerializationException`
from the generated path where the reflection path succeeds.

It cannot be a fallback: by the time the runtime type is known the member element is already open.
Closing it means writing collections into an object member, which needs the collection contract
naming rules the generator does not implement yet. `AllTypes.array1` is the corpus case waiting on
it, though that contract is blocked on several other things too.

### 5. The seam has gaps this work does not close

`CreateSerializer` is not a complete injection point. Even with a perfect generator, CoreWCF still
constructs a reflection-based `DataContractSerializer` directly in:

- `Dispatcher/PrimitiveOperationFormatter` — selected instead of the DataContract formatter for
  simple contracts, bypassing `CreateSerializer` entirely.
- `Dispatcher/FaultContractInfo.cs:42-58` — fault details, hard-typed to the concrete class.
- `Channels/Message.cs`, `MessageFault.cs`, `MessageHeader.cs`, `MessageHeaders.cs`,
  `AddressHeader.cs` — no injection point at all.
- The WS-Trust / secure-conversation token serializers under `Security/`.

An AOT smoke app will still warn until these are addressed. This work covers the operation body path.

### 6. Public API permanence

`AotXmlObjectSerializer` and the context types become public API on first release. Verified there is
**no** public-API baseline or approval test in this repo, so nothing enforces or records the surface —
which cuts both ways: no baseline to update, and no guard against accidental changes later.

### 7. `Lazy<bool>` caches on first read

A test that sets the switch after the first serializer is created silently gets the old value and the
generated path never runs — presenting as a **pass**, not a skip. Set it once per process, and have
the generated provider assert the switch took effect rather than trust it.

### 8. Incremental generator caching

The BuildTools generators hold raw `ISymbol` references in their specs and build them inside
`RegisterSourceOutput`, which defeats caching. This generator keeps symbols out of the specs and
builds them in `transform` before `Collect()`. Getting it wrong costs build time silently rather than
failing a test, and nothing verifies it.

---

## Open questions

- Naming: `AotXmlObjectSerializer` is a placeholder. It is public API, so worth settling before release.
- Should an unsupported contract shape be a build-time diagnostic, or a silent fallback to the
  reflection path at runtime? Currently planned as a diagnostic for clearly-wrong input (context not
  `partial`, type not a `[DataContract]`) and silent fallback for merely out-of-scope shapes.
- Where the context must live once a corpus type has a private `[DataMember]`. Today exactly one does
  (`BaseDCNoIsRef._data`, out of slice), so the context sits in the test project and generator bugs
  cannot break the corpus build that the reflection oracle depends on. That trade reverses the moment
  an in-slice case needs private member access.
