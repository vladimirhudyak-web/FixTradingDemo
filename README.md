# FixTradingDemo

A small, runnable demo of the **FIX 4.4 protocol** in C# using **QuickFIX/n**.

Two console applications talk to each other over TCP:

| Project | FIX role | What it does |
|---|---|---|
| `Exchange` | **Acceptor** (server, port 5001) | Accepts orders, simulates the market, sends Execution Reports |
| `TradeClient` | **Initiator** (client) | Connects, sends buy/sell orders and cancels, prints Execution Reports |

```
TradeClient (initiator)                         Exchange (acceptor)
        |  Logon 35=A, HeartBtInt=30  ------------>  |
        |  <------------------------  Logon 35=A     |
        |  NewOrderSingle 35=D  ------------------>  |
        |  <---------  ExecutionReport 35=8  (New)   |
        |  <---------  ExecutionReport 35=8  (PartiallyFilled 40/100)
        |  <---------  ExecutionReport 35=8  (Filled 100/100)
        |  Heartbeat 35=0  <---------------------->  |   every 30 s
        |  OrderCancelRequest 35=F  -------------->  |   (optional)
        |  <---------  ExecutionReport 35=8  (Canceled)
```

QuickFIX/n handles the **session layer** (Logon, Heartbeat, sequence numbers, resend, persistence).
The application code only handles the **business layer** (orders and executions).

## Requirements

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (or Visual Studio 2022)
- NuGet packages (restored automatically): `QuickFIXn.Core` 1.14.1, `QuickFIXn.FIX44` 1.14.1

## How to run

**Option A — command line (two terminal windows):**

```bash
# Terminal 1: start the exchange first
cd Exchange
dotnet run

# Terminal 2: then the client
cd TradeClient
dotnet run
```

In the client press `1` (buy), `2` (sell) or `3` (cancel the last order before it is fully filled).

**Option B — Visual Studio 2022:** open `FixTradingDemo.sln` →
right-click the solution → *Configure Startup Projects* → *Multiple startup projects* →
set both to **Start** (Exchange first) → F5.

**Option C — VS Code:** *Run and Debug* (Ctrl+Shift+D) → select **Exchange + TradeClient** → F5.
Both apps start in separate integrated terminals; type commands in the TradeClient terminal.

## Project structure

```
FixTradingDemo/
├── Exchange/
│   ├── exchange.cfg       # acceptor session config
│   ├── ExchangeApp.cs     # order handling + execution simulation
│   └── Program.cs         # starts ThreadedSocketAcceptor
└── TradeClient/
    ├── client.cfg         # initiator session config
    ├── TradeClientApp.cs  # sends NewOrderSingle / OrderCancelRequest, handles ExecutionReport
    └── Program.cs         # starts SocketInitiator + console menu
```

## Key FIX tags used

| Tag | Name | Example |
|---|---|---|
| 35 | MsgType | D = NewOrderSingle, F = OrderCancelRequest, 8 = ExecutionReport |
| 11 / 41 | ClOrdID / OrigClOrdID | Client order ID / ID of the order being canceled |
| 55 / 54 | Symbol / Side | TEVA / 1 = Buy, 2 = Sell |
| 38 / 44 / 40 | OrderQty / Price / OrdType | 100 / 55.20 / 2 = Limit |
| 150 / 39 | ExecType / OrdStatus | What happened / current order state |
| 32 / 14 / 151 | LastQty / CumQty / LeavesQty | This fill / total filled / remaining |

## Logs

Raw FIX traffic is written to `log/` next to each executable
(`bin/Debug/net8.0/log/*.messages.current.log`). Sequence numbers and sent messages are stored in `store/`.
Delete both folders to reset sequence numbers.

## Ideas for extension

- Order modification (`OrderCancelReplaceRequest`, 35=G)
- Order rejection on invalid price/quantity (`ExecType=Rejected`)
- Market data subscription (`MarketDataRequest`, 35=V / `MarketDataSnapshotFullRefresh`, 35=W)
- Unit tests for the order state machine
