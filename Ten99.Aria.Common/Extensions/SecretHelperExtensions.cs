using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Ten99.Aria.Common.Secrets;

namespace Ten99.Aria.Common.Extensions
{
	public static class SecretHelperExtensions
	{
		public static ISecretHelper? CreateSecretHelper(
			this IServiceProvider serviceProvider,
			string configTypeName,
			SecretHelperType helperType = SecretHelperType.UserSecrets)
		{
			return helperType switch
			{
				SecretHelperType.UserSecrets => CreateUserSecretHelper(serviceProvider, configTypeName),
				SecretHelperType.None => new NoOpSecretHelper(),
				_ => throw new ArgumentException($"Unknown secret helper type: {helperType}")
			};
		}

		private static ISecretHelper? CreateUserSecretHelper(IServiceProvider serviceProvider, string configTypeName)
		{
			var configType = Type.GetType(configTypeName) ?? throw new InvalidOperationException($"Configuration type '{configTypeName}' not found");
			var userSecretHelperType = typeof(UserSecretHelper<>).MakeGenericType(configType);
			return (ISecretHelper?)Activator.CreateInstance(userSecretHelperType,
				serviceProvider.GetRequiredService(typeof(IOptions<>).MakeGenericType(configType)));
		}
	}

	public enum SecretHelperType
	{
		None,
		UserSecrets
	}
}