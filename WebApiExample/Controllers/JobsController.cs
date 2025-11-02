using BackgroundJobs;
using Microsoft.AspNetCore.Mvc;

namespace WebApiExample.Controllers;

[ApiController]
[Route("api/[controller]")]
public class JobsController : ControllerBase
{
    [HttpGet]
    public IActionResult GetAllJobs()
    {
        var results = JobsRegistry.Instance.GetAllJobs();
        return Ok(results);
    }

    [HttpGet("{id:guid}")]
    public IActionResult GetJobById(Guid id)
    {
        var job = JobsRegistry.Instance.GetJobById(id);

        if (job == null)
        {
            return NotFound(new { message = $"Job with ID {id} not found" });
        }

        return Ok(job.GetResult());
    }

    [HttpDelete("{id:guid}/cancel")]
    public IActionResult CancelJob(Guid id)
    {
        var job = JobsRegistry.Instance.GetJobById(id);

        if (job == null)
        {
            return NotFound(new { message = $"Job with ID {id} not found" });
        }

        job.Cancel();

        return Ok(new { message = $"Cancellation requested for job {id}", result = job.GetResult() });
    }
}
