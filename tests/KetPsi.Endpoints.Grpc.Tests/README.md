# KetPsi.Endpoints.Grpc.Codec.Tests

Comprehensive unit tests for:

1. **Wire layer** (`ProtoWireReader` / `ProtoWireWriter`) — varint, fixed, string, bytes, skip, errors  
2. **Generated codecs** — round-trip read/write for all supported CLR types  
3. **Type resolution** — `/proto` schema matches [Microsoft gRPC protobuf mapping](https://learn.microsoft.com/en-us/aspnet/core/grpc/protobuf)

## Layout

```
KetPsi.Endpoints.Grpc.Codec.Tests/
  Fixtures/AllTypesMessages.cs   # DTOs + IProtoTypeConfiguration + handlers
  HostBootstrap.cs                 # MapGrpc* so the generator emits codecs
  ProtoWireReaderWriterTests.cs
  GeneratedCodecRoundtripTests.cs
  SchemaTypeResolutionTests.cs
```

## Project references

Adjust paths in the `.csproj` if needed:

- `..\src\grpc\KetPsi.Endpoints.Grpc\KetPsi.Endpoints.Grpc.csproj`
- `..\src\grpc\KetPsi.Endpoints.Grpc.Abstractions\...`
- Generator as `OutputItemType="Analyzer"`

## Run

```bash
dotnet test KetPsi.Endpoints.Grpc.Codec.Tests.csproj
```

`MapGrpc*` call sites in `HostBootstrap` / `SchemaTypeResolutionTests` are required so the source generator emits `GeneratedProtoCodecs` and `UseProtoEndpoint`.
