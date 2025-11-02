using System.Diagnostics.CodeAnalysis;

namespace BackgroundJobs
{
    /// <summary>
    /// Extension methods for IJob to provide waiting and completion functionality.
    /// </summary>
    public static class JobExtensions
    {
        /// <summary>
        /// Waits for job completion using TaskCompletionSource instead of polling.
        /// </summary>
        /// <param name="job">The job to wait for completion</param>
        /// <param name="cancellationToken">Cancellation token to cancel the wait operation</param>
        /// <returns>Task that completes when the job reaches a terminal state (Completed, Failed, or Cancelled)</returns>
        /// <exception cref="InvalidOperationException">Thrown when job fails or is cancelled</exception>
        /// <exception cref="OperationCanceledException">Thrown when cancellation is requested</exception>
        public static async Task<JobResult> WaitForCompletionAsync(this IJob job,
            CancellationToken cancellationToken = default)
        {
            return await WaitForCompletionAsync(job, null, null, cancellationToken);
        }

        /// <summary>
        /// Waits for job completion with custom failure handling.
        /// </summary>
        /// <param name="job">The job to wait for completion</param>
        /// <param name="onFailure">Function to handle job failure</param>
        /// <param name="cancellationToken">Cancellation token to cancel the wait operation</param>
        /// <returns>Task that completes when the job reaches a terminal state</returns>
        public static async Task<JobResult> WaitForCompletionAsync(this IJob job,
            Func<JobResult, Task<JobResult>>? onFailure, CancellationToken cancellationToken = default)
        {
            return await WaitForCompletionAsync(job, onFailure, null, cancellationToken);
        }

        /// <summary>
        /// Waits for job completion with custom failure and cancellation handling.
        /// </summary>
        /// <param name="job">The job to wait for completion</param>
        /// <param name="onFailure">Function to handle job failure</param>
        /// <param name="onCancellation">Function to handle job cancellation</param>
        /// <param name="cancellationToken">Cancellation token to cancel the wait operation</param>
        /// <returns>Task that completes when the job reaches a terminal state</returns>
        [SuppressMessage("ReSharper", "AsyncVoidLambda")]
        public static async Task<JobResult> WaitForCompletionAsync(
            this IJob job,
            Func<JobResult, Task<JobResult>>? onFailure,
            Func<JobResult, Task<JobResult>>? onCancellation,
            CancellationToken cancellationToken = default)
        {
            JobResult result = job.GetResult();

            if (!result.IsRunning)
            {
                switch (result.Status)
                {
                    case JobStatus.Failed when onFailure != null:
                        return await onFailure(result);
                    case JobStatus.Cancelled when onCancellation != null:
                        return await onCancellation(result);
                    case JobStatus.Failed:
                    case JobStatus.Cancelled:
                        throw new InvalidOperationException(
                            $"Job {job.Name} {result.Status.ToString().ToLower()}: {result.ErrorMessage}");
                    default:
                        return result;
                }
            }

            TaskCompletionSource<JobResult> tcs = new TaskCompletionSource<JobResult>();
            
            void StatusChangedHandler(JobResult r)
            {
                if (r.IsRunning) return;

                try
                {
                    job.StatusChanged -= StatusChangedHandler;
                    
                    switch (r.Status)
                    {
                        case JobStatus.Failed when onFailure != null:
                            tcs.SetResult(onFailure(r).GetAwaiter().GetResult());
                            break;
                        case JobStatus.Cancelled when onCancellation != null:
                            tcs.SetResult(onCancellation(r).GetAwaiter().GetResult());
                            break;
                        case JobStatus.Failed:
                        case JobStatus.Cancelled:
                            tcs.SetException(new InvalidOperationException(
                                $"Job {job.Name} {r.Status.ToString().ToLower()}: {r.ErrorMessage}"));
                            break;
                        default:
                            tcs.SetResult(r);
                            break;
                    }
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            }

            job.StatusChanged += StatusChangedHandler;

            using (cancellationToken.Register(() =>
                   {
                       job.StatusChanged -= StatusChangedHandler;
                       tcs.TrySetCanceled(cancellationToken);
                   }))
            {
                return await tcs.Task;
            }
        }
    }
}
