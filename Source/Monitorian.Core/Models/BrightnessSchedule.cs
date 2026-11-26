using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Monitorian.Core.Models;

/// <summary>
/// Point in a day from which a brightness applies
/// </summary>
public sealed class BrightnessScheduleEntry
{
	public TimeSpan Time { get; }
	public int Brightness { get; }

	public BrightnessScheduleEntry(TimeSpan time, int brightness)
	{
		if (time < TimeSpan.Zero || time >= TimeSpan.FromDays(1))
			throw new ArgumentOutOfRangeException(nameof(time));
		if (brightness is < 0 or > 100)
			throw new ArgumentOutOfRangeException(nameof(brightness));

		this.Time = time;
		this.Brightness = brightness;
	}

	public override string ToString() => $"{Time:hh\\:mm} {Brightness}";
}

/// <summary>
/// Parsing and evaluation of a daily brightness schedule
/// </summary>
/// <remarks>
/// A schedule is written as entries of "HH:mm brightness" separated by new lines or semicolons,
/// e.g. "07:00 80; 19:00 50; 22:00 20". Each brightness applies from its time until the next
/// entry's time, wrapping around midnight.
/// </remarks>
public static class BrightnessSchedule
{
	private static readonly char[] _entrySeparators = ['\r', '\n', ';'];
	private static readonly char[] _fieldSeparators = [' ', '\t', '=', ','];
	private static readonly string[] _timeFormats = [@"h\:mm", @"hh\:mm"];

	public static bool TryParse(string text, out IReadOnlyList<BrightnessScheduleEntry> entries, out string error)
	{
		var buffer = new List<BrightnessScheduleEntry>();
		entries = buffer;
		error = null;

		if (string.IsNullOrWhiteSpace(text))
			return true;

		foreach (var line in text.Split(_entrySeparators, StringSplitOptions.RemoveEmptyEntries)
			.Select(x => x.Trim())
			.Where(x => x.Length > 0 && !x.StartsWith("#")))
		{
			var fields = line.Split(_fieldSeparators, StringSplitOptions.RemoveEmptyEntries);
			if (fields.Length != 2)
			{
				error = $"\"{line}\": expected \"HH:mm brightness\".";
				return false;
			}
			if (!TimeSpan.TryParseExact(fields[0], _timeFormats, CultureInfo.InvariantCulture, out var time)
				|| time >= TimeSpan.FromDays(1))
			{
				error = $"\"{line}\": invalid time \"{fields[0]}\".";
				return false;
			}
			if (!int.TryParse(fields[1].TrimEnd('%'), NumberStyles.Integer, CultureInfo.InvariantCulture, out var brightness)
				|| brightness is < 0 or > 100)
			{
				error = $"\"{line}\": brightness must be 0-100.";
				return false;
			}
			if (buffer.Any(x => x.Time == time))
			{
				error = $"\"{line}\": time {fields[0]} is listed twice.";
				return false;
			}
			buffer.Add(new BrightnessScheduleEntry(time, brightness));
		}

		buffer.Sort((a, b) => a.Time.CompareTo(b.Time));
		return true;
	}

	public static IReadOnlyList<BrightnessScheduleEntry> Parse(string text) =>
		TryParse(text, out var entries, out _) ? entries : [];

	public static string Format(IEnumerable<BrightnessScheduleEntry> entries, string separator = "; ") =>
		string.Join(separator, entries.Select(x => x.ToString()));

	/// <summary>
	/// Gets the entry in effect at a given moment and the moment it took effect.
	/// </summary>
	/// <param name="entries">Entries sorted by time</param>
	/// <param name="now">Local date and time</param>
	public static (BrightnessScheduleEntry Entry, DateTime Since)? GetActive(IReadOnlyList<BrightnessScheduleEntry> entries, DateTime now)
	{
		if (entries is not { Count: > 0 })
			return null;

		var current = entries.LastOrDefault(x => x.Time <= now.TimeOfDay);
		if (current is not null)
			return (current, now.Date + current.Time);

		// Before the first entry of the day, the last entry of the previous day is in effect.
		var last = entries[entries.Count - 1];
		return (last, now.Date.AddDays(-1) + last.Time);
	}
}
