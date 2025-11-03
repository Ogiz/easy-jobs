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
        var result = JobsRegistry.Instance.GetJobById(id);
        return Ok(result);
    }

    [HttpDelete("{id:guid}/cancel")]
    public IActionResult CancelJob(Guid id)
    {
        var result = JobsRegistry.Instance.CancelJob(id);
        return Ok(result);
    }
}
