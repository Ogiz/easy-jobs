using BackgroundJobs.Tests.TestJobs;

namespace BackgroundJobs.Tests
{
    public class JobTests
    {
        [Fact]
        public async Task ExecuteSuccessfulJob_CompletesWithSuccessStatus()
        {
            var job = new SuccessJob();

            job.Execute();
            var result = await job.WaitForCompletionAsync();

            Assert.Equal(JobStatus.Completed, result.Status);
            Assert.NotNull(result.StartTime);
            Assert.NotNull(result.EndTime);
            Assert.Null(result.ErrorMessage);
        }

        [Fact]
        public async Task ExecuteFailingJob_CompletesWithFailedStatus()
        {
            var expectedError = "Test error message";
            var job = new FailingJob(expectedError);

            job.Execute();

            var result = await job.WaitForCompletionAsync(
                onFailure: failedResult => Task.FromResult(failedResult)
            );

            Assert.Equal(JobStatus.Failed, result.Status);
            Assert.NotNull(result.StartTime);
            Assert.NotNull(result.EndTime);
            Assert.Equal(expectedError, result.ErrorMessage);
        }

        [Fact]
        public async Task CancelRunningJob_TransitionsToCancelledStatus()
        {
            var job = new CancellableJob();

            job.Execute();

            await Task.Delay(TimeSpan.FromMilliseconds(500));

            job.Cancel("Test cancellation");

            var result = job.GetResult();

            Assert.Equal(JobStatus.Cancelled, result.Status);
            Assert.NotNull(result.StartTime);
            Assert.NotNull(result.EndTime);
            Assert.Equal("Test cancellation", result.ErrorMessage);
        }

        [Fact]
        public async Task ExecuteJobTwice_SecondExecuteIsIgnored()
        {
            var job = new SuccessJob();

            job.Execute();
            await job.WaitForCompletionAsync();

            var firstResult = job.GetResult();
            var firstEndTime = firstResult.EndTime;

            await Task.Delay(TimeSpan.FromMilliseconds(100));

            job.Execute();

            var secondResult = job.GetResult();

            Assert.Equal(JobStatus.Completed, secondResult.Status);
            Assert.Equal(firstEndTime, secondResult.EndTime);
        }

        [Fact]
        public async Task GetResult_CalculatesElapsedTimeCorrectly()
        {
            var job = new CancellableJob(TimeSpan.FromMilliseconds(500));

            job.Execute();

            await Task.Delay(TimeSpan.FromMilliseconds(200));

            var runningResult = job.GetResult();
            Assert.NotNull(runningResult.ElapsedTime);
            Assert.Matches(@"\d{2}:\d{2}:\d{2}", runningResult.ElapsedTime);

            await job.WaitForCompletionAsync();

            var completedResult = job.GetResult();
            Assert.NotNull(completedResult.ElapsedTime);
            Assert.Matches(@"\d{2}:\d{2}:\d{2}", completedResult.ElapsedTime);
        }

        [Fact]
        public async Task StatusChangedEvent_FiresOnStatusTransitions()
        {
            var job = new CancellableJob(TimeSpan.FromMilliseconds(100));
            var statusChanges = new List<JobStatus>();

            job.StatusChanged += result => statusChanges.Add(result.Status);

            job.Execute();
            await job.WaitForCompletionAsync();

            Assert.Contains(JobStatus.Running, statusChanges);
            Assert.Contains(JobStatus.Completed, statusChanges);
        }

        [Fact]
        public void CancelPendingJob_TransitionsToCancelledWithoutRunning()
        {
            var job = new CancellableJob();

            var pendingResult = job.GetResult();
            Assert.Equal(JobStatus.Pending, pendingResult.Status);

            job.Cancel("Cancelled before execution");

            var cancelledResult = job.GetResult();
            Assert.Equal(JobStatus.Cancelled, cancelledResult.Status);
            Assert.Null(cancelledResult.StartTime);
            Assert.Equal("Cancelled before execution", cancelledResult.ErrorMessage);
        }

        [Fact]
        public async Task DisposeJob_ProperlyDisposesResources()
        {
            var job = new SuccessJob();

            job.Execute();
            await job.WaitForCompletionAsync();

            job.Dispose();

            await Task.Yield();

            var result = job.GetResult();
            Assert.Equal(JobStatus.Completed, result.Status);
        }

        [Fact]
        public void ExecuteAfterCancel_DoesNotCallExecuteInternal()
        {
            var job = new TrackableJob();

            job.Cancel("Cancelled before execution");

            job.Execute();

            Assert.False(job.ExecuteInternalCalled);
            Assert.Equal(JobStatus.Cancelled, job.GetResult().Status);
            Assert.Equal("Cancelled before execution", job.GetResult().ErrorMessage);
        }

        [Fact]
        public void ExecuteAfterDispose_DoesNotCallExecuteInternal()
        {
            var job = new TrackableJob();

            job.Dispose();

            job.Execute();

            Assert.False(job.ExecuteInternalCalled);
            Assert.Equal(JobStatus.Cancelled, job.GetResult().Status);
            Assert.Equal("Job disposed before execution", job.GetResult().ErrorMessage);
        }

        [Fact]
        public void ExecuteAfterCancelFromPending_MaintainsCancelledStatus()
        {
            var job = new TrackableJob();

            var pendingResult = job.GetResult();
            Assert.Equal(JobStatus.Pending, pendingResult.Status);

            job.Cancel("Custom cancellation reason");

            var cancelledResult = job.GetResult();
            Assert.Equal(JobStatus.Cancelled, cancelledResult.Status);

            job.Execute();

            var finalResult = job.GetResult();
            Assert.Equal(JobStatus.Cancelled, finalResult.Status);
            Assert.Equal("Custom cancellation reason", finalResult.ErrorMessage);
            Assert.False(job.ExecuteInternalCalled);
        }

        [Fact]
        public void ExecuteAfterDisposeFromPending_TransitionsToCancelled()
        {
            var job = new TrackableJob();

            var pendingResult = job.GetResult();
            Assert.Equal(JobStatus.Pending, pendingResult.Status);

            job.Dispose();

            var disposedResult = job.GetResult();
            Assert.Equal(JobStatus.Cancelled, disposedResult.Status);
            Assert.Equal("Job disposed before execution", disposedResult.ErrorMessage);
            Assert.NotNull(disposedResult.EndTime);

            job.Execute();

            var finalResult = job.GetResult();
            Assert.Equal(JobStatus.Cancelled, finalResult.Status);
            Assert.False(job.ExecuteInternalCalled);
        }
    }
}
