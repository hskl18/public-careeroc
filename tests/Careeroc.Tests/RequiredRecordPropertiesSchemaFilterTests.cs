using Careeroc.Api.OpenApi;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Careeroc.Tests;

public sealed class RequiredRecordPropertiesSchemaFilterTests
{
    private sealed record SampleRecord(string Name, string? Note, int Count = 0);

    private sealed class SampleRequest
    {
        public string? Name { get; set; }
    }

    [Fact]
    public void MarksRecordParametersWithoutDefaultsRequired()
    {
        var schema = SchemaWith("name", "note", "count");

        Apply(schema, typeof(SampleRecord));

        Assert.Equal(["name", "note"], schema.Required!.Order());
    }

    [Fact]
    public void LeavesSettablePropertiesOptional()
    {
        var schema = SchemaWith("name");

        Apply(schema, typeof(SampleRequest));

        Assert.Null(schema.Required);
    }

    private static OpenApiSchema SchemaWith(params string[] names) =>
        new() { Properties = names.ToDictionary(name => name, IOpenApiSchema (_) => new OpenApiSchema()) };

    private static void Apply(OpenApiSchema schema, Type type) =>
        new RequiredRecordPropertiesSchemaFilter().Apply(schema, new SchemaFilterContext(type, null!, null!));
}
