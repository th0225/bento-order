using Line.Messaging;

public class LineNotifyService
{
    private readonly string _accessToken = "BOGIUMZ1fcAUZEHB+89xrV/5WN6WPPBuSsSeKpienejA3ld9ndKyIVBbzl9qDQpPcsN0CQ8R0n0VEDym3KYlGzBnE7xhxv8T2peRBGIl4hH0hqVqEuIS4iHBv1Ae9AK+K2dEMkqUm0YFkwb/5s5D/gdB04t89/1O/w1cDnyilFU=";
    private readonly string _targetId = "C2f085d953b848ba60e775cd2686f2a37";

    public async Task SendOrderSummaryAsync(string message)
    {
        var client = new LineMessagingClient(_accessToken);
        await client.PushMessageAsync(_targetId, message);
    }
}