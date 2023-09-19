using Lycoris.Quartz.Extensions.Constant;
using Quartz;

namespace Lycoris.Quartz.Extensions.Job
{
    /// <summary>
    /// [PersistJobDataAfterExecution] 这一次的结果作为值传给下一次
    /// [DisallowConcurrentExecution] 只有上一个任务完成才会执行下一次任务
    /// </summary>
    public abstract class BaseQuartzJob : IJob
    {
        /// <summary>
        /// 当前任务运行JobTraceId
        /// </summary>
        protected string JobTraceId { get; private set; } = "";

        /// <summary>
        /// 当前任务名称
        /// </summary>
        protected string JobName { get; private set; } = "";

        /// <summary>
        /// 执行任务
        /// </summary>
        /// <param name="JobContext"></param>
        /// <returns></returns>
        public async Task Execute(IJobExecutionContext JobContext)
        {
            // 任务唯一标识Id
            JobTraceId = JobTraceIdInit(JobContext);
            JobContext.AddJobDataMap(QuartzConstant.JobTraceId, JobTraceId);

            // 任务名称
            JobName = JobContext.GetJobName();
            JobContext.JodDataClear();

            var endTime = JobContext.GetEndTime();
            if (endTime.HasValue && endTime <= DateTime.Now)
            {
                await JobContext.Scheduler.PauseJob(new JobKey(JobContext.JobDetail.Key.Name, JobContext.JobDetail.Key.Group));
                return;
            }

            try
            {
                await DoWork(JobContext);
            }
            catch (Exception ex)
            {
                if (ex is JobExecutionException)
                    throw;

                JobContext.AddJobException(ex);
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="JobContext"></param>
        /// <returns></returns>
        public virtual string JobTraceIdInit(IJobExecutionContext JobContext) => Guid.NewGuid().ToString("N");

        /// <summary>
        /// 执行任务
        /// </summary>
        /// <param name="context"></param>
        /// <returns></returns>
        protected abstract Task DoWork(IJobExecutionContext context);
    }
}
