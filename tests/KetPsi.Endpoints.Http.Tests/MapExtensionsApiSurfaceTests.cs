using System.Reflection;

using FluentAssertions;

using KetPsi.Endpoints.Abstractions;

namespace HttpEndpointGenerator.Tests.Tests;

/// <summary>
/// Guards API surface consistency for Map* extension methods.
/// </summary>
public class MapExtensionsApiSurfaceTests
{
    [Theory]
    [InlineData("MapGet")]
    [InlineData("MapPost")]
    [InlineData("MapPut")]
    [InlineData("MapDelete")]
    [InlineData("MapPatch")]
    public void MapMethod_Exists_With_IEndpoint_Constraint(string methodName)
    {
        var methods = typeof(KetPsi.Endpoints.Http.MapExtensions)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.Name == methodName)
            .ToList();

        methods.Should().NotBeEmpty($"{methodName} should exist on MapExtensions");

        foreach (var method in methods)
        {
            method.IsGenericMethodDefinition.Should().BeTrue();
            var typeParam = method.GetGenericArguments().Single();
            var constraints = typeParam.GetGenericParameterConstraints();

            constraints.Should().Contain(c => c == typeof(IEndpoint),
                $"{methodName}<TEndpoint> must constrain TEndpoint : IEndpoint");

            // class constraint is encoded as GenericParameterAttributes.ReferenceTypeConstraint
            (typeParam.GenericParameterAttributes & GenericParameterAttributes.ReferenceTypeConstraint)
                .Should().NotBe(GenericParameterAttributes.None,
                    $"{methodName}<TEndpoint> must constrain TEndpoint : class");
        }
    }

    [Fact]
    public void MapPatch_IsRecognizedByGenerator_IsMapMethodList()
    {
        // Regression: generator listed MapPatch while MapExtensions lacked the method.
        typeof(KetPsi.Endpoints.Http.MapExtensions).GetMethod("MapPatch", BindingFlags.Public | BindingFlags.Static)
            .Should().NotBeNull();
    }
}
