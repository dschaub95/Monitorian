using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Threading;
using Microsoft.Win32;

namespace Monitorian.Core.Models.Watcher;

/// <summary>
/// Applies scheduled brightness when a schedule entry takes effect.
/// </summary>
/// <remarks>
/// A brightness is applied once per entry occurrence, so manual changes made afterwards persist
/// until the next entry. Checks run periodically, so an entry that took effect while the system
/// was asleep or the session was locked is applied once the app can reach monitors again.
/// </remarks>
internal class BrightnessScheduler : IDisposable
{
	private static readonly TimeSpan _checkInterval = TimeSpan.FromSeconds(30);

	private readonly DispatcherTimer _timer;
	private Func<int, Task<bool>> _apply;
	private Func<string> _getBlocker;
	private Action<string> _record;
	private string _lastBlocker;

	private IReadOnlyList<BrightnessScheduleEntry> _entries = [];
	private (DateTime Since, int Brightness)? _applied;
	private bool _isApplying;

	public BrightnessScheduler()
	{
		_timer = new DispatcherTimer { Interval = _checkInterval };
		_timer.Tick += (_, _) => Check();

		SystemEvents.TimeChanged += OnTimeChanged;
	}

	private void OnTimeChanged(object sender, EventArgs e)
	{
		// DateTime.Now uses a cached time zone. Refresh it when the system clock or time zone changes.
		TimeZoneInfo.ClearCachedData();
		_timer.Dispatcher.BeginInvoke(new Action(Check));
	}

	public bool IsEnabled => _timer.IsEnabled;

	/// <summary>
	/// Sets the actions used to apply brightness.
	/// </summary>
	/// <param name="apply">Function to apply brightness (0-100) to monitors, returning whether any monitor accepted it</param>
	/// <param name="getBlocker">Function to tell why monitors cannot be changed now, or null if they can</param>
	/// <param name="record">Action to record diagnostic messages</param>
	public void Subscribe(Func<int, Task<bool>> apply, Func<string> getBlocker, Action<string> record = null)
	{
		this._apply = apply ?? throw new ArgumentNullException(nameof(apply));
		this._getBlocker = getBlocker ?? (() => null);
		this._record = record;
	}

	public void SetSchedule(IReadOnlyList<BrightnessScheduleEntry> entries)
	{
		_entries = entries ?? [];

		// Keep the applied state if the entry in effect is unchanged, so that saving an edit to
		// another entry does not override a brightness the user has changed by hand since.

		if (IsEnabled)
			Check();
	}

	public void Start()
	{
		if (IsEnabled)
			return;

		_applied = null;
		_timer.Start();
		Check();
	}

	public void Stop() => _timer.Stop();

	/// <summary>
	/// Applies the entry in effect if it has not been applied yet.
	/// </summary>
	public async void Check()
	{
		if (!IsEnabled || (_apply is null) || _isApplying)
			return;

		var active = BrightnessSchedule.GetActive(_entries, DateTime.Now);
		if (active is null)
			return;

		var target = (active.Value.Since, active.Value.Entry.Brightness);
		if (target == _applied)
			return;

		var blocker = _getBlocker();
		if (blocker is not null)
		{
			// Record once per reason to avoid flooding the log every check.
			if (blocker != _lastBlocker)
				_record?.Invoke($"Scheduled brightness {target.Brightness}% from {target.Since:HH:mm} deferred: {blocker}");
			_lastBlocker = blocker;
			return; // Retry on next check.
		}
		_lastBlocker = null;

		_isApplying = true;
		try
		{
			if (await _apply(target.Brightness))
				_applied = target;
			else
				_record?.Invoke($"Scheduled brightness {target.Brightness}% from {target.Since:HH:mm} failed; retrying");
		}
		finally
		{
			_isApplying = false;
		}
	}

	#region IDisposable

	private bool _isDisposed = false;

	public void Dispose()
	{
		Dispose(true);
		GC.SuppressFinalize(this);
	}

	protected virtual void Dispose(bool disposing)
	{
		if (_isDisposed)
			return;

		if (disposing)
		{
			SystemEvents.TimeChanged -= OnTimeChanged;
			_timer.Stop();
			_apply = null;
		}

		_isDisposed = true;
	}

	#endregion
}
