using BackgroundJobs.Tests.TestJobs;

namespace BackgroundJobs.Tests
{
    public class JobExtensionsTests
    {
        [Fact]
        public async Task WaitForCompletionAsync_AutoExecutesPendingJob()
        {
            var job = new SuccessJob();

            var initialResult = job.GetResult();
            Assert.Equal(JobStatus.Pending, initialResult.Status);

            var finalResult = await job.WaitForCompletionAsync();

            Assert.Equal(JobStatus.Completed, finalResult.Status);
            Assert.NotEqual(JobStatus.Pending, finalResult.Status);
        }

        [Fact]
        public async Task WaitForCompletionAsync_WithFailure_AutoExecutesPendingJob()
        {
            var job = new FailingJob("Test error");

            var initialResult = job.GetResult();
            Assert.Equal(JobStatus.Pending, initialResult.Status);

            var finalResult = await job.WaitForCompletionAsync(
                onFailure: failedResult => Task.FromResult(failedResult)
            );

            Assert.Equal(JobStatus.Failed, finalResult.Status);
            Assert.NotEqual(JobStatus.Pending, finalResult.Status);
            Assert.Equal("Test error", finalResult.ErrorMessage);
        }

        [Fact]
        public async Task WaitForCompletionAsync_WithCancellation_AutoExecutesPendingJob()
        {
            var job = new CancellableJob(TimeSpan.FromSeconds(5));

            var initialResult = job.GetResult();
            Assert.Equal(JobStatus.Pending, initialResult.Status);

            var waitTask = job.WaitForCompletionAsync(
                onFailure: null,
                onCancellation: cancelledResult => Task.FromResult(cancelledResult)
            );

            await Task.Delay(50);
            job.Cancel("Test cancellation");

            var finalResult = await waitTask;

            Assert.Equal(JobStatus.Cancelled, finalResult.Status);
            Assert.NotEqual(JobStatus.Pending, finalResult.Status);
        }

        [Fact]
        public async Task WaitForCompletionAsync_OnAlreadyRunningJob_DoesNotExecuteAgain()
        {
            var job = new TrackableJob();

            job.Execute();
            await Task.Delay(10);

            var runningResult = job.GetResult();
            Assert.True(runningResult.Status == JobStatus.Running || runningResult.Status == JobStatus.Completed);

            await job.WaitForCompletionAsync();

            Assert.True(job.ExecuteInternalCalled);
        }

        [Fact]
        public async Task WaitForCompletionAsync_OnCompletedJob_ReturnsImmediately()
        {
            var job = new SuccessJob();

            job.Execute();
            await job.WaitForCompletionAsync();

            var firstResult = job.GetResult();
            Assert.Equal(JobStatus.Completed, firstResult.Status);

            var secondResult = await job.WaitForCompletionAsync();

            Assert.Equal(JobStatus.Completed, secondResult.Status);
            Assert.Equal(firstResult.EndTime, secondResult.EndTime);
        }

        [Fact]
        public async Task WaitForCompletionAsync_OnFailedJob_ThrowsOrHandlesWithCallback()
        {
            var job = new FailingJob("Already failed");

            job.Execute();
            await job.WaitForCompletionAsync(
                onFailure: failedResult => Task.FromResult(failedResult)
            );

            var firstResult = job.GetResult();
            Assert.Equal(JobStatus.Failed, firstResult.Status);

            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await job.WaitForCompletionAsync();
            });

            var secondResult = await job.WaitForCompletionAsync(
                onFailure: failedResult => Task.FromResult(failedResult)
            );

            Assert.Equal(JobStatus.Failed, secondResult.Status);
            Assert.Equal(firstResult.EndTime, secondResult.EndTime);
        }

        [Fact]
        public async Task WaitForCompletionAsync_OnCancelledJob_ThrowsOrHandlesWithCallback()
        {
            var job = new CancellableJob(TimeSpan.FromSeconds(1));

            job.Execute();
            await Task.Delay(10);
            job.Cancel("Pre-cancelled");

            var firstResult = job.GetResult();
            Assert.Equal(JobStatus.Cancelled, firstResult.Status);

            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await job.WaitForCompletionAsync();
            });

            var secondResult = await job.WaitForCompletionAsync(
                onFailure: null,
                onCancellation: cancelledResult => Task.FromResult(cancelledResult)
            );

            Assert.Equal(JobStatus.Cancelled, secondResult.Status);
        }

        [Fact]
        public async Task WaitForCompletionAsync_MultipleCalls_AllReturnSameResult()
        {
            var job = new CancellableJob(TimeSpan.FromMilliseconds(100));

            var task1 = job.WaitForCompletionAsync();
            var task2 = job.WaitForCompletionAsync();
            var task3 = job.WaitForCompletionAsync();

            var results = await Task.WhenAll(task1, task2, task3);

            Assert.All(results, result => Assert.Equal(JobStatus.Completed, result.Status));
            Assert.Equal(results[0].Id, results[1].Id);
            Assert.Equal(results[1].Id, results[2].Id);
        }
    }
}
