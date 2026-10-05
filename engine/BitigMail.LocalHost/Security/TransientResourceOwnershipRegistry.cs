using Microsoft.AspNetCore.Http;

namespace BitigMail.LocalHost.Security;

public sealed class TransientResourceOwnershipRegistry
{
    private const int Capacity = 20_000;
    private readonly IHttpContextAccessor _http;
    private readonly object _gate = new();
    private readonly Dictionary<string,(string UserId,string SessionId,long SecurityVersion)> _owners = new(StringComparer.Ordinal);
    private readonly Queue<string> _order = new();

    public TransientResourceOwnershipRegistry(IHttpContextAccessor http) => _http=http;

    public void Bind(string id)
    {
        if(string.IsNullOrWhiteSpace(id)||_http.HttpContext?.Items[typeof(AuthenticatedSessionPrincipal)] is not AuthenticatedSessionPrincipal actor)return;
        lock(_gate){if(!_owners.ContainsKey(id))_order.Enqueue(id);_owners[id]=(actor.UserId,actor.SessionId,actor.SecurityVersion);while(_owners.Count>Capacity&&_order.TryDequeue(out var expired))_owners.Remove(expired);}
    }

    public bool IsOwnedBy(string id,AuthenticatedSessionPrincipal actor)
    {
        lock(_gate)return _owners.TryGetValue(id,out var owner)&&owner.UserId==actor.UserId&&owner.SessionId==actor.SessionId&&owner.SecurityVersion==actor.SecurityVersion;
    }
}
