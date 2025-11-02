using BackgroundJobs.Tests.TestJobs;

namespace BackgroundJobs.Tests
{
    public class OrchestratorJobTests
    {
        [Fact]
        public async Task SequentialOrchestrator_ExecutesJobsInOrder()
        {
            var jobs = new List<IJob>
            {
                new SuccessJob(),
                new CancellableJob(TimeSpan.FromMilliseconds(100)),
                new SuccessJob()
            };

            var orchestrator = new SequentialOrchestratorJob(jobs, continueOnFailure: false);
            orchestrator.Execute();

            var result = await orchestrator.WaitForCompletionAsync();

            Assert.Equal(JobStatus.Completed, result.Status);
            Assert.Equal(3, result.ChildJobs.Count);
            Assert.All(result.ChildJobs, childResult => Assert.Equal(JobStatus.Completed, childResult.Status));
        }

        [Fact]
        public async Task SequentialOrchestrator_FailFast_StopsOnFirstFailure()
        {
            var jobs = new List<IJob>
            {
                new SuccessJob(),
                new FailingJob("Intentional failure"),
                new SuccessJob()
            };

            var orchestrator = new SequentialOrchestratorJob(jobs, continueOnFailure: false);
            orchestrator.Execute();

            var result = await orchestrator.WaitForCompletionAsync(
                onFailure: failedResult => Task.FromResult(failedResult)
            );

            Assert.Equal(JobStatus.Failed, result.Status);
            Assert.Equal(3, result.ChildJobs.Count);
            Assert.Equal(JobStatus.Completed, result.ChildJobs[0].Status);
            Assert.Equal(JobStatus.Failed, result.ChildJobs[1].Status);
            Assert.Equal(JobStatus.Pending, result.ChildJobs[2].Status);
        }

        [Fact]
        public async Task SequentialOrchestrator_Resilient_ContinuesDespiteFailures()
        {
            var jobs = new List<IJob>
            {
                new SuccessJob(),
                new FailingJob("Intentional failure"),
                new SuccessJob()
            };

            var orchestrator = new SequentialOrchestratorJob(jobs, continueOnFailure: true);
            orchestrator.Execute();

            var result = await orchestrator.WaitForCompletionAsync();

            Assert.Equal(JobStatus.Completed, result.Status);
            Assert.Equal(3, result.ChildJobs.Count);
            Assert.Equal(JobStatus.Completed, result.ChildJobs[0].Status);
            Assert.Equal(JobStatus.Failed, result.ChildJobs[1].Status);
            Assert.Equal(JobStatus.Completed, result.ChildJobs[2].Status);
        }

        [Fact]
        public async Task SequentialOrchestrator_TracksChildJobResults()
        {
            var jobs = new List<IJob>
            {
                new SuccessJob(),
                new CancellableJob(TimeSpan.FromMilliseconds(50))
            };

            var orchestrator = new SequentialOrchestratorJob(jobs, continueOnFailure: false);
            orchestrator.Execute();

            await Task.Delay(25);
            var midResult = orchestrator.GetResult();
            Assert.Equal(2, midResult.ChildJobs.Count);
            Assert.Equal(JobStatus.Completed, midResult.ChildJobs[0].Status);
            Assert.Equal(JobStatus.Running, midResult.ChildJobs[1].Status);

            var finalResult = await orchestrator.WaitForCompletionAsync();
            Assert.Equal(JobStatus.Completed, finalResult.ChildJobs[1].Status);
        }

        [Fact]
        public async Task ParallelOrchestrator_ExecutesJobsSimultaneously()
        {
            var jobs = new List<IJob>
            {
                new CancellableJob(TimeSpan.FromMilliseconds(100)),
                new CancellableJob(TimeSpan.FromMilliseconds(100)),
                new CancellableJob(TimeSpan.FromMilliseconds(100))
            };

            var orchestrator = new ParallelOrchestratorJob(jobs, continueOnFailure: false);

            var startTime = DateTime.UtcNow;
            orchestrator.Execute();
            var result = await orchestrator.WaitForCompletionAsync();
            var elapsed = DateTime.UtcNow - startTime;

            Assert.Equal(JobStatus.Completed, result.Status);
            Assert.Equal(3, result.ChildJobs.Count);
            Assert.All(result.ChildJobs, childResult => Assert.Equal(JobStatus.Completed, childResult.Status));
            Assert.True(elapsed.TotalMilliseconds < 250, $"Parallel execution took {elapsed.TotalMilliseconds}ms, expected < 250ms");
        }

        [Fact]
        public async Task ParallelOrchestrator_FailFast_ThrowsOnAnyFailure()
        {
            var jobs = new List<IJob>
            {
                new SuccessJob(),
                new FailingJob("Intentional failure"),
                new CancellableJob(TimeSpan.FromMilliseconds(100))
            };

            var orchestrator = new ParallelOrchestratorJob(jobs, continueOnFailure: false);
            orchestrator.Execute();

            var result = await orchestrator.WaitForCompletionAsync(
                onFailure: failedResult => Task.FromResult(failedResult)
            );

            Assert.Equal(JobStatus.Failed, result.Status);
            Assert.Equal(3, result.ChildJobs.Count);
            Assert.Contains(result.ChildJobs, job => job.Status == JobStatus.Failed);
        }

        [Fact]
        public async Task ParallelOrchestrator_Resilient_CollectsAllResults()
        {
            var jobs = new List<IJob>
            {
                new SuccessJob(),
                new FailingJob("Intentional failure"),
                new CancellableJob(TimeSpan.FromMilliseconds(100))
            };

            var orchestrator = new ParallelOrchestratorJob(jobs, continueOnFailure: true);
            orchestrator.Execute();

            var result = await orchestrator.WaitForCompletionAsync();

            Assert.Equal(JobStatus.Completed, result.Status);
            Assert.Equal(3, result.ChildJobs.Count);
            Assert.Equal(JobStatus.Completed, result.ChildJobs[0].Status);
            Assert.Equal(JobStatus.Failed, result.ChildJobs[1].Status);
            Assert.Equal(JobStatus.Completed, result.ChildJobs[2].Status);
        }

        [Fact]
        public async Task ParallelOrchestrator_TracksChildJobResults()
        {
            var jobs = new List<IJob>
            {
                new CancellableJob(TimeSpan.FromMilliseconds(50)),
                new CancellableJob(TimeSpan.FromMilliseconds(100))
            };

            var orchestrator = new ParallelOrchestratorJob(jobs, continueOnFailure: false);
            orchestrator.Execute();

            await Task.Delay(75);
            var midResult = orchestrator.GetResult();
            Assert.Equal(2, midResult.ChildJobs.Count);

            var finalResult = await orchestrator.WaitForCompletionAsync();
            Assert.All(finalResult.ChildJobs, childResult => Assert.Equal(JobStatus.Completed, childResult.Status));
        }

        [Fact]
        public async Task OnErrorOrchestrator_CapturesChildJobExceptions()
        {
            var jobs = new List<IJob>
            {
                new SuccessJob(),
                new FailingJob("Test error message")
            };

            var orchestrator = new OnErrorOrchestratorJob(jobs);
            orchestrator.Execute();

            var result = await orchestrator.WaitForCompletionAsync(
                onFailure: failedResult => Task.FromResult(failedResult)
            );

            Assert.Equal(JobStatus.Failed, result.Status);
            Assert.Single(orchestrator.Errors);
            Assert.IsType<InvalidOperationException>(orchestrator.Errors[0]);
            Assert.Contains("Child job", orchestrator.Errors[0].Message);
        }

        [Fact]
        public async Task OnErrorOrchestrator_IncludesChildJobResults()
        {
            var jobs = new List<IJob>
            {
                new SuccessJob(),
                new FailingJob("Test error")
            };

            var orchestrator = new OnErrorOrchestratorJob(jobs);
            orchestrator.Execute();

            await orchestrator.WaitForCompletionAsync(
                onFailure: failedResult => Task.FromResult(failedResult)
            );

            var result = orchestrator.GetResult();
            Assert.Equal(2, result.ChildJobs.Count);
            Assert.Equal(JobStatus.Completed, result.ChildJobs[0].Status);
            Assert.Equal(JobStatus.Failed, result.ChildJobs[1].Status);
        }

        [Fact]
        public async Task OnFailureCallback_PreventsExceptionPropagation()
        {
            var jobs = new List<IJob>
            {
                new FailingJob("Should be caught")
            };

            var orchestrator = new SequentialOrchestratorJob(jobs, continueOnFailure: true);
            orchestrator.Execute();

            var result = await orchestrator.WaitForCompletionAsync();

            Assert.Equal(JobStatus.Completed, result.Status);
            Assert.Single(result.ChildJobs);
            Assert.Equal(JobStatus.Failed, result.ChildJobs[0].Status);
        }

        [Fact]
        public async Task OnCancellationCallback_HandlesChildCancellation()
        {
            var jobs = new List<IJob>
            {
                new CancellableJob(TimeSpan.FromSeconds(5))
            };

            var orchestrator = new SequentialOrchestratorJob(jobs, continueOnFailure: true);
            orchestrator.Execute();

            await Task.Delay(50);
            jobs[0].Cancel("Test cancellation");

            var result = await orchestrator.WaitForCompletionAsync();

            Assert.Equal(JobStatus.Completed, result.Status);
            Assert.Single(result.ChildJobs);
            Assert.Equal(JobStatus.Cancelled, result.ChildJobs[0].Status);
        }

        [Fact]
        public async Task OrchestratorDispose_DisposesAllChildJobs()
        {
            var trackableJob = new TrackableJob();
            var jobs = new List<IJob> { trackableJob, new SuccessJob() };

            var orchestrator = new SequentialOrchestratorJob(jobs, continueOnFailure: false);
            orchestrator.Execute();
            await orchestrator.WaitForCompletionAsync();

            orchestrator.Dispose();

            Assert.True(trackableJob.ExecuteInternalCalled);
        }
    }
}
