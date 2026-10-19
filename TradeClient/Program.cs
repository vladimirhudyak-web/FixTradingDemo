using QuickFix;
using QuickFix.Fields;
using QuickFix.Logger;
using QuickFix.Store;
using QuickFix.Transport;
using TradeClient;

Console.WriteLine("=== FIX demo TRADE CLIENT (initiator) ===");

var settings = new SessionSettings("client.cfg");
var app = new TradeClientApp();

var initiator = new SocketInitiator(
    app,
    new FileStoreFactory(settings),
    settings,
    new FileLogFactory(settings));

initiator.Start();

Console.Write("Connecting");
for (int i = 0; i < 50 && app.SessionId is null; i++) { Thread.Sleep(200); Console.Write("."); }
Console.WriteLine();

while (true)
{
    Console.WriteLine();
    Console.WriteLine("1 = BUY 100 TEVA @ 55.20   2 = SELL 50 TEVA @ 56.00   3 = cancel last order   q = quit");
    string? key = Console.ReadLine()?.Trim().ToLower();

    switch (key)
    {
        case "1": app.SendLimitOrder("TEVA", Side.BUY, 100m, 55.20m); break;
        case "2": app.SendLimitOrder("TEVA", Side.SELL, 50m, 56.00m); break;
        case "3": app.CancelLastOrder(); break;
        case "q": initiator.Stop(); return;
    }
}
