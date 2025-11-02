namespace BackgroundJobs.Tests.TestJobs
{
    public class FailingJob : Job
    {
        private readonly string _errorMessage;

        public override string Name { get; }

        public FailingJob(string errorMessage = "Job failed intentionally for testing")
        {
            Name = $"FailingJob-{Guid.NewGuid()}";
            _errorMessage = errorMessage;
        }

        protected override Task ExecuteInternal(CancellationToken ct)
        {
            throw new InvalidOperationException(_errorMessage);
        }
    }
}
