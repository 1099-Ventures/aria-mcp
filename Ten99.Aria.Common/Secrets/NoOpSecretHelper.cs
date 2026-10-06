namespace Ten99.Aria.Common.Secrets
{
	public class NoOpSecretHelper : ISecretHelper
	{
		public string? GetSecret(string key)
		{
			return null;
		}

		public IAsyncEnumerable<SecretProperties> GetExpiredSecretPropertiesAsync()
		{
			return AsyncEnumerable.Empty<SecretProperties>();
		}
	}
}