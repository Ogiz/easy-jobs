using BackgroundJobs;
using WebApiExample.Jobs;

namespace WebApiExample.Tests;

public class JobImplementationTests
{
    [Fact]
    public async Task SuccessJob_CompletesImmediately_WithSuccessStatus()
    {
        var job = new SuccessJob("TestSuccess");
        job.Execute();

        var result = await job.WaitForCompletionAsync();

        Assert.Equal(JobStatus.Completed, result.Status);
        Assert.NotNull(result.ElapsedTime);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public async Task SuccessJob_ReturnsCompletedResult_WithCorrectTiming()
    {
        var job = new SuccessJob("TestSuccess");
        job.Execute();

        await job.WaitForCompletionAsync();
        var result = job.GetResult();

        Assert.NotNull(result);
        Assert.Equal(JobStatus.Completed, result.Status);
        Assert.NotNull(result.StartTime);
        Assert.NotNull(result.EndTime);
        Assert.True(result.EndTime >= result.StartTime);
    }

    [Fact]
    public async Task FailingJob_TransitionsToFailed_WhenExecuted()
    {
        var job = new FailingJob("TestFailing", "Test error message");
        job.Execute();

        var result = await job.WaitForCompletionAsync(
            onFailure: Task.FromResult);

        Assert.Equal(JobStatus.Failed, result.Status);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public async Task FailingJob_CapturesException_InResult()
    {
        var customError = "This is a test exception";
        var job = new FailingJob("TestFailing", customError);
        job.Execute();

        var result = await job.WaitForCompletionAsync(
            onFailure: Task.FromResult);

        Assert.Equal(JobStatus.Failed, result.Status);
        Assert.Contains(customError, result.ErrorMessage);
    }

    [Fact]
    public async Task FailingJob_CustomErrorMessage_AppearsInResult()
    {
        var customMessage = "Custom failure scenario";
        var job = new FailingJob("TestFailing", customMessage);
        job.Execute();

        var result = await job.WaitForCompletionAsync(
            onFailure: Task.FromResult);

        Assert.Equal(JobStatus.Failed, result.Status);
        Assert.Contains(customMessage, result.ErrorMessage);
    }

    [Fact]
    public async Task CancellableJob_CompletesAfterSpecifiedDuration()
    {
        var duration = TimeSpan.FromSeconds(1);
        var job = new CancellableJob("TestCancellable", duration);
        var startTime = DateTime.UtcNow;
        job.Execute();

        var result = await job.WaitForCompletionAsync();
        var elapsed = DateTime.UtcNow - startTime;

        Assert.Equal(JobStatus.Completed, result.Status);
        Assert.True(elapsed >= duration, $"Expected at least {duration.TotalSeconds}s, actual {elapsed.TotalSeconds:F2}s");
        Assert.True(elapsed < duration.Add(TimeSpan.FromMilliseconds(500)),
            $"Job took too long: {elapsed.TotalSeconds:F2}s");
    }

    [Fact]
    public async Task CancellableJob_CanBeCancelled_MidExecution()
    {
        var duration = TimeSpan.FromSeconds(10);
        var job = new CancellableJob("TestCancellable", duration);
        var startTime = DateTime.UtcNow;
        job.Execute();

        await Task.Delay(100);
        job.Cancel();

        var result = await job.WaitForCompletionAsync(
            onFailure: null,
            onCancellation: Task.FromResult);
        var elapsed = DateTime.UtcNow - startTime;

        Assert.Equal(JobStatus.Cancelled, result.Status);
        Assert.True(elapsed < duration, $"Cancelled job should complete faster than {duration.TotalSeconds}s");
    }

    [Fact]
    public async Task CancellableJob_RespectsCancellationToken()
    {
        var job = new CancellableJob("TestCancellable", TimeSpan.FromSeconds(30));
        job.Execute();

        await Task.Delay(50);

        var resultBefore = job.GetResult();
        Assert.Equal(JobStatus.Running, resultBefore.Status);

        job.Cancel();
        await Task.Delay(200);

        var resultAfter = job.GetResult();
        Assert.Equal(JobStatus.Cancelled, resultAfter.Status);
    }

    [Fact]
    public async Task SequentialOrchestratorJob_ExecutesChildJobsInSequence()
    {
        var executionOrder = new List<DateTime>();
        var job1 = new TestTimingJob("Job1", TimeSpan.FromMilliseconds(100), executionOrder);
        var job2 = new TestTimingJob("Job2", TimeSpan.FromMilliseconds(100), executionOrder);
        var job3 = new TestTimingJob("Job3", TimeSpan.FromMilliseconds(100), executionOrder);

        var orchestrator = new SequentialOrchestratorJob(
            "TestSequential",
            [job1, job2, job3],
            continueOnFailure: false
        );

        orchestrator.Execute();
        await orchestrator.WaitForCompletionAsync();

        Assert.Equal(3, executionOrder.Count);
        Assert.True(executionOrder[0] < executionOrder[1], "Job1 should start before Job2");
        Assert.True(executionOrder[1] < executionOrder[2], "Job2 should start before Job3");
    }

    [Fact]
    public async Task SequentialOrchestratorJob_TracksChildJobResults()
    {
        var job1 = new CancellableJob("Child1", TimeSpan.FromMilliseconds(50));
        var job2 = new CancellableJob("Child2", TimeSpan.FromMilliseconds(50));
        var job3 = new CancellableJob("Child3", TimeSpan.FromMilliseconds(50));

        var orchestrator = new SequentialOrchestratorJob(
            "TestSequential",
            [job1, job2, job3],
            continueOnFailure: false
        );

        orchestrator.Execute();
        var result = await orchestrator.WaitForCompletionAsync();

        Assert.Equal(JobStatus.Completed, result.Status);
        Assert.NotNull(result.ChildJobs);
        Assert.Equal(3, result.ChildJobs.Count);
        Assert.All(result.ChildJobs, childResult =>
            Assert.Equal(JobStatus.Completed, childResult.Status)
        );
    }

    [Fact]
    public async Task SequentialOrchestratorJob_WithContinueOnFailureFalse_StopsOnFirstFailure()
    {
        var job1 = new SuccessJob("Child1");
        var job2 = new FailingJob("Child2", "Expected failure");
        var job3 = new SuccessJob("Child3");

        var orchestrator = new SequentialOrchestratorJob(
            "TestSequential",
            [job1, job2, job3],
            continueOnFailure: false
        );

        orchestrator.Execute();
        var result = await orchestrator.WaitForCompletionAsync(
            onFailure: Task.FromResult);

        Assert.Equal(JobStatus.Failed, result.Status);
        Assert.NotNull(result.ChildJobs);
        Assert.True(result.ChildJobs.Count >= 2);
        Assert.Equal(JobStatus.Completed, result.ChildJobs[0].Status);
        Assert.Equal(JobStatus.Failed, result.ChildJobs[1].Status);
    }

    [Fact]
    public async Task SequentialOrchestratorJob_WithContinueOnFailureTrue_ContinuesDespiteFailure()
    {
        var job1 = new SuccessJob("Child1");
        var job2 = new FailingJob("Child2", "Expected failure");
        var job3 = new SuccessJob("Child3");

        var orchestrator = new SequentialOrchestratorJob(
            "TestSequential",
            [job1, job2, job3],
            continueOnFailure: true
        );

        orchestrator.Execute();
        var result = await orchestrator.WaitForCompletionAsync();

        Assert.Equal(JobStatus.Completed, result.Status);
        Assert.NotNull(result.ChildJobs);
        Assert.Equal(3, result.ChildJobs.Count);
        Assert.Equal(JobStatus.Completed, result.ChildJobs[0].Status);
        Assert.Equal(JobStatus.Failed, result.ChildJobs[1].Status);
        Assert.Equal(JobStatus.Completed, result.ChildJobs[2].Status);
    }

    [Fact]
    public async Task SequentialOrchestratorJob_DisposesChildJobs_AfterCompletion()
    {
        var job1 = new DisposableTestJob("Child1");
        var job2 = new DisposableTestJob("Child2");

        var orchestrator = new SequentialOrchestratorJob(
            "TestSequential",
            [job1, job2],
            continueOnFailure: false
        );

        orchestrator.Execute();
        await orchestrator.WaitForCompletionAsync();

        orchestrator.Dispose();

        Assert.True(job1.IsDisposed);
        Assert.True(job2.IsDisposed);
    }

    [Fact]
    public async Task ParallelOrchestratorJob_ExecutesChildJobsConcurrently()
    {
        var job1 = new CancellableJob("Job1", TimeSpan.FromSeconds(1));
        var job2 = new CancellableJob("Job2", TimeSpan.FromSeconds(1));
        var job3 = new CancellableJob("Job3", TimeSpan.FromSeconds(1));

        var orchestrator = new ParallelOrchestratorJob(
            "TestParallel",
            [job1, job2, job3],
            continueOnFailure: false
        );

        var startTime = DateTime.UtcNow;
        orchestrator.Execute();
        await orchestrator.WaitForCompletionAsync();
        var elapsed = DateTime.UtcNow - startTime;

        Assert.Equal(JobStatus.Completed, orchestrator.GetResult().Status);
        Assert.True(elapsed.TotalSeconds < 2,
            $"Parallel execution should complete in ~1s, took {elapsed.TotalSeconds:F2}s");
    }

    [Fact]
    public async Task ParallelOrchestratorJob_ThreadSafeChildResultTracking()
    {
        var job1 = new SuccessJob("Child1");
        var job2 = new SuccessJob("Child2");
        var job3 = new SuccessJob("Child3");

        var orchestrator = new ParallelOrchestratorJob(
            "TestParallel",
            [job1, job2, job3],
            continueOnFailure: false
        );

        orchestrator.Execute();
        var result = await orchestrator.WaitForCompletionAsync();

        Assert.Equal(JobStatus.Completed, result.Status);
        Assert.NotNull(result.ChildJobs);
        Assert.Equal(3, result.ChildJobs.Count);
        Assert.All(result.ChildJobs, childResult =>
            Assert.Equal(JobStatus.Completed, childResult.Status)
        );
    }

    [Fact]
    public async Task ParallelOrchestratorJob_WithContinueOnFailureFalse_FailsIfAnyChildFails()
    {
        var job1 = new SuccessJob("Child1");
        var job2 = new FailingJob("Child2", "Expected failure");
        var job3 = new CancellableJob("Child3", TimeSpan.FromSeconds(1));

        var orchestrator = new ParallelOrchestratorJob(
            "TestParallel",
            [job1, job2, job3],
            continueOnFailure: false
        );

        orchestrator.Execute();
        var result = await orchestrator.WaitForCompletionAsync(
            onFailure: Task.FromResult);

        Assert.Equal(JobStatus.Failed, result.Status);
        Assert.NotNull(result.ChildJobs);
    }

    [Fact]
    public async Task ParallelOrchestratorJob_WithContinueOnFailureTrue_CompletesDespiteChildFailures()
    {
        var job1 = new SuccessJob("Child1");
        var job2 = new FailingJob("Child2", "Expected failure");
        var job3 = new SuccessJob("Child3");

        var orchestrator = new ParallelOrchestratorJob(
            "TestParallel",
            [job1, job2, job3],
            continueOnFailure: true
        );

        orchestrator.Execute();
        var result = await orchestrator.WaitForCompletionAsync();

        Assert.Equal(JobStatus.Completed, result.Status);
        Assert.NotNull(result.ChildJobs);
        Assert.Equal(3, result.ChildJobs.Count);
    }

    [Fact]
    public async Task ParallelOrchestratorJob_DisposesChildJobs_AfterCompletion()
    {
        var job1 = new DisposableTestJob("Child1");
        var job2 = new DisposableTestJob("Child2");

        var orchestrator = new ParallelOrchestratorJob(
            "TestParallel",
            [job1, job2],
            continueOnFailure: false
        );

        orchestrator.Execute();
        await orchestrator.WaitForCompletionAsync();

        orchestrator.Dispose();

        Assert.True(job1.IsDisposed);
        Assert.True(job2.IsDisposed);
    }

    [Fact]
    public async Task ParallelOrchestratorJob_PropagatesStatusChangedEvents()
    {
        var statusChanges = new List<JobStatus>();
        var job1 = new CancellableJob("Child1", TimeSpan.FromMilliseconds(100));

        var orchestrator = new ParallelOrchestratorJob(
            "TestParallel",
            [job1],
            continueOnFailure: false
        );

        orchestrator.StatusChanged += (result) => statusChanges.Add(result.Status);

        orchestrator.Execute();
        await Task.Delay(50);
        await orchestrator.WaitForCompletionAsync();

        Assert.Contains(JobStatus.Running, statusChanges);
        Assert.Contains(JobStatus.Completed, statusChanges);
    }

    private class TestTimingJob : Job
    {
        private readonly TimeSpan _duration;
        private readonly List<DateTime> _executionOrder;
        private readonly string _name;

        public override string Name => _name;

        public TestTimingJob(string name, TimeSpan duration, List<DateTime> executionOrder)
        {
            _name = name;
            _duration = duration;
            _executionOrder = executionOrder;
        }

        protected override async Task ExecuteInternal(CancellationToken cancellationToken)
        {
            _executionOrder.Add(DateTime.UtcNow);
            await Task.Delay(_duration, cancellationToken);
        }
    }

    private class DisposableTestJob : Job
    {
        private readonly string _name;

        public override string Name => _name;
        public bool IsDisposed { get; private set; }

        public DisposableTestJob(string name)
        {
            _name = name;
        }

        protected override Task ExecuteInternal(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                IsDisposed = true;
            }
            base.Dispose(disposing);
        }
    }
}
