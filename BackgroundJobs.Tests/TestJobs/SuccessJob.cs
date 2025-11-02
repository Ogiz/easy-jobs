namespace BackgroundJobs.Tests.TestJobs
{
    public class SuccessJob : Job
    {
        public override string Name { get; }

        public SuccessJob()
        {
            Name = $"SuccessJob-{Guid.NewGuid()}";
        }

        protected override Task ExecuteInternal(CancellationToken ct)
        {
            return Task.CompletedTask;
        }
    }
}
