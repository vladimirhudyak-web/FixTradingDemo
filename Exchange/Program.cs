using QuickFix;
using QuickFix.Logger;
using QuickFix.Store;
using Exchange;

Console.WriteLine("=== FIX demo EXCHANGE (acceptor) ===");

var settings = new SessionSettings("exchange.cfg");
var app = new ExchangeApp();

var acceptor = new ThreadedSocketAcceptor(
    app,
    new FileStoreFactory(settings),   // keeps sequence numbers and sent messages on disk
    settings,
    new FileLogFactory(settings));    // raw FIX traffic -> log/*.messages.current.log

acceptor.Start();
Console.WriteLine("Listening on port 5001. Start TradeClient now. Press Enter to stop.");
Console.ReadLine();
acceptor.Stop();
