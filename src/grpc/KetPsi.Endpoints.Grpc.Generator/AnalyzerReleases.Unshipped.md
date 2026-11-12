; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
DG001 | Routing | Error | Invalid gRPC route (segments, identifiers, MapGroup-aware)
KPG001 | KetPsi.Grpc | Warning | Route is not a compile-time constant
KPG003 | KetPsi.Grpc | Error | Duplicate gRPC method mapping
KPG004 | KetPsi.Grpc | Error | Unsupported handler signature
KPG005 | KetPsi.Grpc | Error | Handler signature does not match MapGrpc variant
KPG006 | KetPsi.Grpc | Error | Convention references local state
KPG007 | KetPsi.Grpc | Warning | MapGrpc result not chained
KPG008 | KetPsi.Grpc | Warning | configure argument is not an inline lambda
KPG010 | KetPsi.Grpc | Error | Unsupported property type
KPG011 | KetPsi.Grpc | Warning | Property not serialized
KPG012 | KetPsi.Grpc | Error | Duplicate field number
KPG013 | KetPsi.Grpc | Error | Invalid field number
KPG014 | KetPsi.Grpc | Warning | Field number not a compile-time value
KPG015 | KetPsi.Grpc | Info | Implicit field numbers (declaration order)
KPG016 | KetPsi.Grpc | Error | Message cannot be constructed
KPG017 | KetPsi.Grpc | Warning | Property cannot be set when deserializing
KPG018 | KetPsi.Grpc | Error | Invalid map key type
KPG019 | KetPsi.Grpc | Error | Enum value outside int32 range
KPG020 | KetPsi.Grpc | Error | Invalid metadata binding
KPG022 | KetPsi.Grpc | Warning | Configured property not found
KPG023 | KetPsi.Grpc | Error | Unsupported message type
