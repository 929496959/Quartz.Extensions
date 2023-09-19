using Lycoris.Base.Logging;
using Lycoris.Quartz.Extensions.Job;
using Quartz;

namespace QuartzSample
{
    [QuartzJob("测试任务", Trigger = QuartzTriggerEnum.SIMPLE, IntervalSecond = 5)]
    public class TestJob : BaseQuartzJob
    {
        private readonly Lycoris.Base.Logging.ILycorisLogger logger;

        public TestJob(ILycorisLoggerFactory factory)
        {
            logger = factory.CreateLogger<TestJob>();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="context"></param>
        /// <returns></returns>
        protected override Task DoWork(IJobExecutionContext context)
        {
            Console.WriteLine("123");
            logger.Warn("123");
            return Task.CompletedTask;
        }
    }
}
