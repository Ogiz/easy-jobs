using BackgroundJobs;

namespace WebApiExample.Tests;

public class JobResultDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public JobStatus Status { get; init; }
    public DateTime? StartTime { get; init; }
    public DateTime? EndTime { get; init; }
    public string? ErrorMessage { get; init; }
    public List<JobResultDto> ChildJobs { get; init; } = new();
    public string? ElapsedTime { get; init; }
    public bool IsRunning { get; init; }
}
