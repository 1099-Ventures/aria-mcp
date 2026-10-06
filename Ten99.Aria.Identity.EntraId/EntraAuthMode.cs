namespace Ten99.Aria.Identity.EntraId;

/// <summary>How to authenticate to Microsoft Entra ID. Resource-agnostic — the resulting
/// <see cref="Azure.Core.TokenCredential"/> serves Graph, Azure DevOps, or any Entra-protected API.</summary>
public enum EntraAuthMode
{
	/// <summary>Desktop interactive browser sign-in (public client, no secret), with a persistent
	/// token cache + <see cref="Azure.Identity.AuthenticationRecord"/> so it prompts once. For
	/// MCP/agent processes acting as the signed-in user.</summary>
	Interactive,

	/// <summary>App-only / service principal. Uses a client certificate when one is supplied
	/// (the better unattended story), otherwise a client secret — the credential shape is inferred
	/// from which is configured, it is not a separate mode.</summary>
	ServicePrincipal,

	/// <summary>Azure Managed Identity — no secret to manage. System-assigned when no ClientId is given
	/// (a silent login from the runtime's identity); user-assigned when a ClientId is supplied.</summary>
	ManagedIdentity,
}
