using System;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Monitorian.Core.Models;

namespace Monitorian.Test;

[TestClass]
public class BrightnessScheduleTest
{
	[TestMethod]
	public void TestParseSortsEntries()
	{
		Assert.IsTrue(BrightnessSchedule.TryParse("22:00 20\n7:00 80; 19:00=50%", out var entries, out _));
		Assert.AreEqual("07:00 80; 19:00 50; 22:00 20", BrightnessSchedule.Format(entries));
	}

	[TestMethod]
	public void TestParseIgnoresCommentsAndBlankLines()
	{
		Assert.IsTrue(BrightnessSchedule.TryParse("# morning\r\n\r\n07:00 80\r\n", out var entries, out _));
		Assert.AreEqual(1, entries.Count);
	}

	[DataTestMethod]
	[DataRow("07:00")]
	[DataRow("25:00 50")]
	[DataRow("07:00 101")]
	[DataRow("07:00 80; 07:00 50")]
	public void TestParseRejectsInvalid(string text)
	{
		Assert.IsFalse(BrightnessSchedule.TryParse(text, out _, out var error));
		Assert.IsNotNull(error);
	}

	[TestMethod]
	public void TestGetActiveWithinDay()
	{
		var entries = BrightnessSchedule.Parse("07:00 80; 22:00 20");
		var active = BrightnessSchedule.GetActive(entries, new DateTime(2026, 11, 26, 12, 0, 0));

		Assert.AreEqual(80, active.Value.Entry.Brightness);
		Assert.AreEqual(new DateTime(2026, 11, 26, 7, 0, 0), active.Value.Since);
	}

	[TestMethod]
	public void TestGetActiveWrapsAroundMidnight()
	{
		var entries = BrightnessSchedule.Parse("07:00 80; 22:00 20");
		var active = BrightnessSchedule.GetActive(entries, new DateTime(2026, 11, 26, 3, 0, 0));

		Assert.AreEqual(20, active.Value.Entry.Brightness);
		Assert.AreEqual(new DateTime(2026, 11, 25, 22, 0, 0), active.Value.Since);
	}

	[TestMethod]
	public void TestGetActiveEmpty()
	{
		Assert.IsNull(BrightnessSchedule.GetActive(BrightnessSchedule.Parse(""), DateTime.Now));
	}
}
