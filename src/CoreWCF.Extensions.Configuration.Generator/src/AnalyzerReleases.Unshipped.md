; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|--------------------
COREWCF_0600 | ServiceModelConfigurationGenerator | Error | A class carrying [ServiceModelConfigurable] is not partial.
COREWCF_0601 | ServiceModelConfigurationGenerator | Error | A class carrying [ServiceModelConfigurable] does not derive from ServiceModelConfigurationContext.
COREWCF_0602 | ServiceModelConfigurationGenerator | Error | Two listed types claim the same configuration name.
COREWCF_0603 | ServiceModelConfigurationGenerator | Warning | A listed type is not accessible from the generated context.
COREWCF_0604 | ServiceModelConfigurationGenerator | Warning | A listed type has no accessible parameterless constructor.
COREWCF_0605 | ServiceModelConfigurationGenerator | Warning | The property graph was truncated, so a nested type falls back to reflection.
COREWCF_0606 | ServiceModelConfigurationGenerator | Warning | A member type has no compile-time conversion from a configuration string.
COREWCF_0607 | ServiceModelConfigurationGenerator | Warning | CustomBinding is listed but no concrete BindingElement is.
