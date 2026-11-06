namespace Zarpazo.Tests;

internal static class Sta
{
    /// <summary>Runs a test body on an STA thread: WPF elements can only be created on one, and xunit's threads aren't.</summary>
    public static void Run(Action test)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { test(); }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure != null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}
