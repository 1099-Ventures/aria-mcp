using System.Reflection;

namespace Ten99.Aria.Common.Attributes
{
	[AttributeUsage(AttributeTargets.Class)]
	public class ConfigurationSectionAttribute(string sectionName) : Attribute
	{
		public string SectionName { get; } = sectionName;

		public static string GetSectionName(Type configType)
		{
			var attribute = configType.GetCustomAttribute<ConfigurationSectionAttribute>();
			return attribute?.SectionName ?? configType.Name.Replace("Configuration", "");
		}

		public static string GetSectionName<T>() => GetSectionName(typeof(T));

		public static string GetSectionName(string typeName)
		{
			var configType = Type.GetType(typeName);
			if (configType == null)
			{
				return typeName.Replace("Configuration", "");
			}
			return GetSectionName(configType);
		}
	}
}