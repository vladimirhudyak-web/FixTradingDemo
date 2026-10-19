using System.Collections.Concurrent;
using QuickFix;
using QuickFix.Fields;

namespace Exchange;

/// <summary>
/// A tiny "exchange" (FIX acceptor).
/// Accepts NewOrderSingle (35=D), answers with ExecutionReport (35=8):
///   New  ->  partial fill (40%)  ->  full fill.
/// Supports OrderCancelRequest (35=F) while the order is still open.
/// </summary>
public class ExchangeApp : MessageCracker, IApplication
{
    private class Order
    {
        public required string OrderId { get; init; }
        public required string ClOrdId { get; set; }
        public required string Symbol { get; init; }
        public required char Side { get; init; }
        public required decimal Qty { get; init; }
        public required decimal Price { get; init; }
        public decimal CumQty { get; set; }
        public bool Closed { get; set; }   // filled or canceled
    }

    private readonly ConcurrentDictionary<string, Order> _ordersByClOrdId = new();
    private int _orderCounter;
    private int _execCounter;

    // ---------- Session-level events (handled by QuickFIX/n) ----------

    public void OnCreate(SessionID sessionId) => Console.WriteLine($"[Session created] {sessionId}");
    public void OnLogon(SessionID sessionId) => Console.WriteLine($"[LOGON]  client connected: {sessionId}");
    public void OnLogout(SessionID sessionId) => Console.WriteLine($"[LOGOUT] {sessionId}");
    public void ToAdmin(Message message, SessionID sessionId) { }
    public void FromAdmin(Message message, SessionID sessionId) { }
    public void ToApp(Message message, SessionID sessionId) { }

    // Every application message (orders etc.) arrives here and is routed
    // by MessageCracker to the matching OnMessage(...) overload below.
    public void FromApp(Message message, SessionID sessionId) => Crack(message, sessionId);

    // ---------- Business logic ----------

    public void OnMessage(QuickFix.FIX44.NewOrderSingle msg, SessionID sessionId)
    {
        var order = new Order
        {
            OrderId = $"EX-{Interlocked.Increment(ref _orderCounter)}",
            ClOrdId = msg.GetString(Tags.ClOrdID),
            Symbol = msg.GetString(Tags.Symbol),
            Side = msg.GetChar(Tags.Side),
            Qty = msg.GetDecimal(Tags.OrderQty),
            Price = msg.IsSetField(Tags.Price) ? msg.GetDecimal(Tags.Price) : 0m
        };
        _ordersByClOrdId[order.ClOrdId] = order;

        Console.WriteLine($"[ORDER]  {order.ClOrdId}: {(order.Side == Side.BUY ? "BUY" : "SELL")} " +
                          $"{order.Qty} {order.Symbol} @ {order.Price}");

        // 1) Acknowledge: ExecType=New, OrdStatus=New
        SendExecutionReport(order, ExecType.NEW, OrdStatus.NEW, lastQty: 0, sessionId);

        // 2) Simulate the market: partial fill after 2 s, full fill after 5 s
        _ = Task.Run(async () =>
        {
            await Task.Delay(2000);
            Fill(order, Math.Floor(order.Qty * 0.4m), sessionId);

            await Task.Delay(3000);
            Fill(order, order.Qty - order.CumQty, sessionId);
        });
    }

    public void OnMessage(QuickFix.FIX44.OrderCancelRequest msg, SessionID sessionId)
    {
        string origClOrdId = msg.GetString(Tags.OrigClOrdID);
        string newClOrdId = msg.GetString(Tags.ClOrdID);

        if (!_ordersByClOrdId.TryGetValue(origClOrdId, out var order))
        {
            Console.WriteLine($"[CANCEL] unknown order {origClOrdId}");
            return;
        }

        lock (order)
        {
            if (order.Closed)
            {
                Console.WriteLine($"[CANCEL] too late, {origClOrdId} is already closed");
                return;
            }
            order.Closed = true;
            order.ClOrdId = newClOrdId;
        }
        _ordersByClOrdId[newClOrdId] = order;

        Console.WriteLine($"[CANCEL] {origClOrdId} canceled (cum qty {order.CumQty})");
        SendExecutionReport(order, ExecType.CANCELED, OrdStatus.CANCELED, lastQty: 0, sessionId,
                            origClOrdId: origClOrdId);
    }

    // ---------- Helpers ----------

    private void Fill(Order order, decimal qty, SessionID sessionId)
    {
        lock (order)
        {
            if (order.Closed || qty <= 0) return;
            order.CumQty += qty;
            if (order.CumQty >= order.Qty) order.Closed = true;
        }

        char status = order.CumQty >= order.Qty ? OrdStatus.FILLED : OrdStatus.PARTIALLY_FILLED;
        Console.WriteLine($"[FILL]   {order.ClOrdId}: {qty} filled, total {order.CumQty}/{order.Qty}");
        SendExecutionReport(order, ExecType.TRADE, status, qty, sessionId);
    }

    private void SendExecutionReport(Order order, char execType, char ordStatus, decimal lastQty,
                                     SessionID sessionId, string? origClOrdId = null)
    {
        var er = new QuickFix.FIX44.ExecutionReport();
        er.SetField(new OrderID(order.OrderId));
        er.SetField(new ExecID(Interlocked.Increment(ref _execCounter).ToString()));
        er.SetField(new ClOrdID(order.ClOrdId));
        if (origClOrdId != null) er.SetField(new OrigClOrdID(origClOrdId));
        er.SetField(new ExecType(execType));          // tag 150: what just happened
        er.SetField(new OrdStatus(ordStatus));        // tag 39:  current order state
        er.SetField(new Symbol(order.Symbol));
        er.SetField(new Side(order.Side));
        er.SetField(new OrderQty(order.Qty));
        er.SetField(new Price(order.Price));
        er.SetField(new LastQty(lastQty));            // tag 32:  qty in this fill
        er.SetField(new LastPx(lastQty > 0 ? order.Price : 0m));
        er.SetField(new CumQty(order.CumQty));        // tag 14:  total filled
        er.SetField(new LeavesQty(order.Closed ? 0 : order.Qty - order.CumQty)); // tag 151
        er.SetField(new AvgPx(order.CumQty > 0 ? order.Price : 0m));
        er.SetField(new TransactTime(DateTime.UtcNow));

        Session.SendToTarget(er, sessionId);
    }
}
