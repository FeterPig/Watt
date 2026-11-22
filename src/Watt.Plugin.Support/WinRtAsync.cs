using System;
using System.Threading.Tasks;
using Windows.Foundation;

namespace Watt.Plugins.Support
{
    /// <summary>
    /// Converts a WinRT <see cref="IAsyncOperation{T}"/> into a .NET <see cref="Task{T}"/> without
    /// depending on <c>System.Runtime.WindowsRuntime</c>.
    ///
    /// The project targets .NET Framework 4.8 and references <c>Windows.winmd</c> directly, which
    /// provides the WinRT projections but not the <c>AsTask()</c> extension (that lives in
    /// System.Runtime.WindowsRuntime, whose targeting-pack reference is absent on the build
    /// machine). Subscribing to the WinRT Completed event is exactly what AsTask does internally;
    /// the operation is closed in the completion handler so the underlying COM object is released.
    /// </summary>
    public static class WinRtAsync
    {
        public static Task<T> AsTask<T>(IAsyncOperation<T> operation)
        {
            if (operation == null) return Task.FromResult(default(T));

            var completion = new TaskCompletionSource<T>();
            try
            {
                operation.Completed = (info, status) =>
                {
                    try
                    {
                        if (status == AsyncStatus.Completed)
                        {
                            completion.TrySetResult(info.GetResults());
                        }
                        else if (status == AsyncStatus.Canceled)
                        {
                            completion.TrySetCanceled();
                        }
                        else
                        {
                            completion.TrySetException(
                                new InvalidOperationException("WinRT async operation failed."));
                        }
                    }
                    catch (Exception exception)
                    {
                        completion.TrySetException(exception);
                    }
                    finally
                    {
                        try
                        {
                            info.Close();
                        }
                        catch
                        {
                            // releasing the WinRT operation is best effort
                        }
                    }
                };
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }

            return completion.Task;
        }
    }
}

