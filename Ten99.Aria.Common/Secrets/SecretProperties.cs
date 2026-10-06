namespace Ten99.Aria.Common.Secrets
{
	public class SecretProperties
	{
		public string? Name { get; set; }
		public DateTimeOffset? ExpiresOn { get; set; }
		public DateTimeOffset? CreatedOn { get; set; }
		public bool Enabled { get; set; }
	}
}