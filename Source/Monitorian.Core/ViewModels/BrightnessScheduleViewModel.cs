using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;

using Monitorian.Core.Common;
using Monitorian.Core.Models;

namespace Monitorian.Core.ViewModels;

public class BrightnessScheduleViewModel : ViewModelBase
{
	private readonly Settings _settings;

	public ObservableCollection<BrightnessScheduleEntryViewModel> Entries { get; } = [];

	/// <summary>
	/// Brightness for each hour of a day, for the overview strip
	/// </summary>
	public ObservableCollection<HourBrightness> Hours { get; } = [];

	public BrightnessScheduleViewModel(Settings settings)
	{
		this._settings = settings ?? throw new ArgumentNullException(nameof(settings));

		foreach (var entry in BrightnessSchedule.Parse(settings.BrightnessSchedule))
			AddEntry(new BrightnessScheduleEntryViewModel(entry.Time, entry.Brightness));

		for (int hour = 0; hour < 24; hour++)
			Hours.Add(new HourBrightness(hour));

		_isEnabled = settings.UsesBrightnessSchedule;

		Refresh();
	}

	/// <summary>
	/// Whether to use the schedule, committed on save
	/// </summary>
	public bool IsEnabled
	{
		get => _isEnabled;
		set => SetProperty(ref _isEnabled, value);
	}
	private bool _isEnabled;

	public bool IsValid
	{
		get => _isValid;
		private set => SetProperty(ref _isValid, value);
	}
	private bool _isValid = true;

	public string Error
	{
		get => _error;
		private set => SetProperty(ref _error, value);
	}
	private string _error;

	public bool IsEmpty => Entries.Count == 0;

	public void Add()
	{
		// Place a new entry an hour after the last one, with the same brightness.
		var last = Entries.Where(x => x.Time.HasValue).OrderBy(x => x.Time).LastOrDefault();
		var start = last is null
			? new TimeSpan(12, 0, 0)
			: TimeSpan.FromHours((last.Time.Value.TotalHours + 1) % 24);

		// Find the first free time stepping by an hour, then by a minute.
		var used = Entries.Where(x => x.Time.HasValue).Select(x => x.Time.Value).ToHashSet();
		var time = Enumerable.Range(0, 24).Select(x => TimeSpan.FromHours((start.TotalHours + x) % 24))
			.Concat(Enumerable.Range(0, 24 * 60).Select(x => TimeSpan.FromMinutes((start.TotalMinutes + x) % (24 * 60))))
			.Cast<TimeSpan?>()
			.FirstOrDefault(x => !used.Contains(x.Value));
		if (time is null)
			return; // Every minute of the day is taken.

		AddEntry(new BrightnessScheduleEntryViewModel(time.Value, last?.Brightness ?? 50));
		Refresh();
	}

	public void Remove(BrightnessScheduleEntryViewModel entry)
	{
		if (!Entries.Remove(entry))
			return;

		entry.PropertyChanged -= OnEntryChanged;
		Refresh();
	}

	/// <summary>
	/// Saves the schedule if valid.
	/// </summary>
	/// <returns>True if saved</returns>
	public bool Save()
	{
		if (!TryBuild(out var entries))
			return false;

		_settings.BrightnessSchedule = BrightnessSchedule.Format(entries);
		_settings.UsesBrightnessSchedule = IsEnabled;
		return true;
	}

	private void AddEntry(BrightnessScheduleEntryViewModel entry)
	{
		entry.PropertyChanged += OnEntryChanged;
		Entries.Add(entry);
	}

	private void OnEntryChanged(object sender, PropertyChangedEventArgs e)
	{
		// TimeText changes before Time, so refresh on both to keep the error text current.
		if (e.PropertyName is nameof(BrightnessScheduleEntryViewModel.TimeText)
			or nameof(BrightnessScheduleEntryViewModel.Time)
			or nameof(BrightnessScheduleEntryViewModel.Brightness))
			Refresh();
	}

	private bool TryBuild(out List<BrightnessScheduleEntry> entries)
	{
		entries = [];
		string error = null;

		var duplicates = Entries
			.Where(x => x.Time.HasValue)
			.GroupBy(x => x.Time.Value)
			.Where(x => x.Count() > 1)
			.SelectMany(x => x)
			.ToHashSet();

		foreach (var entry in Entries)
		{
			entry.IsDuplicate = duplicates.Contains(entry);

			if (!entry.Time.HasValue)
				error ??= $"\"{entry.TimeText}\" is not a valid time. Use HH:mm, e.g. 07:30.";
			else if (entry.IsDuplicate)
				error ??= $"{entry.Time.Value:hh\\:mm} is used more than once.";
			else
				entries.Add(new BrightnessScheduleEntry(entry.Time.Value, entry.Brightness));
		}

		entries.Sort((a, b) => a.Time.CompareTo(b.Time));
		Error = error;
		IsValid = error is null;
		return IsValid;
	}

	private void Refresh()
	{
		TryBuild(out var entries);

		foreach (var hour in Hours)
		{
			var active = BrightnessSchedule.GetActive(entries, DateTime.Today.AddHours(hour.Hour).AddMinutes(30));
			hour.Brightness = active?.Entry.Brightness;
		}

		OnPropertyChanged(nameof(IsEmpty));
	}
}

public class BrightnessScheduleEntryViewModel : BindableBase
{
	private static readonly string[] _timeFormats = [@"h\:mm", @"hh\:mm"];

	public BrightnessScheduleEntryViewModel(TimeSpan time, int brightness)
	{
		_timeText = time.ToString(@"hh\:mm", CultureInfo.InvariantCulture);
		_time = time;
		_brightness = brightness;
	}

	/// <summary>
	/// Time as typed by user
	/// </summary>
	public string TimeText
	{
		get => _timeText;
		set
		{
			if (!SetProperty(ref _timeText, value))
				return;

			Time = TimeSpan.TryParseExact(value?.Trim(), _timeFormats, CultureInfo.InvariantCulture, out var time)
				&& (time < TimeSpan.FromDays(1))
				? time
				: null;
		}
	}
	private string _timeText;

	/// <summary>
	/// Parsed time or null if invalid
	/// </summary>
	public TimeSpan? Time
	{
		get => _time;
		private set
		{
			if (SetProperty(ref _time, value))
				OnPropertyChanged(nameof(IsTimeValid));
		}
	}
	private TimeSpan? _time;

	public bool IsTimeValid => Time.HasValue && !IsDuplicate;

	public bool IsDuplicate
	{
		get => _isDuplicate;
		set
		{
			if (SetProperty(ref _isDuplicate, value))
				OnPropertyChanged(nameof(IsTimeValid));
		}
	}
	private bool _isDuplicate;

	public int Brightness
	{
		get => _brightness;
		set => SetProperty(ref _brightness, Math.Max(0, Math.Min(100, value)));
	}
	private int _brightness;

	/// <summary>
	/// Formats the time on leaving the box, e.g. "7:5" stays as typed but "7:05" becomes "07:05".
	/// </summary>
	public void NormalizeTimeText()
	{
		if (Time.HasValue)
			TimeText = Time.Value.ToString(@"hh\:mm", CultureInfo.InvariantCulture);
	}
}

public class HourBrightness : BindableBase
{
	public int Hour { get; }

	public string Label => (Hour % 6 == 0) ? Hour.ToString("00", CultureInfo.InvariantCulture) : null;

	public HourBrightness(int hour) => this.Hour = hour;

	/// <summary>
	/// Brightness in effect during this hour or null if no schedule
	/// </summary>
	public int? Brightness
	{
		get => _brightness;
		set
		{
			if (SetProperty(ref _brightness, value))
				OnPropertyChanged(nameof(Level));
		}
	}
	private int? _brightness;

	/// <summary>
	/// Brightness as a ratio (0-1) for drawing
	/// </summary>
	public double Level => (Brightness ?? 0) / 100D;
}
