using BackgroundJobs.Tests.TestJobs;

namespace BackgroundJobs.Tests
{
    public class JobsRegistryTests
    {
        [Fact]
        public async Task RegisterJob_AddsJobToRegistry()
        {
            var registry = JobsRegistry.Instance;
            var job = new SuccessJob();

            var registerResult = registry.RegisterJob(job);

            var jobById = registry.GetJobById(job.Id);
            var jobByName = registry.GetJobByName(job.Name);

            Assert.Equal(job.Id, jobById.Id);
            Assert.Equal(job.Name, jobByName.Name);
            Assert.Equal(jobById.Id, jobByName.Id);

            await job.WaitForCompletionAsync();
        }

        [Fact]
        public async Task RegisterDuplicateName_ReturnsExistingJobResult()
        {
            var registry = JobsRegistry.Instance;
            var firstJob = new TestJobWithCustomName("DuplicateTestJob-" + Guid.NewGuid());

            var firstResult = registry.RegisterJob(firstJob);

            var secondJob = new TestJobWithCustomName(firstJob.Name);

            var secondResult = registry.RegisterJob(secondJob);

            Assert.Equal(firstJob.Id, firstResult.Id);
            Assert.Equal(firstJob.Id, secondResult.Id);
            Assert.NotEqual(secondJob.Id, secondResult.Id);

            firstJob.Cancel();
            await Task.Delay(100);
        }

        private class TestJobWithCustomName : Job
        {
            public override string Name { get; }

            public TestJobWithCustomName(string name)
            {
                Name = name;
            }

            protected override async Task ExecuteInternal(CancellationToken ct)
            {
                await Task.Delay(TimeSpan.FromSeconds(2), ct);
            }
        }

        [Fact]
        public void RegisterPreExecutedJob_ReturnsCurrentStatus()
        {
            var registry = JobsRegistry.Instance;
            var job = new SuccessJob();

            job.Execute();

            Thread.Sleep(100);

            var result = registry.RegisterJob(job);

            Assert.True(result.Status == JobStatus.Completed || result.Status == JobStatus.Running);
        }

        [Fact]
        public async Task GetAllJobs_ReturnsAllRegisteredJobs()
        {
            var registry = JobsRegistry.Instance;
            var job1 = new SuccessJob();
            var job2 = new SuccessJob();

            registry.RegisterJob(job1);
            registry.RegisterJob(job2);

            var allJobs = registry.GetAllJobs().ToList();

            Assert.Contains(allJobs, j => j.Id == job1.Id);
            Assert.Contains(allJobs, j => j.Id == job2.Id);

            await job1.WaitForCompletionAsync();
            await job2.WaitForCompletionAsync();
        }

        [Fact]
        public void GetJobById_ReturnsDummyJobWithCompletedStatus()
        {
            var registry = JobsRegistry.Instance;
            var nonExistentId = Guid.NewGuid();

            var result = registry.GetJobById(nonExistentId);
            
            Assert.Equal(nonExistentId, result.Id);
            Assert.Equal(JobStatus.Completed, result.Status);
        }

        [Fact]
        public void GetJobByName_ThrowsWhenNotFound()
        {
            var registry = JobsRegistry.Instance;
            var nonExistentName = $"NonExistent-{Guid.NewGuid()}";

            var exception = Assert.Throws<KeyNotFoundException>(() => registry.GetJobByName(nonExistentName));

            Assert.Contains(nonExistentName, exception.Message);
        }

        [Fact]
        public async Task ConfigureJobRetentionTime_ThreadSafe()
        {
            var registry = JobsRegistry.Instance;
            var originalRetention = registry.JobRetentionTime;

            var tasks = Enumerable.Range(0, 10).Select(i => Task.Run(() =>
            {
                for (int j = 0; j < 100; j++)
                {
                    registry.JobRetentionTime = TimeSpan.FromMinutes(i + 1);
                    var readValue = registry.JobRetentionTime;
                    Assert.True(readValue.TotalMinutes >= 1 && readValue.TotalMinutes <= 10);
                }
            })).ToArray();

            await Task.WhenAll(tasks);

            registry.JobRetentionTime = originalRetention;
        }

        [Fact]
        public async Task ConfigureMaxJobs_ThreadSafe()
        {
            var registry = JobsRegistry.Instance;
            var originalMaxJobs = registry.MaxJobs;

            var tasks = Enumerable.Range(0, 10).Select(i => Task.Run(() =>
            {
                for (int j = 0; j < 100; j++)
                {
                    registry.MaxJobs = 100 + i;
                    var readValue = registry.MaxJobs;
                    Assert.True(readValue >= 100 && readValue <= 110);
                }
            })).ToArray();

            await Task.WhenAll(tasks);

            registry.MaxJobs = originalMaxJobs;
        }

        [Fact]
        public void RegisterJob_WithNullJob_ThrowsArgumentNullException()
        {
            var registry = JobsRegistry.Instance;

            Assert.Throws<ArgumentNullException>(() => registry.RegisterJob(null!));
        }
    }
}
