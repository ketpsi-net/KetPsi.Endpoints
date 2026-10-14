using KetPsi.Endpoints.Grpc;
using KetPsi.Endpoints.Grpc.Tests.Fixtures;

using Xunit;

namespace KetPsi.Endpoints.Grpc.Tests;

/// <summary>
/// Round-trips for primary-ctor records, ctor+private-set classes, init-only records, and Dictionary maps.
/// Requires MapGrpc* of the corresponding handlers so GeneratedProtoCodecs emits the types.
/// </summary>
public class ConstructionAndMapCodecTests
{
    static ConstructionAndMapCodecTests()
    {
        _ = typeof(PrimaryCtorOrderHandler);
        _ = typeof(PrivateSetterOrderHandler);
        _ = typeof(InitOnlyOrderHandler);
        _ = typeof(MapOnlyHandler);
    }

    [Fact]
    public void PrimaryCtorRecord_RoundTrip()
    {
        var id = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var original = new PrimaryCtorOrder("SKU-9", 4, id);

        var copy = GeneratedProtoCodecs.Deserialize_PrimaryCtorOrder(
            GeneratedProtoCodecs.Serialize_PrimaryCtorOrder(original));

        Assert.Equal(original.Sku, copy.Sku);
        Assert.Equal(original.Quantity, copy.Quantity);
        Assert.Equal(original.OrderId, copy.OrderId);
    }

    [Fact]
    public void PrimaryCtorRecord_EmptySku_RoundTrip()
    {
        var original = new PrimaryCtorOrder("", 0, Guid.Empty);
        var copy = GeneratedProtoCodecs.Deserialize_PrimaryCtorOrder(
            GeneratedProtoCodecs.Serialize_PrimaryCtorOrder(original));

        Assert.Equal("", copy.Sku);
        Assert.Equal(0, copy.Quantity);
        Assert.Equal(Guid.Empty, copy.OrderId);
    }

    [Fact]
    public void PrivateSetterClass_RoundTrip()
    {
        var original = new PrivateSetterOrder("ABC", 12);

        var copy = GeneratedProtoCodecs.Deserialize_PrivateSetterOrder(
            GeneratedProtoCodecs.Serialize_PrivateSetterOrder(original));

        Assert.Equal("ABC", copy.Sku);
        Assert.Equal(12, copy.Quantity);
    }

    [Fact]
    public void InitOnlyRecord_RoundTrip()
    {
        var original = new InitOnlyOrder
        {
            Sku = "INIT",
            Quantity = 7,
            Status = SampleStatus.Pending
        };

        var copy = GeneratedProtoCodecs.Deserialize_InitOnlyOrder(
            GeneratedProtoCodecs.Serialize_InitOnlyOrder(original));

        Assert.Equal("INIT", copy.Sku);
        Assert.Equal(7, copy.Quantity);
        Assert.Equal(SampleStatus.Pending, copy.Status);
    }

    [Fact]
    public void Dictionary_StringInt_RoundTrip()
    {
        var original = new MapOnlyMessage
        {
            Scores = new Dictionary<string, int>
            {
                ["alice"] = 10,
                ["bob"] = 20,
                [""] = 0
            }
        };

        var copy = GeneratedProtoCodecs.Deserialize_MapOnlyMessage(
            GeneratedProtoCodecs.Serialize_MapOnlyMessage(original));

        Assert.NotNull(copy.Scores);
        Assert.Equal(3, copy.Scores.Count);
        Assert.Equal(10, copy.Scores["alice"]);
        Assert.Equal(20, copy.Scores["bob"]);
        Assert.Equal(0, copy.Scores[""]);
    }

    [Fact]
    public void Dictionary_IntString_RoundTrip()
    {
        var original = new MapOnlyMessage
        {
            Labels = new Dictionary<int, string>
            {
                [1] = "one",
                [2] = "two"
            }
        };

        var copy = GeneratedProtoCodecs.Deserialize_MapOnlyMessage(
            GeneratedProtoCodecs.Serialize_MapOnlyMessage(original));

        Assert.Equal(2, copy.Labels.Count);
        Assert.Equal("one", copy.Labels[1]);
        Assert.Equal("two", copy.Labels[2]);
    }

    [Fact]
    public void Dictionary_StringNestedMessage_RoundTrip()
    {
        var original = new MapOnlyMessage
        {
            ItemsByKey = new Dictionary<string, NestedItem>
            {
                ["a"] = new NestedItem { Id = 1, Name = "alpha", Amount = 1.5m, Status = SampleStatus.Active },
                ["b"] = new NestedItem { Id = 2, Name = "beta", Amount = 2.5m, Status = SampleStatus.Inactive }
            }
        };

        var copy = GeneratedProtoCodecs.Deserialize_MapOnlyMessage(
            GeneratedProtoCodecs.Serialize_MapOnlyMessage(original));

        Assert.Equal(2, copy.ItemsByKey.Count);
        Assert.Equal(1, copy.ItemsByKey["a"].Id);
        Assert.Equal("alpha", copy.ItemsByKey["a"].Name);
        Assert.Equal(1.5m, copy.ItemsByKey["a"].Amount);
        Assert.Equal(SampleStatus.Active, copy.ItemsByKey["a"].Status);
        Assert.Equal(2, copy.ItemsByKey["b"].Id);
        Assert.Equal("beta", copy.ItemsByKey["b"].Name);
    }

    [Fact]
    public void Dictionary_Empty_RoundTrip_IsEmptyNotNull()
    {
        var original = new MapOnlyMessage();
        var copy = GeneratedProtoCodecs.Deserialize_MapOnlyMessage(
            GeneratedProtoCodecs.Serialize_MapOnlyMessage(original));

        Assert.NotNull(copy.Scores);
        Assert.Empty(copy.Scores);
        Assert.NotNull(copy.Labels);
        Assert.Empty(copy.Labels);
        Assert.NotNull(copy.ItemsByKey);
        Assert.Empty(copy.ItemsByKey);
    }

    [Fact]
    public void CollectionsMessage_IncludesDictionary_RoundTrip()
    {
        var original = new CollectionsMessage
        {
            Scores = new Dictionary<string, int> { ["x"] = 99 },
            ItemsByKey = new Dictionary<string, NestedItem>
            {
                ["n"] = new NestedItem { Id = 5, Name = "five", Amount = 5m, Status = SampleStatus.Pending }
            }
        };

        var copy = GeneratedProtoCodecs.Deserialize_CollectionsMessage(
            GeneratedProtoCodecs.Serialize_CollectionsMessage(original));

        Assert.Equal(99, copy.Scores["x"]);
        Assert.Equal(5, copy.ItemsByKey["n"].Id);
        Assert.Equal("five", copy.ItemsByKey["n"].Name);
    }

    [Fact]
    public void Marshallers_Exist_ForConstructionAndMapTypes()
    {
        Assert.NotNull(GeneratedProtoCodecs.Marshaller_PrimaryCtorOrder);
        Assert.NotNull(GeneratedProtoCodecs.Marshaller_PrivateSetterOrder);
        Assert.NotNull(GeneratedProtoCodecs.Marshaller_InitOnlyOrder);
        Assert.NotNull(GeneratedProtoCodecs.Marshaller_MapOnlyMessage);
    }
}
