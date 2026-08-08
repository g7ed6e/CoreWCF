# Imported from dotnet/runtime

Contract types in the `SerializationTestTypes` namespace are copied from the .NET runtime's own
`System.Runtime.Serialization.Xml` test suite. Reusing upstream types rather than inventing new ones
inherits years of accumulated edge-case coverage, and keeps our fixtures comparable with the values
upstream asserts.

**Pinned commit:** `bbfaee3bfa7edb0d556556bc32778d09a745134b` (2025-09-22)
**Upstream path:** `src/libraries/System.Runtime.Serialization.Xml/tests/SerializationTestTypes/`
**Licence:** MIT, .NET Foundation - the same licence and header text CoreWCF uses, so files are
carried with their header unchanged.

Always pin a commit SHA, never `main`: an upstream edit to a populating constructor would otherwise
silently invalidate every affected fixture.

## Files

| File | Upstream | Notes |
| --- | --- | --- |
| `Primitives.cs` | `SerializationTestTypes/Primitives.cs` | Imported in full. |
| `_ImportSupport.cs` | extracted declarations | Two symbols `Primitives.cs` references but does not declare. |

`Primitives.cs` is **not** self-contained. Rather than import the files that declare its two missing
symbols - `ObjRefSample.cs` is `IObjectReference`/`ISerializable` territory and `SampleTypes.cs` is
163 KB of `DataContractResolver`-oriented material, both out of scope for v1 - only the referenced
declarations are reproduced in `_ImportSupport.cs`, verbatim and with per-symbol provenance links:

- `IgnoreMemberAttribute` (from `ObjRefSample.cs`) - an empty marker upstream's `ComparisonHelper`
  uses to skip a member during object-graph comparison. It has no effect on serialization; this
  corpus compares XML bytes, not object graphs, so it exists purely so `Primitives.cs` compiles
  unmodified.
- `PublicDCStruct` (from `SampleTypes.cs`) - a nine-line struct used as a known type of `AllTypes`.

When either file is eventually imported in full, delete the corresponding declaration from
`_ImportSupport.cs`.

## Local modifications

Every local edit carries a `// CoreWCF:` marker so a diff against upstream stays readable.

| Location | Change | Why |
| --- | --- | --- |
| `Primitives.cs`, `DateTimeOnlyWrapper` | Wrapped in `#if !NETFRAMEWORK` | `DateOnly`/`TimeOnly` do not exist on .NET Framework, and the corpus spans `$(TestTargetFrameworks)`, which still includes net472 on Windows. `CorpusCatalog.Primitives.cs` carries the same guard, so the case is excluded rather than silently dropped. |

No type was deleted on import.

## Deferred upstream files

| File | Size | Why not yet |
| --- | --- | --- |
| `SampleTypes.cs`, `DataContract.cs` | 244 KB | The coupled core; import together or not at all. |
| `DCRTypeLibrary.cs`, `DataContractResolverLibrary.cs`, `DCRSampleType.cs`, `DCRImplVariations.cs` | 72 KB | `DataContractResolver` is out of scope for v1. |
| `ObjRefSample.cs`, `SampleIObjectRef.cs`, `InheritanceObjectRef.cs`, `SelfRefAndCycles.cs` | 35 KB | `IObjectReference` and object-graph preservation are out of scope for v1. |
| `Collections.cs` | 12.5 KB | `[CollectionDataContract]` is a generator subsystem of its own; deferred deliberately. |
| `InheritanceCases.cs` | 17 KB | In scope, and the natural next import. |
| `SerializationTypes.cs`, `SerializationTypes.RuntimeOnly.cs` | 173 KB | Different namespace; `RuntimeOnly` is reflection-only behaviour by name, the opposite of what a source generator targets. |
| `ComparisonHelper.cs` | 27 KB | **Never needed.** It compares object graphs; our oracle is XML bytes. |

## Re-syncing

1. Download the file at a new SHA and diff it against the copy here, ignoring the provenance header.
2. Re-apply any row from *Local modifications*.
3. Update the SHA above.
4. Regenerate fixtures and **read the diff**. An upstream change to a populating constructor is a
   legitimate fixture change; an unexplained one is a bug.
