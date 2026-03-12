using System.Globalization;
using System.Runtime.InteropServices;

namespace llmaid;

/// <summary>
/// Replaces well-known system placeholders in prompt strings.
/// Placeholders use the {{NAME}} convention and are resolved once per run
/// using the current system environment and locale.
/// </summary>
/// <remarks>
/// Available placeholders:
/// <list type="table">
///   <listheader><term>Placeholder</term><description>Value</description></listheader>
///   <item><term>{{CODE}}</term><description>Full content of the current file being processed</description></item>
///   <item><term>{{CODELANGUAGE}}</term><description>Programming language derived from the file extension (e.g. csharp)</description></item>
///   <item><term>{{FILENAME}}</term><description>Name of the current file being processed (without path)</description></item>
///   <item><term>{{TODAY}}</term><description>Current date in ISO 8601 format (yyyy-MM-dd)</description></item>
///   <item><term>{{NOW}}</term><description>Current date and time in ISO 8601 format (yyyy-MM-ddTHH:mm:ss)</description></item>
///   <item><term>{{YEAR}}</term><description>Current four-digit year</description></item>
///   <item><term>{{MONTH}}</term><description>Current month as two-digit number (01–12)</description></item>
///   <item><term>{{WEEKDAY}}</term><description>Current day of week in English (e.g. Thursday)</description></item>
///   <item><term>{{USERNAME}}</term><description>Operating system login name of the current user</description></item>
///   <item><term>{{MACHINENAME}}</term><description>Network hostname of the machine</description></item>
///   <item><term>{{TIMEZONE}}</term><description>IANA time zone identifier of the local system (e.g. Europe/Berlin)</description></item>
///   <item><term>{{CULTURE}}</term><description>BCP 47 locale tag of the current UI culture (e.g. de-DE)</description></item>
///   <item><term>{{DATEFORMAT}}</term><description>Short date pattern of the current culture (e.g. dd.MM.yyyy)</description></item>
///   <item><term>{{TIMEFORMAT}}</term><description>Long time pattern of the current culture (e.g. HH:mm:ss)</description></item>
///   <item><term>{{DATESEPARATOR}}</term><description>Date separator character of the current culture (e.g. .)</description></item>
///   <item><term>{{TIMESEPARATOR}}</term><description>Time separator character of the current culture (e.g. :)</description></item>
///   <item><term>{{DECIMALSEPARATOR}}</term><description>Decimal separator of the current culture (e.g. ,)</description></item>
///   <item><term>{{GROUPSEPARATOR}}</term><description>Thousands group separator of the current culture (e.g. .)</description></item>
///   <item><term>{{CURRENCYSYMBOL}}</term><description>Currency symbol of the current culture (e.g. €)</description></item>
///   <item><term>{{NEWLINE}}</term><description>Platform-specific newline sequence</description></item>
/// </list>
/// </remarks>
internal static class PromptPlaceholders
{
	/// <summary>
	/// Replaces all known system-level {{PLACEHOLDER}} tokens in <paramref name="prompt"/> with their current values.
	/// File-specific placeholders ({{CODE}}, {{CODELANGUAGE}}, {{FILENAME}}) are substituted separately
	/// by <see cref="FileProcessor"/> after this method runs.
	/// </summary>
	/// <param name="prompt">The raw prompt string that may contain placeholder tokens.</param>
	/// <returns>The prompt with all system placeholders substituted.</returns>
	internal static string Replace(string prompt)
	{
		if (string.IsNullOrEmpty(prompt))
			return prompt;

		var now = DateTime.Now;
		var culture = CultureInfo.CurrentCulture;
		var dtf = culture.DateTimeFormat;
		var nf = culture.NumberFormat;

		var timeZoneId = TimeZoneInfo.Local.Id;

		// On non-Windows platforms the BCL Id is already an IANA id.
		// On Windows it is a Windows zone id; try to convert it to IANA.
		if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
		{
			if (TimeZoneInfo.TryConvertWindowsIdToIanaId(timeZoneId, out var ianaId))
				timeZoneId = ianaId;
		}

		return prompt
			.Replace("{{TODAY}}", now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
			.Replace("{{NOW}}", now.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture))
			.Replace("{{YEAR}}", now.ToString("yyyy", CultureInfo.InvariantCulture))
			.Replace("{{MONTH}}", now.ToString("MM", CultureInfo.InvariantCulture))
			.Replace("{{WEEKDAY}}", now.ToString("dddd", CultureInfo.InvariantCulture))
			.Replace("{{USERNAME}}", Environment.UserName)
			.Replace("{{MACHINENAME}}", Environment.MachineName)
			.Replace("{{TIMEZONE}}", timeZoneId)
			.Replace("{{CULTURE}}", culture.Name)
			.Replace("{{DATEFORMAT}}", dtf.ShortDatePattern)
			.Replace("{{TIMEFORMAT}}", dtf.LongTimePattern)
			.Replace("{{DATESEPARATOR}}", dtf.DateSeparator)
			.Replace("{{TIMESEPARATOR}}", dtf.TimeSeparator)
			.Replace("{{DECIMALSEPARATOR}}", nf.NumberDecimalSeparator)
			.Replace("{{GROUPSEPARATOR}}", nf.NumberGroupSeparator)
			.Replace("{{CURRENCYSYMBOL}}", nf.CurrencySymbol)
			.Replace("{{NEWLINE}}", Environment.NewLine);
	}
}
