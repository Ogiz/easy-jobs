using BackgroundJobs;
using Microsoft.AspNetCore.Mvc.Testing;

namespace WebApiExample.Tests;

public class IntegrationScenarioTests : TestBase
{
    public IntegrationScenarioTests(WebApplicationFactory<Program> factory) : base(factory)
    {
    }

    [Fact]
    public async Task FullJobLifecycle_CreateMonitorCompletion_WorksEndToEnd()
    {
        var createResponse = await PostAsync<JobResultDto>("/api/jobexecution/long-running?durationSeconds=1");
        var jobId = createResponse.Id;

        Assert.NotEqual(Guid.Empty, jobId);

        var runningResult = await GetAsync<JobResultDto>($"/api/jobs/{jobId}");
        Assert.Equal(JobStatus.Running, runningResult.Status);

        await Task.Delay(1500);

        var completedResult = await GetAsync<JobResultDto>($"/api/jobs/{jobId}");
        Assert.Equal(JobStatus.Completed, completedResult.Status);
        Assert.NotNull(completedResult.StartTime);
        Assert.NotNull(completedResult.EndTime);
        Assert.True(completedResult.EndTime >= completedResult.StartTime);
    }

    [Fact]
    public async Task FullJobLifecycle_CreateCancelVerify_WorksEndToEnd()
    {
        var createResponse = await PostAsync<JobResultDto>("/api/jobexecution/long-running?durationSeconds=30");
        var jobId = createResponse.Id;

        Assert.NotEqual(Guid.Empty, jobId);

        await Task.Delay(100);

        var runningResult = await GetAsync<JobResultDto>($"/api/jobs/{jobId}");
        Assert.Equal(JobStatus.Running, runningResult.Status);

        await DeleteAsync($"/api/jobs/{jobId}/cancel");

        await Task.Delay(200);

        var cancelledResult = await GetAsync<JobResultDto>($"/api/jobs/{jobId}");
        Assert.Equal(JobStatus.Cancelled, cancelledResult.Status);
        Assert.NotNull(cancelledResult.ErrorMessage);
    }

    [Fact]
    public async Task MultipleConcurrentJobs_ThroughAPI_ExecuteIndependently()
    {
        var job1Response = await PostAsync<JobResultDto>("/api/jobexecution/long-running?durationSeconds=1");
        var job2Response = await PostAsync<JobResultDto>("/api/jobexecution/long-running?durationSeconds=1");
        var job3Response = await PostAsync<JobResultDto>("/api/jobexecution/long-running?durationSeconds=1");

        var job1Id = job1Response.Id;
        var job2Id = job2Response.Id;
        var job3Id = job3Response.Id;

        Assert.NotEqual(job1Id, job2Id);
        Assert.NotEqual(job1Id, job3Id);
        Assert.NotEqual(job2Id, job3Id);

        var allJobs = await GetAsync<List<JobResultDto>>("/api/jobs");
        Assert.Contains(allJobs, j => j.Id == job1Id);
        Assert.Contains(allJobs, j => j.Id == job2Id);
        Assert.Contains(allJobs, j => j.Id == job3Id);

        await Task.Delay(1500);

        var job1Final = await GetAsync<JobResultDto>($"/api/jobs/{job1Id}");
        var job2Final = await GetAsync<JobResultDto>($"/api/jobs/{job2Id}");
        var job3Final = await GetAsync<JobResultDto>($"/api/jobs/{job3Id}");

        Assert.Equal(JobStatus.Completed, job1Final.Status);
        Assert.Equal(JobStatus.Completed, job2Final.Status);
        Assert.Equal(JobStatus.Completed, job3Final.Status);
    }

    [Fact]
    public async Task SequentialOrchestrator_EndToEndWorkflow_ExecutesSuccessfully()
    {
        var createResponse = await PostAsync<JobResultDto>("/api/jobexecution/sequential?continueOnFailure=false");
        var jobId = createResponse.Id;

        Assert.NotEqual(Guid.Empty, jobId);

        var initialResult = await GetAsync<JobResultDto>($"/api/jobs/{jobId}");
        Assert.True(initialResult.Status == JobStatus.Running || initialResult.Status == JobStatus.Completed);

        await Task.Delay(6000);

        var finalResult = await GetAsync<JobResultDto>($"/api/jobs/{jobId}");
        Assert.Equal(JobStatus.Completed, finalResult.Status);
        Assert.NotNull(finalResult.ChildJobs);
        Assert.Equal(3, finalResult.ChildJobs.Count);

        var job1 = finalResult.ChildJobs[0];
        var job2 = finalResult.ChildJobs[1];
        var job3 = finalResult.ChildJobs[2];

        Assert.Equal(JobStatus.Completed, job1.Status);
        Assert.Equal(JobStatus.Completed, job2.Status);
        Assert.Equal(JobStatus.Completed, job3.Status);

        Assert.True(job1.EndTime <= job2.StartTime,
            "Sequential execution: Job1 should end before Job2 starts");
        Assert.True(job2.EndTime <= job3.StartTime,
            "Sequential execution: Job2 should end before Job3 starts");
    }

    [Fact]
    public async Task ParallelOrchestrator_EndToEndWorkflow_ExecutesSuccessfully()
    {
        var createResponse = await PostAsync<JobResultDto>("/api/jobexecution/parallel?continueOnFailure=false");
        var jobId = createResponse.Id;

        Assert.NotEqual(Guid.Empty, jobId);

        var startTime = DateTime.UtcNow;

        await Task.Delay(6000);

        var finalResult = await GetAsync<JobResultDto>($"/api/jobs/{jobId}");
        var elapsed = DateTime.UtcNow - startTime;

        Assert.Equal(JobStatus.Completed, finalResult.Status);
        Assert.NotNull(finalResult.ChildJobs);
        Assert.Equal(3, finalResult.ChildJobs.Count);
        Assert.All(finalResult.ChildJobs, childResult =>
            Assert.Equal(JobStatus.Completed, childResult.Status)
        );

        Assert.True(elapsed.TotalSeconds < 10,
            $"Parallel execution should complete in ~5-6 seconds, took {elapsed.TotalSeconds:F2}s");

        var childStartTimes = finalResult.ChildJobs
            .Select(r => r.StartTime!.Value)
            .OrderBy(t => t)
            .ToList();

        var maxStartTimeSpread = (childStartTimes.Last() - childStartTimes.First()).TotalSeconds;
        Assert.True(maxStartTimeSpread < 1,
            $"Child jobs should start nearly simultaneously, spread was {maxStartTimeSpread:F2}s");
    }

    [Fact(Skip = "Test isolation issue: JobsRegistry singleton retains state from previous tests")]
    public async Task ErrorHandling_ThroughAPILayer_PropagatesCorrectly()
    {
        var customError = "Test error propagation";
        var createResponse = await PostAsync<JobResultDto>(
            $"/api/jobexecution/failing?errorMessage={Uri.EscapeDataString(customError)}"
        );
        var jobId = createResponse.Id;

        await Task.Delay(100);

        var result = await GetAsync<JobResultDto>($"/api/jobs/{jobId}");

        Assert.Equal(JobStatus.Failed, result.Status);
        Assert.NotNull(result.ErrorMessage);
        Assert.Contains(customError, result.ErrorMessage);
        Assert.Contains("InvalidOperationException", result.ErrorMessage);
        Assert.NotNull(result.StartTime);
        Assert.NotNull(result.EndTime);
    }

    [Fact]
    public async Task JobRegistryState_AfterOperations_RemainsConsistent()
    {
        var initialJobs = await GetAsync<List<JobResultDto>>("/api/jobs");
        var initialCount = initialJobs.Count;

        var job1Response = await PostAsync<JobResultDto>("/api/jobexecution/failing");
        var job2Response = await PostAsync<JobResultDto>("/api/jobexecution/long-running?durationSeconds=1");

        await Task.Delay(100);

        var afterCreationJobs = await GetAsync<List<JobResultDto>>("/api/jobs");
        Assert.True(afterCreationJobs.Count >= initialCount + 2);

        var job1Id = job1Response.Id;
        var job2Id = job2Response.Id;

        Assert.Contains(afterCreationJobs, j => j.Id == job1Id);
        Assert.Contains(afterCreationJobs, j => j.Id == job2Id);

        await Task.Delay(1500);

        var job1 = await GetAsync<JobResultDto>($"/api/jobs/{job1Id}");
        var job2 = await GetAsync<JobResultDto>($"/api/jobs/{job2Id}");

        Assert.Equal(JobStatus.Failed, job1.Status);
        Assert.Equal(JobStatus.Completed, job2.Status);
    }

    [Fact]
    public async Task CancelMultipleJobs_Concurrently_HandlesCorrectly()
    {
        var job1Response = await PostAsync<JobResultDto>("/api/jobexecution/long-running?durationSeconds=30");
        var job2Response = await PostAsync<JobResultDto>("/api/jobexecution/long-running?durationSeconds=30");
        var job3Response = await PostAsync<JobResultDto>("/api/jobexecution/long-running?durationSeconds=30");

        var job1Id = job1Response.Id;
        var job2Id = job2Response.Id;
        var job3Id = job3Response.Id;

        await Task.Delay(100);

        var cancelTask1 = DeleteAsync($"/api/jobs/{job1Id}/cancel");
        var cancelTask2 = DeleteAsync($"/api/jobs/{job2Id}/cancel");
        var cancelTask3 = DeleteAsync($"/api/jobs/{job3Id}/cancel");

        await Task.WhenAll(cancelTask1, cancelTask2, cancelTask3);

        await Task.Delay(200);

        var job1Result = await GetAsync<JobResultDto>($"/api/jobs/{job1Id}");
        var job2Result = await GetAsync<JobResultDto>($"/api/jobs/{job2Id}");
        var job3Result = await GetAsync<JobResultDto>($"/api/jobs/{job3Id}");

        Assert.Equal(JobStatus.Cancelled, job1Result.Status);
        Assert.Equal(JobStatus.Cancelled, job2Result.Status);
        Assert.Equal(JobStatus.Cancelled, job3Result.Status);
    }

    [Fact]
    public async Task SequentialOrchestrator_WithContinueOnFailure_HandlesFailureCorrectly()
    {
        var createResponse = await PostAsync<JobResultDto>("/api/jobexecution/sequential?continueOnFailure=true");
        var jobId = createResponse.Id;

        await Task.Delay(6000);

        var result = await GetAsync<JobResultDto>($"/api/jobs/{jobId}");

        Assert.Equal(JobStatus.Completed, result.Status);
        Assert.NotNull(result.ChildJobs);
        Assert.Equal(3, result.ChildJobs.Count);
    }

    [Fact]
    public async Task ParallelOrchestrator_WithContinueOnFailure_HandlesFailureCorrectly()
    {
        var createResponse = await PostAsync<JobResultDto>("/api/jobexecution/parallel?continueOnFailure=true");
        var jobId = createResponse.Id;

        await Task.Delay(6000);

        var result = await GetAsync<JobResultDto>($"/api/jobs/{jobId}");

        Assert.Equal(JobStatus.Completed, result.Status);
        Assert.NotNull(result.ChildJobs);
        Assert.Equal(3, result.ChildJobs.Count);
    }

    [Fact]
    public async Task JobStatusTransitions_ThroughAPI_AreConsistent()
    {
        var createResponse = await PostAsync<JobResultDto>("/api/jobexecution/long-running?durationSeconds=2");
        var jobId = createResponse.Id;

        var result1 = await GetAsync<JobResultDto>($"/api/jobs/{jobId}");
        Assert.Equal(JobStatus.Running, result1.Status);
        Assert.NotNull(result1.StartTime);
        Assert.Null(result1.EndTime);

        await Task.Delay(2500);

        var result2 = await GetAsync<JobResultDto>($"/api/jobs/{jobId}");
        Assert.Equal(JobStatus.Completed, result2.Status);
        Assert.NotNull(result2.StartTime);
        Assert.NotNull(result2.EndTime);
        Assert.Equal(result1.StartTime, result2.StartTime);
    }

    [Fact]
    public async Task FailingJob_ImmediateFailure_CapturedCorrectly()
    {
        var createResponse = await PostAsync<JobResultDto>("/api/jobexecution/failing");
        var jobId = createResponse.Id;

        await Task.Delay(50);

        var result = await GetAsync<JobResultDto>($"/api/jobs/{jobId}");

        Assert.Equal(JobStatus.Failed, result.Status);
        Assert.NotNull(result.ErrorMessage);
        Assert.NotNull(result.StartTime);
        Assert.NotNull(result.EndTime);
        Assert.NotNull(result.ElapsedTime);
    }
}
