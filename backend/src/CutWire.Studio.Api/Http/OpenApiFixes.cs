using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace CutWire.Studio.Api.Http;

/// <summary>Corrections that cannot be expressed through endpoint metadata alone.</summary>
internal static class OpenApiFixes
{
    private static readonly string[] JsonOptionsPaths =
        ["/api/v1/account/passkeys/request-options", "/api/v1/account/passkeys/creation-options"];

    public static Task Apply(OpenApiDocument doc, OpenApiDocumentTransformerContext context, CancellationToken ct)
    {
        doc.Components ??= new OpenApiComponents();
        doc.Components.Schemas ??= new Dictionary<string, IOpenApiSchema>();
        var schemas = doc.Components.Schemas;

        schemas["FieldViolation"] = new OpenApiSchema
        {
            Type = JsonSchemaType.Object,
            Required = new HashSet<string> { "field", "code", "message" },
            Properties = new Dictionary<string, IOpenApiSchema>
            {
                ["field"] = new OpenApiSchema { Type = JsonSchemaType.String },
                ["code"] = new OpenApiSchema { Type = JsonSchemaType.String },
                ["message"] = new OpenApiSchema { Type = JsonSchemaType.String },
                ["params"] = new OpenApiSchema { Type = JsonSchemaType.Object | JsonSchemaType.Null, AdditionalProperties = new OpenApiSchema() },
            },
        };

        var problem = new OpenApiSchema
        {
            Type = JsonSchemaType.Object,
            Required = new HashSet<string> { "code" },
            Properties = new Dictionary<string, IOpenApiSchema>
            {
                ["type"] = new OpenApiSchema { Type = JsonSchemaType.String | JsonSchemaType.Null },
                ["title"] = new OpenApiSchema { Type = JsonSchemaType.String | JsonSchemaType.Null },
                ["status"] = new OpenApiSchema { Type = JsonSchemaType.Integer, Format = "int32" },
                ["detail"] = new OpenApiSchema { Type = JsonSchemaType.String | JsonSchemaType.Null },
                ["instance"] = new OpenApiSchema { Type = JsonSchemaType.String | JsonSchemaType.Null },
                ["code"] = new OpenApiSchema { Type = JsonSchemaType.String, Description = "Stable machine-readable error code." },
                ["errors"] = new OpenApiSchema
                {
                    Type = JsonSchemaType.Object,
                    Description = "Validation messages by field.",
                    AdditionalProperties = new OpenApiSchema { Type = JsonSchemaType.Array, Items = new OpenApiSchema { Type = JsonSchemaType.String } },
                },
                ["violations"] = new OpenApiSchema
                {
                    Type = JsonSchemaType.Array,
                    Description = "Validation failures with stable per-field codes.",
                    Items = new OpenApiSchemaReference("FieldViolation", doc),
                },
                ["traceId"] = new OpenApiSchema { Type = JsonSchemaType.String },
            },
        };
        schemas["ProblemDetails"] = problem;

        foreach (var (name, schema) in schemas)
            if (name.EndsWith("Request", StringComparison.Ordinal) && schema is OpenApiSchema { Required: not null, Properties: not null } request)
                foreach (var (property, value) in request.Properties)
                    if (value.Type is { } type && type.HasFlag(JsonSchemaType.Null))
                        request.Required.Remove(property);

        foreach (var path in JsonOptionsPaths)
            if (doc.Paths.TryGetValue(path, out var item) && item.Operations is { } ops)
                foreach (var op in ops.Values)
                    if (op.Responses?.TryGetValue("200", out var ok) == true && ok.Content?.TryGetValue("application/json", out var media) == true)
                        media.Schema = new OpenApiSchema
                        {
                            Type = JsonSchemaType.Object,
                            Description = "WebAuthn options JSON, passed unchanged to navigator.credentials.",
                            AdditionalProperties = new OpenApiSchema(),
                        };

        doc.Paths["/connect/verify"] = new OpenApiPathItem
        {
            Operations = new Dictionary<HttpMethod, OpenApiOperation>
            {
                [HttpMethod.Post] = new OpenApiOperation
                {
                    OperationId = "VerifyDevice",
                    Summary = "Approve or deny a device authorization request (browser form post with the session cookie).",
                    Tags = new HashSet<OpenApiTagReference> { new("Connect", doc) },
                    Parameters =
                    [
                        new OpenApiParameter
                        {
                            Name = "X-Requested-With", In = ParameterLocation.Header, Required = true,
                            Schema = new OpenApiSchema { Type = JsonSchemaType.String },
                        },
                    ],
                    RequestBody = new OpenApiRequestBody
                    {
                        Required = true,
                        Content = new Dictionary<string, OpenApiMediaType>
                        {
                            ["application/x-www-form-urlencoded"] = new()
                            {
                                Schema = new OpenApiSchema
                                {
                                    Type = JsonSchemaType.Object,
                                    Required = new HashSet<string> { "user_code", "submit" },
                                    Properties = new Dictionary<string, IOpenApiSchema>
                                    {
                                        ["user_code"] = new OpenApiSchema { Type = JsonSchemaType.String },
                                        ["submit"] = new OpenApiSchema { Type = JsonSchemaType.String, Enum = [System.Text.Json.Nodes.JsonValue.Create("accept"), System.Text.Json.Nodes.JsonValue.Create("deny")] },
                                    },
                                },
                            },
                        },
                    },
                    Responses = new OpenApiResponses
                    {
                        ["302"] = new OpenApiResponse { Description = "Redirect to /account/device?result=approved|denied." },
                    },
                },
            },
        };
        return Task.CompletedTask;
    }
}
