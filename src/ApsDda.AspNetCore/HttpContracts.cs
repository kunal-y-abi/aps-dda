using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using ApsDda.Core;
using ApsDda.Runtime;

namespace ApsDda.AspNetCore;

/// <summary>Explicitly experimental envelopes; enclosed artifacts retain the unchanged original schema.</summary>
public static class HttpBinding
{
    public const string Version = "aps-dda.experimental/http/1";
    public const string Prefix = "/aps-dda/v1";
    public const string AuthenticationScheme = "ApsDdaConfiguredToken";
    public static JsonSerializerOptions CreateJsonOptions() => new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        RespectRequiredConstructorParameters = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };
}

public sealed record StorePlanRequest(string Binding, SignedArtifact Plan, Dictionary<string, JsonObject> Payloads);
public sealed record PreparePlanRequest(string Binding, string ResourceAlias, string Quantity, string ExpectedVersion);
public sealed record ApprovalRequest(string Binding, string ReviewDigest, DateTimeOffset ExpiresAt, string[] AllowedStepIds);
public sealed record AuthorizationRequest(string Binding, ArtifactRef AuthorityRef);
public sealed record ActionRequest(string Binding);
public sealed record WorkflowReview(string Binding, WorkflowSnapshot Workflow, SignedArtifact Plan,
    Dictionary<string, JsonObject> Payloads, string ReviewDigest);
public sealed record ResourceRead(string Binding, ResourceSnapshot Resource, SignedArtifact Product, DateTimeOffset ObservedAt);
public sealed record OperationLookup(string Binding, bool Found, SourceResult? Result);
public sealed record HttpError(string Binding, string Code);

/// <summary>Trusted server code, never a caller-supplied plugin. Must issue context from the supplied authenticated principal.</summary>
public interface IBoundedPlanPreparer
{
    StorePlanRequest Prepare(PreparePlanRequest request, AuthenticatedPrincipal principal);
}

/// <summary>Server-owned read alias; source issuer and actual target cannot be supplied through HTTP.</summary>
public sealed record HttpResourceRegistration(string Alias, ArtifactRef CapabilityRef, string Target);
