using QuickFix;
using QuickFix.Fields;

namespace TradeClient;

/// <summary>
/// FIX client (initiator): sends orders and cancels, prints ExecutionReports.
/// </summary>
public class TradeClientApp : MessageCracker, IApplication
{
    public SessionID? SessionId { get; private set; }
    public string? LastClOrdId { get; private set; }
    private char _lastSide = Side.BUY;
    private int _orderCounter;

    // ---------- Session-level events ----------

    public void OnCreate(SessionID sessionId) { }

    public void OnLogon(SessionID sessionId)
    {
        SessionId = sessionId;
        Console.WriteLine($"[LOGON]  connected to exchange: {sessionId}");
    }

    public void OnLogout(SessionID sessionId)
    {
        SessionId = null;
        Console.WriteLine("[LOGOUT] disconnected (QuickFIX/n will reconnect automatically)");
    }

    public void ToAdmin(Message message, SessionID sessionId)
    {
        // Real brokers often require credentials in Logon (tags 553/554), e.g.:
        // if (message.Header.GetString(Tags.MsgType) == MsgType.LOGON)
        //     message.SetField(new Username("user"));
    }

    public void FromAdmin(Message message, SessionID sessionId) { }
    public void ToApp(Message message, SessionID sessionId) { }
    public void FromApp(Message message, SessionID sessionId) => Crack(message, sessionId);

    // ---------- Incoming business messages ----------

    public void OnMessage(QuickFix.FIX44.ExecutionReport er, SessionID sessionId)
    {
        string execType = er.GetChar(Tags.ExecType) switch
        {
            ExecType.NEW => "NEW",
            ExecType.TRADE => "TRADE",
            ExecType.CANCELED => "CANCELED",
            ExecType.REJECTED => "REJECTED",
            var c => c.ToString()
        };
        string ordStatus = er.GetChar(Tags.OrdStatus) switch
        {
            OrdStatus.NEW => "New",
            OrdStatus.PARTIALLY_FILLED => "PartiallyFilled",
            OrdStatus.FILLED => "Filled",
            OrdStatus.CANCELED => "Canceled",
            OrdStatus.REJECTED => "Rejected",
            var c => c.ToString()
        };

        Console.WriteLine(
            $"[EXEC]   {er.GetString(Tags.ClOrdID)}  ExecType={execType,-8} Status={ordStatus,-15} " +
            $"LastQty={er.GetDecimal(Tags.LastQty)}  CumQty={er.GetDecimal(Tags.CumQty)}  " +
            $"LeavesQty={er.GetDecimal(Tags.LeavesQty)}");
    }

    public void OnMessage(QuickFix.FIX44.OrderCancelReject reject, SessionID sessionId)
    {
        Console.WriteLine($"[REJECT] cancel rejected for {reject.GetString(Tags.OrigClOrdID)}");
    }

    // ---------- Outgoing business messages ----------

    public void SendLimitOrder(string symbol, char side, decimal qty, decimal price)
    {
        if (SessionId is null) { Console.WriteLine("Not connected yet."); return; }

        string clOrdId = $"ORD-{DateTime.Now:HHmmss}-{++_orderCounter}";

        var order = new QuickFix.FIX44.NewOrderSingle();      // 35=D
        order.SetField(new ClOrdID(clOrdId));                 // 11
        order.SetField(new Symbol(symbol));                   // 55
        order.SetField(new Side(side));                       // 54: 1=Buy, 2=Sell
        order.SetField(new OrderQty(qty));                    // 38
        order.SetField(new OrdType(OrdType.LIMIT));           // 40: 2=Limit
        order.SetField(new Price(price));                     // 44
        order.SetField(new TimeInForce(TimeInForce.DAY));     // 59
        order.SetField(new TransactTime(DateTime.UtcNow));    // 60

        LastClOrdId = clOrdId;
        _lastSide = side;
        Session.SendToTarget(order, SessionId);
        Console.WriteLine($"[SENT]   NewOrderSingle {clOrdId}");
    }

    public void CancelLastOrder()
    {
        if (SessionId is null || LastClOrdId is null) { Console.WriteLine("Nothing to cancel."); return; }

        string newClOrdId = $"CXL-{DateTime.Now:HHmmss}-{++_orderCounter}";

        var cancel = new QuickFix.FIX44.OrderCancelRequest(); // 35=F
        cancel.SetField(new OrigClOrdID(LastClOrdId));        // 41: which order to cancel
        cancel.SetField(new ClOrdID(newClOrdId));             // 11: new ID for the cancel itself
        cancel.SetField(new Side(_lastSide));
        cancel.SetField(new TransactTime(DateTime.UtcNow));

        Session.SendToTarget(cancel, SessionId);
        Console.WriteLine($"[SENT]   OrderCancelRequest for {LastClOrdId}");
        LastClOrdId = newClOrdId;
    }
}
