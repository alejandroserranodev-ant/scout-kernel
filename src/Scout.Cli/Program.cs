using Scout.Cli;

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};

return await ScoutCommandLine.RunAsync(
    args,
    Console.Out,
    Console.Error,
    cancellation.Token);
