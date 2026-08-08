; Unshipped analyzer releases
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|--------------------
COREWCF_0400 | DataContractSerializerGenerator | Error | A class carrying [DataContractSerializable] is not partial, so nothing can be generated into it.
COREWCF_0401 | DataContractSerializerGenerator | Error | A class carrying [DataContractSerializable] does not derive from DataContractSerializerContext.
COREWCF_0402 | DataContractSerializerGenerator | Warning | A type listed in [DataContractSerializable] has no [DataContract] attribute.
