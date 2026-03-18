using bento_order.Data;
using Microsoft.EntityFrameworkCore;

public class BentoReportService
{
    private readonly AppDbContext _db;
    private readonly LineNotifyService _lineService;

    public BentoReportService(AppDbContext db, LineNotifyService lineService)
    {
        _db = db;
        _lineService = lineService;
    }

    public async Task ProcessAndSendReportAsync()
    {
        var today = DateTime.Today;

        var orders = await _db.Orders
            .Where(o => o.OrderDate == today)
            .Select(o => new
            {
                Main = o.BentoItem!.Name,
                Extra = o.AdditionalBentoItem!.Name
            }).ToListAsync();

        var allBentos = orders
            .Select(o => o.Main)
            .Concat(orders.Select(o => o.Extra))
            .Where(name => !string.IsNullOrEmpty(name))
            .ToList();

        var orderStats = allBentos
            .GroupBy(name => name)
            .Select(g => new { Name = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .ToList();

        string messageContent = string.Empty;
        if (orderStats.Any())
        {
            messageContent = string.Join(", ", orderStats.Select(
                s => $"{s.Name}x{s.Count}"
            ));
        }
        else
        {
            messageContent = "今日無人訂餐";
        }

        int total = orderStats.Sum(s => s.Count);
        var finalMessage = $"新技術午餐統計 ({today:MM/dd}): \n" +
                           $"{messageContent}\n" +
                           $"----------------\n" +
                           $"總計: {total} 份";

        await _lineService.SendOrderSummaryAsync(finalMessage);
    }
}