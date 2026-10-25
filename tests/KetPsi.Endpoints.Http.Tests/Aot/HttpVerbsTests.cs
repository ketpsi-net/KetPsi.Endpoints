using FluentAssertions;

using KetPsi.Endpoints.Http.Aot;

namespace HttpEndpointGenerator.Tests.Aot;

public class HttpVerbsTests
{
    [Fact]
    public void Get_Is_Get()
    {
        HttpVerbs.Get.Should().ContainSingle().Which.Should().Be("GET");
    }

    [Fact]
    public void Post_Is_Post()
    {
        HttpVerbs.Post.Should().ContainSingle().Which.Should().Be("POST");
    }

    [Fact]
    public void Put_Delete_Patch_Defined()
    {
        HttpVerbs.Put.Should().Contain("PUT");
        HttpVerbs.Delete.Should().Contain("DELETE");
        HttpVerbs.Patch.Should().Contain("PATCH");
    }
}
