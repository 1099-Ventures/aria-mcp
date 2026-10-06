using System.Security.Cryptography.X509Certificates;

namespace Ten99.Aria.Identity.EntraId;

/// <summary>
/// Resolves a service-principal client certificate from configuration so cert-based SP auth can be wired
/// the same way across MCPs. Two sources, in precedence order: a <b>store thumbprint</b> (searched in the
/// CurrentUser then LocalMachine "My" stores) or a <b>file</b> on disk (PFX/PKCS#12, or a PEM containing
/// the cert + private key). Returns <c>null</c> when neither is configured — the caller then falls back to
/// a client secret (the <see cref="EntraCredentialFactory.CreateServicePrincipal"/> "cert wins over secret"
/// rule). The loaded certificate must carry its private key (SP auth signs with it).
/// </summary>
public static class EntraCertificateResolver
{
	/// <summary>Resolve a certificate from a thumbprint or a file path, or null when neither is set.
	/// Throws <see cref="InvalidOperationException"/> when a source IS configured but can't be honoured
	/// (thumbprint not found, file missing, no private key) — a misconfiguration should fail loudly, not
	/// silently fall through to a secret that may also be absent.</summary>
	public static X509Certificate2? Resolve(string? thumbprint, string? path, string? password)
	{
		if (!string.IsNullOrWhiteSpace(thumbprint))
			return FromStore(thumbprint!);
		if (!string.IsNullOrWhiteSpace(path))
			return FromFile(path!, password);
		return null;
	}

	static X509Certificate2 FromStore(string thumbprint)
	{
		// Normalise: thumbprints are often pasted with spaces or a leading LTR mark from certmgr.
		string wanted = new string(thumbprint.Where(Uri.IsHexDigit).ToArray());

		foreach (StoreLocation location in (ReadOnlySpan<StoreLocation>)[StoreLocation.CurrentUser, StoreLocation.LocalMachine])
		{
			using var store = new X509Store(StoreName.My, location);
			store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
			foreach (X509Certificate2 cert in store.Certificates)
			{
				if (string.Equals(cert.Thumbprint, wanted, StringComparison.OrdinalIgnoreCase))
				{
					if (!cert.HasPrivateKey)
						throw new InvalidOperationException(
							$"Certificate with thumbprint '{wanted}' was found in {location}/My but has no private key — " +
							"service-principal auth needs the private key.");
					return cert;
				}
			}
		}

		throw new InvalidOperationException(
			$"No certificate with thumbprint '{wanted}' in the CurrentUser or LocalMachine 'My' store. " +
			"Import the SP cert (with its private key) or use a file path / client secret instead.");
	}

	static X509Certificate2 FromFile(string path, string? password)
	{
		if (!File.Exists(path))
			throw new InvalidOperationException($"Client certificate file not found: '{path}'.");

		bool looksPem = path.EndsWith(".pem", StringComparison.OrdinalIgnoreCase)
			|| path.EndsWith(".crt", StringComparison.OrdinalIgnoreCase)
			|| path.EndsWith(".key", StringComparison.OrdinalIgnoreCase);

		X509Certificate2 cert = looksPem
			// PEM: cert + private key in one file (CreateFromPemFile reads the key from the same file).
			? X509Certificate2.CreateFromPemFile(path)
			// PFX / PKCS#12. X509CertificateLoader is the non-obsolete loader (net9+).
			: X509CertificateLoader.LoadPkcs12FromFile(path, password);

		if (!cert.HasPrivateKey)
			throw new InvalidOperationException(
				$"Client certificate '{path}' has no private key — service-principal auth needs it. " +
				"For PEM, include the private key in the same file; for PFX, export with the key.");
		return cert;
	}
}
