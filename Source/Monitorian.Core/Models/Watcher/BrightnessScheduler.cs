using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Threading;

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
	private Func<bool> _canApply;

	private IReadOnlyList<BrightnessScheduleEntry> _entries = [];
	private (DateTime Since, int Brightness)? _applied;
	private bool _isApplying;

	public BrightnessScheduler()
	{
		_timer = new DispatcherTimer { Interval = _checkInterval };
		_timer.Tick += (_, _) => Check();
	}

	public bool IsEnabled => _timer.IsEnabled;

	/// <summary>
	/// Sets the actions used to apply brightness.
	/// </summary>
	/// <param name="apply">Function to apply brightness (0-100) to monitors, returning whether any monitor accepted it</param>
	/// <param name="canApply">Function to tell whether monitors can be reached now</param>
	public void Subscribe(Func<int, Task<bool>> apply, Func<bool> canApply)
	{
		this._apply = apply ?? throw new ArgumentNullException(nameof(apply));
		this._canApply = canApply ?? (() => true);
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

		if (!_canApply())
			return; // Retry on next check.

		_isApplying = true;
		try
		{
			if (await _apply(target.Brightness))
				_applied = target;
			// Otherwise retry on next check.
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
			_timer.Stop();
			_apply = null;
		}

		_isDisposed = true;
	}

	#endregion
}
