using Porting.Cli;
using Porting.Core.Building;
using Porting.Core.Generation;
using Porting.Core.Inspection;
using Porting.Core.Planning;

using CancellationTokenSource cancellation = new();
ConsoleCancelEventHandler cancel = (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};

Console.CancelKeyPress += cancel;
try
{
    PortingCommandLine cli = new(new GradleProjectInspector(), new PortPlanner(), new DesktopProjectGenerator(), new GradleDesktopProjectBuilder());
    return await cli.RunAsync(args, cancellation.Token);
}
finally
{
    Console.CancelKeyPress -= cancel;
}