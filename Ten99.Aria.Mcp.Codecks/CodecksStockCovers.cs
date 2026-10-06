namespace Ten99.Aria.Mcp.Codecks;

/// <summary>
/// The built-in ("cdx-cover") deck cover images. Codecks has no API to list them — the UI GETs a fixed
/// set of perma-asset URLs — so we maintain the known set here. Used as an external coverFileData
/// (<c>{ url, external:true, source:"cdx-cover" }</c>).
/// </summary>
internal static class CodecksStockCovers
{
	const string Base = "https://perma-assets-codecks.s3.eu-central-1.amazonaws.com/covers/ruth-2019/";

	public static readonly IReadOnlyDictionary<string, string> ByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
	{
		["Animation"] = Base + "CD_Animation.gif",
		["Backlog"] = Base + "CD_Backlog.jpeg",
		["UI"] = Base + "CD_UI.jpeg",
		["Bizdev"] = Base + "CD_Bizdev.jpeg",
		["LevelDesign"] = Base + "CD_LD.jpeg",
		["Audio"] = Base + "CD_Audio.jpeg",
		["Production"] = Base + "CD_Production.jpeg",
		["Docs"] = Base + "CD_Docs.jpeg",
		["Art"] = Base + "CD_Art.jpeg",
		["Code"] = Base + "CD_Code.jpeg",
		["QA"] = Base + "CD_QA.jpeg",
		["Marketing"] = Base + "CD_Marketing.jpeg",
		["Ideas"] = Base + "CD_Ideas.jpeg",
	};
}
