using System.Globalization;
using System.Text.Json;
using bento_order.Models;

namespace bento_order.Services;

// 匯入「中華民國政府行政機關辦公日曆表」（行政院人事行政總處，政府資料開放平臺 14718）：
//   平日放假（國定假日、彈性放假）→ 鎖定
//   週末上班（補班日）→ 開放
// 只寫入上述日期，其他日期（含管理員自行設定的）不受影響。
//
// CSV 格式（每年一個檔，UTF-8）：
//   西元日期,星期,是否放假,備註
//   20260101,四,2,開國紀念日      ← 是否放假：2 = 放假、0 = 上班
public class HolidayImportService
{
    // 政府資料開放平臺的資料集 API，回傳每年 CSV 的下載網址
    private const string DatasetApiUrl =
        "https://data.gov.tw/api/v2/rest/dataset/14718";

    public record CalendarDay(DateTime Date, bool IsHoliday, string Note);

    // 匯入結果：鎖定的平日假日、開放的週末補班日
    public record ImportResult(int Year, List<CalendarDay> LockedHolidays,
        List<CalendarDay> OpenedWorkdays);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly BentoDbService _bentoDbService;

    public HolidayImportService(IHttpClientFactory httpClientFactory,
        BentoDbService bentoDbService)
    {
        _httpClientFactory = httpClientFactory;
        _bentoDbService = bentoDbService;
    }

    // 從政府資料開放平臺下載指定年度（西元）的辦公日曆並匯入
    public async Task<ImportResult> ImportFromGovernmentAsync(int year,
        CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(30);

        var csvUrl = await FindCsvUrlAsync(client, year, cancellationToken);
        var csv = await client.GetStringAsync(csvUrl, cancellationToken);

        return await ImportFromCsvAsync(csv, year);
    }

    // 匯入 CSV 內容（手動上傳時使用）；expectedYear 有值時檢查年度是否相符
    public async Task<ImportResult> ImportFromCsvAsync(string csv,
        int? expectedYear = null)
    {
        var days = ParseCsv(csv);
        var years = days.Select(d => d.Date.Year).Distinct().ToList();
        if (years.Count != 1)
        {
            throw new InvalidOperationException("CSV 應只包含同一年度的日期。");
        }

        var year = years[0];
        if (expectedYear.HasValue && expectedYear != year)
        {
            throw new InvalidOperationException(
                $"CSV 是 {year} 年的資料，不是 {expectedYear} 年。");
        }

        var lockedHolidays = days
            .Where(d => d.IsHoliday && !IsWeekend(d.Date))
            .ToList();
        var openedWorkdays = days
            .Where(d => !d.IsHoliday && IsWeekend(d.Date))
            .ToList();

        await _bentoDbService.ApplyLockedDatesAsync(
            lockedHolidays.Select(d => new LockedDate
                {
                    Date = d.Date,
                    IsLocked = true,
                    Note = d.Note
                })
                .Concat(openedWorkdays.Select(d => new LockedDate
                {
                    Date = d.Date,
                    IsLocked = false,
                    Note = string.IsNullOrEmpty(d.Note) ? "補班" : d.Note
                })));

        return new ImportResult(year, lockedHolidays, openedWorkdays);
    }

    // 解析辦公日曆 CSV
    public static List<CalendarDay> ParseCsv(string csv)
    {
        var days = new List<CalendarDay>();
        var lines = csv.TrimStart('﻿')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);

        foreach (var rawLine in lines)
        {
            var fields = rawLine.Trim().Split(',');
            // 跳過標題列與格式不符的列
            if (fields.Length < 3 ||
                !DateTime.TryParseExact(fields[0].Trim(), "yyyyMMdd",
                    CultureInfo.InvariantCulture, DateTimeStyles.None,
                    out var date))
            {
                continue;
            }

            var isHoliday = fields[2].Trim() switch
            {
                "2" => true,
                "0" => false,
                _ => throw new InvalidOperationException(
                    $"無法辨識 {fields[0]} 的「是否放假」欄位：{fields[2]}")
            };
            var note = fields.Length > 3
                ? string.Join(",", fields.Skip(3)).Trim().Trim('"')
                : string.Empty;

            days.Add(new CalendarDay(date, isHoliday, note));
        }

        if (days.Count < 300)
        {
            throw new InvalidOperationException(
                "CSV 格式不符，請確認是「中華民國政府行政機關辦公日曆表」（非 Google 行事曆專用版）。");
        }

        return days;
    }

    private static bool IsWeekend(DateTime date) =>
        date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;

    // 從資料集 API 找出該年度 CSV 的下載網址（排除 Google 行事曆專用版）
    private static async Task<string> FindCsvUrlAsync(HttpClient client,
        int year, CancellationToken cancellationToken)
    {
        var rocYear = year - 1911;
        var title = $"{rocYear}年中華民國政府行政機關辦公日曆表";

        await using var stream = await client.GetStreamAsync(DatasetApiUrl,
            cancellationToken);
        using var json = await JsonDocument.ParseAsync(stream,
            cancellationToken: cancellationToken);

        var url = json.RootElement
            .GetProperty("result")
            .GetProperty("distribution")
            .EnumerateArray()
            .Where(r =>
            {
                var description = r.GetProperty("resourceDescription")
                    .GetString() ?? string.Empty;
                return description.StartsWith(title) &&
                    !description.Contains("Google");
            })
            .Select(r => r.GetProperty("resourceDownloadUrl").GetString())
            .FirstOrDefault(u => !string.IsNullOrEmpty(u));

        return url ?? throw new InvalidOperationException(
            $"政府資料開放平臺尚未提供 {year} 年（民國 {rocYear} 年）的辦公日曆。");
    }
}
