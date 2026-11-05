using System.Text.Json.Nodes;
using ApsDda.Core;
using Microsoft.Data.Sqlite;

namespace ApsDda.Runtime;

/// <summary>SQLite reference persistence. Administrator methods are in-process only, not HTTP admin endpoints.</summary>
public sealed class SqliteRuntimeStore
{
    private readonly string _connectionString;
    public SqliteRuntimeStore(string databasePath)
    {
        if (databasePath == ":memory:") throw new ArgumentException("Use a durable file, not a transient in-memory database.", nameof(databasePath));
        DatabasePath = Path.GetFullPath(databasePath);
        Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);
        _connectionString = new SqliteConnectionStringBuilder { DataSource = DatabasePath, Pooling = false, DefaultTimeout = 30 }.ToString();
        using var c = Open();
        Execute(c, null, "PRAGMA journal_mode=WAL; PRAGMA synchronous=FULL;");
        Execute(c, null, """
            CREATE TABLE IF NOT EXISTS metadata(key TEXT PRIMARY KEY, value TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS artifacts(refkey TEXT PRIMARY KEY, artifact_type TEXT NOT NULL, artifact_id TEXT NOT NULL, artifact_version TEXT NOT NULL, digest TEXT NOT NULL, envelope TEXT NOT NULL, UNIQUE(artifact_type,artifact_id,artifact_version));
            CREATE TABLE IF NOT EXISTS evidence_values(valueref TEXT PRIMARY KEY, value TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS revocations(kind TEXT NOT NULL, identity TEXT NOT NULL, observed_at TEXT NOT NULL, PRIMARY KEY(kind,identity));
            CREATE TABLE IF NOT EXISTS authorities(refkey TEXT PRIMARY KEY, authenticated_identity TEXT NOT NULL, authenticated_subject TEXT NOT NULL, admitted_at TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS authority_uses(authority_id TEXT PRIMARY KEY, refkey TEXT NOT NULL, workflow_id TEXT NOT NULL, plan_digest TEXT NOT NULL, steps TEXT NOT NULL, consumed_at TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS workflows(workflow_id TEXT PRIMARY KEY, plan_ref TEXT NOT NULL, state TEXT NOT NULL, authority_ref TEXT, safe_error TEXT);
            CREATE TABLE IF NOT EXISTS workflow_steps(workflow_id TEXT NOT NULL, step_id TEXT NOT NULL, ordinal INTEGER NOT NULL, payload TEXT NOT NULL, state TEXT NOT NULL, invocation_ref TEXT, receipt_ref TEXT, PRIMARY KEY(workflow_id,step_id));
            CREATE TABLE IF NOT EXISTS resources(source_issuer TEXT NOT NULL, target TEXT NOT NULL, tenant_id TEXT NOT NULL, version INTEGER NOT NULL, state TEXT NOT NULL, PRIMARY KEY(source_issuer,target));
            CREATE TABLE IF NOT EXISTS source_operations(tenant_id TEXT NOT NULL, source_issuer TEXT NOT NULL, domain TEXT NOT NULL, idem_key TEXT NOT NULL, fingerprint TEXT NOT NULL, invocation_ref TEXT NOT NULL, operation_id TEXT NOT NULL UNIQUE, result TEXT NOT NULL, committed_at INTEGER NOT NULL, authority_ref TEXT NOT NULL, target TEXT NOT NULL, PRIMARY KEY(tenant_id,source_issuer,domain,idem_key));
            """);
        var version = Scalar(c, null, "SELECT value FROM metadata WHERE key='schema_version'") as string;
        if (version is not null && version != "1") throw new InvalidOperationException("Unsupported runtime database schema version.");
        Execute(c, null, "INSERT OR IGNORE INTO metadata(key,value) VALUES('schema_version','1')");
    }
    public string DatabasePath { get; }
    internal SqliteConnection Open()
    {
        var c = new SqliteConnection(_connectionString); c.Open();
        Execute(c, null, "PRAGMA foreign_keys=ON; PRAGMA synchronous=FULL; PRAGMA busy_timeout=30000;"); return c;
    }
    internal T Transaction<T>(Func<SqliteConnection, SqliteTransaction, T> action)
    {
        using var c = Open(); using var t = c.BeginTransaction(deferred: false);
        var result = action(c, t); t.Commit(); return result;
    }
    internal static SqliteCommand Command(SqliteConnection c, SqliteTransaction? t, string sql, params (string Name, object? Value)[] args)
    {
        var command = c.CreateCommand(); command.Transaction = t; command.CommandText = sql;
        foreach (var (name, value) in args) command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return command;
    }
    internal static int Execute(SqliteConnection c, SqliteTransaction? t, string sql, params (string Name, object? Value)[] args)
    { using var cmd = Command(c, t, sql, args); return cmd.ExecuteNonQuery(); }
    internal static object? Scalar(SqliteConnection c, SqliteTransaction? t, string sql, params (string Name, object? Value)[] args)
    { using var cmd = Command(c, t, sql, args); return cmd.ExecuteScalar(); }
    internal static string RefKey(ArtifactRef r) => r.ArtifactType + "\n" + r.Id + "\n" + r.ArtifactVersion + "\n" + r.ContentDigest;
    internal static string RefJson(ArtifactRef r) => r.ToJson().ToJsonString();
    internal static ArtifactRef ParseRef(string json) => ArtifactRef.FromJson(StrictJson.Parse(json));
    public void Retain(SignedArtifact artifact) => Transaction((c, t) => { Retain(c, t, artifact); return true; });
    internal static void Retain(SqliteConnection c, SqliteTransaction t, SignedArtifact artifact)
    {
        artifact.Artifact.RequireIntegrity(); var r = artifact.Artifact.Reference;
        var existing = Scalar(c, t, "SELECT digest FROM artifacts WHERE artifact_type=$type AND artifact_id=$id AND artifact_version=$version", ("$type", r.ArtifactType), ("$id", r.Id), ("$version", r.ArtifactVersion)) as string;
        Errors.Require(existing is null || existing == r.ContentDigest, "CONFLICT", "Immutable artifact identity already has different bytes.");
        Execute(c, t, "INSERT OR IGNORE INTO artifacts VALUES($key,$type,$id,$version,$digest,$envelope)", ("$key", RefKey(r)), ("$type", r.ArtifactType), ("$id", r.Id), ("$version", r.ArtifactVersion), ("$digest", r.ContentDigest), ("$envelope", artifact.ToJsonString()));
    }
    public SignedArtifact GetArtifact(ArtifactRef reference)
    { using var c = Open(); return GetArtifact(c, null, reference); }
    internal static SignedArtifact GetArtifact(SqliteConnection c, SqliteTransaction? t, ArtifactRef reference)
    {
        var json = Scalar(c, t, "SELECT envelope FROM artifacts WHERE refkey=$key", ("$key", RefKey(reference))) as string;
        Errors.Require(json is not null, "UNTRUSTED_PARTICIPANT", "Exact retained artifact is missing.");
        var signed = SignedArtifact.Parse(json!); reference.RequireMatches(signed.Artifact); return signed;
    }
    public string RetainEvidenceValue(JsonObject value)
    {
        var digest = Jcs.Sha256Digest(value); var reference = "urn:aps-dda:value:" + digest;
        var json = StrictJson.ParseObject(value.ToJsonString()).ToJsonString();
        Transaction((c, t) => { Execute(c, t, "INSERT OR IGNORE INTO evidence_values VALUES($ref,$value)", ("$ref", reference), ("$value", json)); return true; });
        return reference;
    }
    public JsonObject GetEvidenceValue(string reference)
    { using var c = Open(); return GetEvidenceValue(c, null, reference); }
    internal static JsonObject GetEvidenceValue(SqliteConnection c, SqliteTransaction? t, string reference)
    {
        var json = Scalar(c, t, "SELECT value FROM evidence_values WHERE valueref=$ref", ("$ref", reference)) as string;
        Errors.Require(json is not null, "INVALID_ARTIFACT", "Protected evidence value is not retained.");
        var value = StrictJson.ParseObject(json!);
        Errors.Require(reference == "urn:aps-dda:value:" + Jcs.Sha256Digest(value)); return value;
    }
    internal static string RetainEvidenceValue(SqliteConnection c, SqliteTransaction t, JsonObject value)
    {
        var reference = "urn:aps-dda:value:" + Jcs.Sha256Digest(value);
        Execute(c, t, "INSERT OR IGNORE INTO evidence_values VALUES($ref,$value)", ("$ref", reference), ("$value", value.ToJsonString())); return reference;
    }
    public void RevokeArtifact(ArtifactRef r, DateTimeOffset observedAt) => Revoke("artifact", RefKey(r), observedAt);
    public void RevokeIssuer(string issuer, DateTimeOffset observedAt) => Revoke("issuer", issuer, observedAt);
    public void RevokeKey(string issuer, string keyId, DateTimeOffset observedAt) => Revoke("key", issuer + "\n" + keyId, observedAt);
    private void Revoke(string kind, string id, DateTimeOffset now) => Transaction((c, t) => Execute(c, t, "INSERT OR IGNORE INTO revocations VALUES($kind,$id,$time)", ("$kind", kind), ("$id", id), ("$time", now.ToString("O"))));
    internal static void RequireNotRevoked(SqliteConnection c, SqliteTransaction? t, SignedArtifact a)
    {
        var n = Convert.ToInt64(Scalar(c, t, "SELECT count(*) FROM revocations WHERE (kind='artifact' AND identity=$artifact) OR (kind='issuer' AND identity=$issuer) OR (kind='key' AND identity=$key)", ("$artifact", RefKey(a.Artifact.Reference)), ("$issuer", a.Artifact.Header.Issuer), ("$key", a.Artifact.Header.Issuer + "\n" + a.Signature.KeyId)));
        Errors.Require(n == 0, "UNTRUSTED_PARTICIPANT", "Artifact, issuer or key was durably revoked.");
    }
    public void SetCurrentPolicy(JsonObject policyReference)
    {
        ArtifactSchema.Default.RequireDefinition(policyReference, "PolicyRef");
        Transaction((c, t) => Execute(c, t, "INSERT INTO metadata VALUES('current_policy',$value) ON CONFLICT(key) DO UPDATE SET value=excluded.value", ("$value", policyReference.ToJsonString())));
    }
    internal static void RequireCurrentPolicy(SqliteConnection c, SqliteTransaction? t, JsonObject reference)
    {
        var json = Scalar(c, t, "SELECT value FROM metadata WHERE key='current_policy'") as string;
        Errors.Require(json is not null && Jcs.Canonicalize(StrictJson.Parse(json)).AsSpan().SequenceEqual(Jcs.Canonicalize(reference)), "POLICY_EVALUATION_FAILED", "Current pinned policy changed or is unavailable.");
    }
    public void SeedResource(string sourceIssuer, string target, string tenantId, JsonObject state, long version = 1)
    {
        Errors.Require(version > 0); _ = Jcs.Canonicalize(state);
        Transaction((c, t) => Execute(c, t, "INSERT INTO resources VALUES($source,$target,$tenant,$version,$state)", ("$source", sourceIssuer), ("$target", target), ("$tenant", tenantId), ("$version", version), ("$state", state.ToJsonString())));
    }
    public ResourceSnapshot GetResource(string sourceIssuer, string target)
    { using var c = Open(); return GetResource(c, null, sourceIssuer, target); }
    internal static ResourceSnapshot GetResource(SqliteConnection c, SqliteTransaction? t, string issuer, string target)
    {
        using var cmd = Command(c, t, "SELECT tenant_id,version,state FROM resources WHERE source_issuer=$source AND target=$target", ("$source", issuer), ("$target", target));
        using var r = cmd.ExecuteReader(); Errors.Require(r.Read(), "PRECONDITION_FAILED", "Source resource does not exist.");
        return new(target, r.GetString(0), CanonicalQuantity.Format(r.GetInt64(1)), StrictJson.ParseObject(r.GetString(2)));
    }
    public long OperationCount { get { using var c = Open(); return Convert.ToInt64(Scalar(c, null, "SELECT count(*) FROM source_operations")); } }
    public long AuthorityUseCount { get { using var c = Open(); return Convert.ToInt64(Scalar(c, null, "SELECT count(*) FROM authority_uses")); } }
    public WorkflowSnapshot GetWorkflow(string workflowId)
    { using var c = Open(); return GetWorkflow(c, null, workflowId); }
    internal static WorkflowSnapshot GetWorkflow(SqliteConnection c, SqliteTransaction? t, string workflowId)
    {
        ArtifactRef plan; WorkflowState state; ArtifactRef? authority; string? error;
        using (var cmd = Command(c, t, "SELECT plan_ref,state,authority_ref,safe_error FROM workflows WHERE workflow_id=$id", ("$id", workflowId)))
        using (var r = cmd.ExecuteReader())
        { Errors.Require(r.Read(), "INVALID_ARTIFACT", "Workflow is not retained."); plan = ParseRef(r.GetString(0)); state = Enum.Parse<WorkflowState>(r.GetString(1)); authority = r.IsDBNull(2) ? null : ParseRef(r.GetString(2)); error = r.IsDBNull(3) ? null : r.GetString(3); }
        var planArtifact = GetArtifact(c, t, plan).Artifact;
        var steps = new List<StepSnapshot>();
        using (var cmd = Command(c, t, "SELECT step_id,state,invocation_ref,receipt_ref FROM workflow_steps WHERE workflow_id=$id ORDER BY ordinal", ("$id", workflowId)))
        using (var r = cmd.ExecuteReader())
            while (r.Read())
            { var step = ArtifactBindings.GetStep(planArtifact, r.GetString(0)); steps.Add(new(r.GetString(0), r.GetString(1), r.IsDBNull(2) ? null : ParseRef(r.GetString(2)), r.IsDBNull(3) ? null : ParseRef(r.GetString(3)), step["manualRecoveryOwner"]?.GetValue<string>())); }
        return new(workflowId, plan, state, authority, error, steps);
    }
    /// <summary>Server-side retained exact payload. Hosts MUST authorize reads before exposing it.</summary>
    public JsonObject GetPayload(string workflowId, string stepId)
    { using var c = Open(); return GetPayload(c, null, workflowId, stepId); }
    internal static JsonObject GetPayload(SqliteConnection c, SqliteTransaction? t, string workflowId, string stepId)
    {
        var json = Scalar(c, t, "SELECT payload FROM workflow_steps WHERE workflow_id=$id AND step_id=$step", ("$id", workflowId), ("$step", stepId)) as string;
        Errors.Require(json is not null); return StrictJson.ParseObject(json!);
    }
    internal static void SetWorkflowState(SqliteConnection c, SqliteTransaction t, string workflowId, WorkflowState state, string? error = null)
        => Execute(c, t, "UPDATE workflows SET state=$state,safe_error=$error WHERE workflow_id=$id", ("$state", state.ToString()), ("$error", error), ("$id", workflowId));
    internal static SourceResult? Lookup(SqliteConnection c, SqliteTransaction? t, OperationIdentity identity)
    {
        using var cmd = Command(c, t, "SELECT fingerprint,result FROM source_operations WHERE tenant_id=$tenant AND source_issuer=$source AND domain=$domain AND idem_key=$key", ("$tenant", identity.TenantId), ("$source", identity.SourceIssuer), ("$domain", identity.IdempotencyDomain), ("$key", identity.IdempotencyKey));
        using var r = cmd.ExecuteReader(); if (!r.Read()) return null;
        Errors.Require(r.GetString(0) == identity.Fingerprint, "IDEMPOTENCY_KEY_REUSE_CONFLICT", "Idempotency key is already bound to another fingerprint.");
        var o = StrictJson.ParseObject(r.GetString(1)); return new(SignedArtifact.Parse(o.Object("receipt").ToJsonString()), SignedArtifact.Parse(o.Object("evidence").ToJsonString()));
    }
}
