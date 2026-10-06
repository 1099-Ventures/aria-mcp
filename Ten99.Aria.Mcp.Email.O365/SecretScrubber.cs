using System.Text.RegularExpressions;

namespace Ten99.Aria.Mcp.Email.O365;

/// <summary>
/// Deterministic, pattern-based masking of bearer-shaped secrets in text bound for a model.
/// This is transport hygiene, not classification: it masks known credential <em>shapes</em>
/// (tokens carried in URL query strings, one-time codes announced in prose) without any judgement
/// about what the mail means, so it does not breach the MCP's dumb-provider boundary.
///
/// Applied to <c>BodyPreview</c> on list results, which flow to the mailbox actor (a model) on every
/// page — the always-on leak identified in live testing (#508). Magic links and password-reset links
/// are bearer credentials valid for hours; the realistic exposure is not a careful reader but
/// incidental logging and the model provider itself. Full bodies (get_email) are a deliberate,
/// on-demand fetch and are intentionally not scrubbed — masking them would defeat the reason to fetch.
/// </summary>
internal static partial class SecretScrubber
{
	private const string Mask = "[REDACTED]";

	// The value of a sensitive URL query parameter — the token in a magic link, reset link, or OAuth
	// callback. Matches "&token=..." / "?code=..." up to the next delimiter and masks only the value.
	[GeneratedRegex(
		"""(?i)([?&](?:access_token|refresh_token|id_token|token|otp|passcode|password|pwd|secret|api[-_]?key|apikey|code|magic)=)[^\s&#"'<>]+""",
		RegexOptions.CultureInvariant)]
	private static partial Regex UrlSecretParam();

	// A one-time code announced in prose: a specific code keyword followed shortly by 4–8 digits.
	// Deliberately narrow (no bare "code") so "code review 2024" / phone numbers don't trip it.
	[GeneratedRegex(
		@"(?i)((?:verification code|security code|access code|login code|one[- ]?time (?:code|passcode|pin)|passcode|\botp\b|\bpin\b)\D{0,15})\d{4,8}\b",
		RegexOptions.CultureInvariant)]
	private static partial Regex OneTimeCode();

	/// <summary>Mask bearer-shaped secrets in <paramref name="text"/>. Null/empty passes through.</summary>
	public static string Scrub(string? text)
	{
		if (string.IsNullOrEmpty(text))
			return text ?? string.Empty;

		string result = UrlSecretParam().Replace(text, m => m.Groups[1].Value + Mask);
		result = OneTimeCode().Replace(result, m => m.Groups[1].Value + Mask);
		return result;
	}
}
