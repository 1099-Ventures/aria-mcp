using Microsoft.Extensions.Options;
using System.Reflection;

namespace Ten99.Aria.Common.Secrets
{
	public class UserSecretHelper<T>(IOptions<T> options) : ISecretHelper where T : class, new()
	{
		private readonly T _config = options.Value;
		public T Settings => _config;

		public Func<string, string>? KeyShaper { get; set; }

		public string? GetSecret(string key)
		{
			if (KeyShaper != null)
				key = KeyShaper(key);
			var prop = typeof(T).GetProperty(key, BindingFlags.IgnoreCase | BindingFlags.Instance | BindingFlags.Public);
			return prop?.GetValue(_config)?.ToString();
		}

		public IAsyncEnumerable<SecretProperties> GetExpiredSecretPropertiesAsync()
		{
			throw new NotImplementedException();
		}
	}
}