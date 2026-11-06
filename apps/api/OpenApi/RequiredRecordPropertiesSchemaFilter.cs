using System.Text.Json;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Careeroc.Api.OpenApi;

// Marks a record property required when its constructor parameter has no default. The serializer
// always writes every property of a response record, so the web's generated types can say `field: T`
// instead of `field?: T`; a defaulted parameter stays optional for request bodies.
public sealed class RequiredRecordPropertiesSchemaFilter : ISchemaFilter
{
    public void Apply(IOpenApiSchema schema, SchemaFilterContext context)
    {
        if (schema is not OpenApiSchema objectSchema || objectSchema.Properties is not { Count: > 0 } properties)
        {
            return;
        }

        var constructor = context.Type
            .GetConstructors()
            .MaxBy(candidate => candidate.GetParameters().Length);
        foreach (var parameter in constructor?.GetParameters() ?? [])
        {
            var name = JsonNamingPolicy.CamelCase.ConvertName(parameter.Name!);
            if (!parameter.HasDefaultValue && properties.ContainsKey(name))
            {
                objectSchema.Required ??= new HashSet<string>(StringComparer.Ordinal);
                objectSchema.Required.Add(name);
            }
        }
    }
}
