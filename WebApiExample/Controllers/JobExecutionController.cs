using BackgroundJobs;
using Microsoft.AspNetCore.Mvc;
using WebApiExample.Jobs;

namespace WebApiExample.Controllers;

[ApiController]
[Route("api/[controller]")]
public class JobExecutionController : ControllerBase
{
    [HttpPost("long-running")]
    public IActionResult StartLongRunningJob([FromQuery] int durationSeconds = 30)
    {
        var job = new CancellableJob($"Long-Running-{Guid.NewGuid():N}", TimeSpan.FromSeconds(durationSeconds));
        var result = JobsRegistry.Instance.RegisterJob(job);

        return Ok(new
        {
            jobId = job.Id,
            jobName = job.Name,
            message = $"Started long-running job for {durationSeconds} seconds",
            result
        });
    }

    [HttpPost("sequential")]
    public IActionResult StartSequentialOrchestrator([FromQuery] bool continueOnFailure = false)
    {
        var jobs = new List<BackgroundJobs.IJob>
        {
            new SuccessJob($"Sequential-Success-1-{Guid.NewGuid():N}"),
            new CancellableJob($"Sequential-Delay-{Guid.NewGuid():N}", TimeSpan.FromSeconds(5)),
            new SuccessJob($"Sequential-Success-2-{Guid.NewGuid():N}")
        };

        var orchestrator = new SequentialOrchestratorJob($"Sequential-Orchestrator-{Guid.NewGuid():N}", jobs, continueOnFailure);
        var result = JobsRegistry.Instance.RegisterJob(orchestrator);

        return Ok(new
        {
            jobId = orchestrator.Id,
            jobName = orchestrator.Name,
            message = "Started sequential orchestrator with 3 jobs",
            continueOnFailure,
            result
        });
    }

    [HttpPost("parallel")]
    public IActionResult StartParallelOrchestrator([FromQuery] bool continueOnFailure = false)
    {
        var jobs = new List<BackgroundJobs.IJob>
        {
            new CancellableJob($"Parallel-Job-1-{Guid.NewGuid():N}", TimeSpan.FromSeconds(3)),
            new CancellableJob($"Parallel-Job-2-{Guid.NewGuid():N}", TimeSpan.FromSeconds(5)),
            new CancellableJob($"Parallel-Job-3-{Guid.NewGuid():N}", TimeSpan.FromSeconds(2))
        };

        var orchestrator = new ParallelOrchestratorJob($"Parallel-Orchestrator-{Guid.NewGuid():N}", jobs, continueOnFailure);
        var result = JobsRegistry.Instance.RegisterJob(orchestrator);

        return Ok(new
        {
            jobId = orchestrator.Id,
            jobName = orchestrator.Name,
            message = "Started parallel orchestrator with 3 jobs (2s, 3s, 5s durations)",
            continueOnFailure,
            result
        });
    }

    [HttpPost("failing")]
    public IActionResult StartFailingJob([FromQuery] string errorMessage = "Intentional failure for demonstration")
    {
        var job = new FailingJob($"Failing-Job-{Guid.NewGuid():N}", errorMessage);
        var result = JobsRegistry.Instance.RegisterJob(job);

        return Ok(new
        {
            jobId = job.Id,
            jobName = job.Name,
            message = "Started failing job (will fail immediately)",
            result
        });
    }
}
