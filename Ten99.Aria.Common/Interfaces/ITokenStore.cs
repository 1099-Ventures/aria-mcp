namespace Ten99.Aria.Common.Interfaces;

/// <summary>
/// Persistence layer for token cache blobs (e.g. MSAL caches).
/// Implementations: CosmosTokenStore (Data.Cosmos), future Key Vault or DPAPI.
/// </summary>
public interface ITokenStore
{
    Task<byte[]?> GetAsync(string key, CancellationToken ct = default);
    Task StoreAsync(string key, byte[] data, CancellationToken ct = default);
    Task RemoveAsync(string key, CancellationToken ct = default);
}
