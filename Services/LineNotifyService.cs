using bento_order.Models;
using Line.Messaging;

public class LineNotifyService
{
    private readonly GlobalState _globalState;

    public LineNotifyService(GlobalState globalState)
    {
        _globalState = globalState;
    }

    public async Task SendOrderSummaryAsync(string message)
    {
        var client = new LineMessagingClient(_globalState.LineAccessToken);
        await client.PushMessageAsync(_globalState.LineId, message);
    }
}