# Ten99.Aria.Common

Small, dependency-light generic primitives shared across the ARIA projects and the ARIA MCP servers.

## What's inside

- **Secrets** (`Ten99.Aria.Common.Secrets`): `ISecretHelper`, a minimal abstraction for resolving secrets, with `NoOpSecretHelper` and `UserSecretHelper<T>` (user-secrets / bound configuration) implementations. An Azure Key Vault implementation lives separately in `Ten99.Aria.Common.Secrets.Azure`.
- **Configuration** (`Ten99.Aria.Common.Attributes`): `ConfigurationSectionAttribute` for declaring the configuration section a typed options class binds to.
- **Interfaces**: `ITokenStore`, a persistence abstraction for token-cache blobs.

Depends only on `Microsoft.Extensions.*`. No cloud SDKs.

## License

MIT © 1099 Ventures Inc
