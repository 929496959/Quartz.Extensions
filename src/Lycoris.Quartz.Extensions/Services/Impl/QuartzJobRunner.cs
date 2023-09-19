using Lycoris.Base.Logging;
using Microsoft.Extensions.DependencyInjection;
using Quartz;

namespace Lycoris.Quartz.Extensions.Services.Impl
{
    /// <summary>
    /// 
    /// </summary>
    public class QuartzJobRunner : IJob
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILycorisLogger? _logger;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="serviceProvider"></param>
        public QuartzJobRunner(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
            var factory = _serviceProvider.GetService<ILycorisLoggerFactory>();
            _logger = factory?.CreateLogger<QuartzJobRunner>();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="context"></param>
        /// <returns></returns>
        public async Task Execute(IJobExecutionContext context)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                {
                    var jobType = context.JobDetail.JobType;
                    var jobService = scope.ServiceProvider.GetRequiredService(jobType);
                    if (jobService == null)
                        return;

                    if (jobService is IJob job)
                        await job.Execute(context);
                }
            }
            catch (Exception ex)
            {
                if (_logger == null)
                    throw;

                _logger.Error($"execute task:{context.JobDetail.JobType.FullName} failed", ex);
            }
        }
    }
}
