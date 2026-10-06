namespace Ten99.Aria.Common.Secrets
{
	public interface ISecretHelper
	{
		string? GetSecret(string key);
		IAsyncEnumerable<SecretProperties> GetExpiredSecretPropertiesAsync();
	}
}