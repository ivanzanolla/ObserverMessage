using System;
using System.Threading.Tasks;

namespace Messages.Tests
{
    /// <summary>Task.WaitAsync is not available on .NET Framework: this works on every target.</summary>
    internal static class TaskTimeoutExtensions
    {
        public static async Task WithTimeout(this Task task, TimeSpan timeout)
        {
            if (await Task.WhenAny(task, Task.Delay(timeout)).ConfigureAwait(false) != task)
                throw new TimeoutException($"The operation did not complete within {timeout}.");
            await task.ConfigureAwait(false);
        }

        public static async Task<T> WithTimeout<T>(this Task<T> task, TimeSpan timeout)
        {
            await ((Task)task).WithTimeout(timeout).ConfigureAwait(false);
            return await task.ConfigureAwait(false);
        }
    }
}
