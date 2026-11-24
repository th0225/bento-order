using System.Text;
using bento_order.Models;
using bento_order.Services;

namespace BentoOrder.Tests;

public class HolidayImportTests : IDisposable
{
    private readonly TestDb _db = new();
    private readonly BentoDbService _service;
    private readonly HolidayImportService _import;

    public HolidayImportTests()
    {
        _service = _db.CreateService();
        // 這些測試不連網路，HttpClientFactory 用不到
        _import = new HolidayImportService(null!, _service);
    }

    public void Dispose() => _db.Dispose();

    // 產生與政府 CSV 相同格式的一整年資料：週末放假、平日上班，再套用特例
    private static string BuildCsv(int year,
        Dictionary<DateTime, (string Flag, string Note)> overrides)
    {
        var sb = new StringBuilder("﻿西元日期,星期,是否放假,備註\r\n");
        var weekNames = "日一二三四五六";
        for (var d = new DateTime(year, 1, 1); d.Year == year; d = d.AddDays(1))
        {
            var weekend = d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
            var (flag, note) = overrides.TryGetValue(d, out var o)
                ? o : (weekend ? "2" : "0", "");
            sb.Append($"{d:yyyyMMdd},{weekNames[(int)d.DayOfWeek]},{flag},{note}\r\n");
        }
        return sb.ToString();
    }

    [Fact]
    public void 解析政府CSV格式()
    {
        var csv = BuildCsv(2027, new()
        {
            [new DateTime(2027, 1, 1)] = ("2", "開國紀念日")
        });

        var days = HolidayImportService.ParseCsv(csv);

        Assert.Equal(365, days.Count);
        var newYear = days.Single(d => d.Date == new DateTime(2027, 1, 1));
        Assert.True(newYear.IsHoliday);
        Assert.Equal("開國紀念日", newYear.Note);
        Assert.False(days.Single(d => d.Date == new DateTime(2027, 1, 4)).IsHoliday);
    }

    [Fact]
    public void 格式不符_拒絕()
    {
        Assert.Throws<InvalidOperationException>(() =>
            HolidayImportService.ParseCsv("Subject,Start Date\nfoo,2027/01/01"));
    }

    [Fact]
    public async Task 匯入_平日假日鎖定_週末補班開放_其他日期不動()
    {
        var holiday = new DateTime(2027, 1, 1);        // 星期五
        var workSaturday = new DateTime(2027, 2, 20);  // 星期六補班
        var manual = new DateTime(2027, 3, 3);         // 管理員自行鎖定的平日
        await _service.SaveLockedDateAsync(new LockedDate { Date = manual, IsLocked = true });

        var result = await _import.ImportFromCsvAsync(BuildCsv(2027, new()
        {
            [holiday] = ("2", "開國紀念日"),
            [workSaturday] = ("0", "補行上班"),
        }), expectedYear: 2027);

        Assert.Equal(2027, result.Year);
        Assert.Equal(holiday, Assert.Single(result.LockedHolidays).Date);
        Assert.Equal(workSaturday, Assert.Single(result.OpenedWorkdays).Date);

        var jan = await _service.GetLockedDatesAsync(2027, 1);
        var newYear = Assert.Single(jan);
        Assert.True(newYear.IsLocked);
        Assert.Equal("開國紀念日", newYear.Note);

        var feb = await _service.GetLockedDatesAsync(2027, 2);
        Assert.False(Assert.Single(feb).IsLocked);

        Assert.True(Assert.Single(await _service.GetLockedDatesAsync(2027, 3)).IsLocked);

        // 補班日依規則開放
        Assert.False(OrderLockPolicy.IsLocked(workSaturday,
            new DateTime(2027, 2, 1, 8, 0, 0), Assert.Single(feb)));
    }

    [Fact]
    public async Task 重複匯入_更新既有紀錄不重複()
    {
        var holiday = new DateTime(2027, 1, 1);
        // 管理員先前手動解鎖了這天
        await _service.SaveLockedDateAsync(new LockedDate { Date = holiday, IsLocked = false });

        var csv = BuildCsv(2027, new() { [holiday] = ("2", "開國紀念日") });
        await _import.ImportFromCsvAsync(csv);
        await _import.ImportFromCsvAsync(csv);

        var record = Assert.Single(await _service.GetLockedDatesAsync(2027, 1));
        Assert.True(record.IsLocked);
        Assert.Equal("開國紀念日", record.Note);
    }

    [Fact]
    public async Task 年度不符_拒絕()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _import.ImportFromCsvAsync(BuildCsv(2027, new()), expectedYear: 2026));
    }

    [Fact]
    public async Task 管理員切換鎖定_保留備註()
    {
        var holiday = new DateTime(2027, 1, 1);
        await _import.ImportFromCsvAsync(
            BuildCsv(2027, new() { [holiday] = ("2", "開國紀念日") }));

        await _service.SaveLockedDateAsync(new LockedDate { Date = holiday, IsLocked = false });

        var record = Assert.Single(await _service.GetLockedDatesAsync(2027, 1));
        Assert.False(record.IsLocked);
        Assert.Equal("開國紀念日", record.Note);
    }
}
