; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
KP001 | KetPsi.Endpoints.Http | Error | Source generation failed
KP002 | KetPsi.Endpoints.Http | Info | No endpoint to generate
KP101 | KetPsi.Endpoints.Http | Warning | Could not resolve method symbol
KP102 | KetPsi.Endpoints.Http | Error | Map method must be generic
KP103 | KetPsi.Endpoints.Http | Error | Endpoint missing ExecuteAsync method
KP104 | KetPsi.Endpoints.Http | Warning | Map call could not be resolved
KP105 | KetPsi.Endpoints.Http | Warning | Helper method has no call sites
KP106 | KetPsi.Endpoints.Http | Error | Expression cannot be re-emitted
KP107 | KetPsi.Endpoints.Http | Error | Parameter cannot be bound
KP108 | KetPsi.Endpoints.Http | Error | Keyed service requires a key
KP109 | KetPsi.Endpoints.Http | Error | Multiple body parameters
KP110 | KetPsi.Endpoints.Http | Error | Endpoint generator crashed
KP111 | KetPsi.Endpoints.Http | Warning | Configuration value must be constant
