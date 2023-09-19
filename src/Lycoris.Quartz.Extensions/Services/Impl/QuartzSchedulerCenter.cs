using Lycoris.Base.Extensions;
using Lycoris.Quartz.Extensions.Constant;
using Lycoris.Quartz.Extensions.Job;
using Lycoris.Quartz.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Quartz;
using Quartz.Spi;
using System.Reflection;

namespace Lycoris.Quartz.Extensions.Services.Impl
{
    /// <summary>
    /// 
    /// </summary>
    public class QuartzSchedulerCenter : IQuartzSchedulerCenter
    {
        private const string DefaultJobGroup = "unclassified";
        private readonly IServiceProvider _serviceProvider;
        private readonly Base.Logging.ILycorisLogger? logger;
        private readonly ILogger? _Systemlogger;

        /// <summary>
        /// 调度器
        /// </summary>
        private readonly IScheduler scheduler;
        /// <summary>
        /// 任务工厂
        /// </summary>
        private readonly IJobFactory? jobFactory;

        /// <summary>
        /// ctor
        /// </summary>
        /// <param name="serviceProvider"></param>
        /// <exception cref="ArgumentNullException"></exception>
        public QuartzSchedulerCenter(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;

            var schedulerFactory = serviceProvider.GetService<ISchedulerFactory>() ?? throw new ArgumentNullException(nameof(ISchedulerFactory), "unable to resolve 'ISchedulerFactory' service from constructor");

            scheduler = schedulerFactory.GetScheduler().GetAwaiter().GetResult();

            jobFactory = serviceProvider.GetService<IJobFactory>();

            var factory = serviceProvider.GetService<Base.Logging.ILycorisLoggerFactory>();
            if (factory != null)
                logger = factory.CreateLogger<QuartzSchedulerCenter>();
            else
                _Systemlogger = serviceProvider.GetService<ILogger<QuartzSchedulerCenter>>();
        }

        /// <summary>
        /// 开启调度器
        /// </summary>
        /// <returns></returns>
        public async Task<bool> StartScheduleAsync()
        {
            if (scheduler.InStandbyMode)
            {
                if (jobFactory == null)
                    throw new ArgumentNullException(nameof(IJobFactory), "unable to resolve 'IJobFactory' service from constructor");

                scheduler.JobFactory = jobFactory;

                var schedulerListener = _serviceProvider.GetService<ISchedulerListener>();
                if (schedulerListener != null)
                    scheduler.ListenerManager.AddSchedulerListener(schedulerListener);

                var jobListener = _serviceProvider.GetService<IJobListener>();
                if (jobListener != null)
                    scheduler.ListenerManager.AddJobListener(jobListener);

                var triggerListener = _serviceProvider.GetService<ITriggerListener>();
                if (triggerListener != null)
                    scheduler.ListenerManager.AddTriggerListener(triggerListener);

                await scheduler.Start();

                InfoLog("quartz schedulercenter start ...");
            }

            return scheduler.InStandbyMode;
        }

        /// <summary>
        /// 停止调度器
        /// </summary>
        public async Task<bool> StopScheduleAsync()
        {
            try
            {
                //判断调度是否已经关闭
                if (!scheduler.InStandbyMode)
                {
                    //等待任务运行完成
                    await scheduler.Standby(); //TODO  注意：Shutdown后Start会报错,所以这里使用暂停。
                    InfoLog("quartz schedulercenter stop ...");
                }

                return !scheduler.InStandbyMode;
            }
            catch (Exception ex)
            {
                ErrorLog("quartz schedulercenter stop warning", ex);
                return false;
            }
        }

        /// <summary>
        /// 添加工作任务
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public Task AddJobAsync<T>() where T : IJob
        {
            var settings = typeof(T).GetCustomAttribute<QuartzJobAttribute>() ?? throw new Exception($"can not find QuartzJobAttribute by current job {typeof(T).Name},unable to add job");

            var option = new QuartzSchedulerOption()
            {
                BeginTime = new DateTime(2000, 1, 1),
                TriggerType = settings.Trigger,
                Cron = settings.Cron,
                IntervalSecond = settings.IntervalSecond,
                RunTimes = settings.RunTimes,
                JobGroup = string.IsNullOrEmpty(settings.JobGroup) ? "unclassified" : settings.JobGroup,
                JobName = settings.JobName
            };

            return AddJobAsync<T>(option);
        }

        /// <summary>
        /// 添加工作任务
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="sche"></param>
        /// <returns></returns>
        public Task AddJobAsync<T>(QuartzSchedulerOption sche) where T : IJob => AddJobAsync(typeof(T), sche);

        /// <summary>
        /// 添加工作任务
        /// </summary>
        /// <param name="jobType"></param>
        /// <param name="sche"></param>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        public async Task AddJobAsync(Type jobType, QuartzSchedulerOption sche)
        {
            if (!jobType.IsInterfaceFrom<IJob>())
                throw new Exception("{typeof(T).Name} must implement 'IJob' or inherit the base class 'BaseQuartzJob'");

            if (string.IsNullOrEmpty(sche.JobGroup))
                sche.JobGroup = DefaultJobGroup;

            //检查任务是否已存在
            var jobKey = new JobKey(sche.JobKey, sche.JobGroup);
            if (await scheduler.CheckExists(jobKey))
                throw new Exception($"job name {sche.JobName} already exists");

            var jobBuilder = JobBuilder.Create(jobType);

            //定义这个工作,并将其绑定到我们的IJob实现类                
            var job = jobBuilder.UsingJobData(QuartzConstant.JobKey, sche.JobKey)
                                .UsingJobData(QuartzConstant.JobName, sche.JobName)
                                .WithDescription(sche.Remark)
                                .WithIdentity(sche.JobKey, sche.JobGroup)
                                .Build();

            var trigger = sche.TriggerType == QuartzTriggerEnum.CRON
                ? CreateCronTrigger(sche)
                : CreateSimpleTrigger(sche);

            //安排触发器开始作业
            await scheduler.ScheduleJob(job, trigger);
        }

        /// <summary>
        /// 启动任务
        /// </summary>
        /// <param name="jobKey"></param>
        /// <param name="jobGroup"></param>
        /// <returns></returns>
        public async Task StartJobAsync(string jobKey, string jobGroup = "")
        {
            if (string.IsNullOrEmpty(jobGroup))
                jobGroup = DefaultJobGroup;

            //检查任务是否存在
            var job = new JobKey(jobKey, jobGroup);
            if (!await scheduler.CheckExists(job))
                throw new Exception($"this job {jobKey} already exists");

            var jobDetail = await scheduler.GetJobDetail(job);
            if (jobDetail != null)
            {
                var endTime = jobDetail.JobDataMap.GetString(QuartzConstant.EndTime);
                if (!string.IsNullOrEmpty(endTime) && DateTime.Parse(endTime) <= DateTime.Now)
                    throw new Exception($"this job {jobKey} has expired");
            }

            var state = await scheduler.GetTriggerState(new TriggerKey(jobKey));
            if (state == TriggerState.Normal)
                return;

            //任务已经存在则暂停任务
            await scheduler.ResumeJob(job);
        }

        /// <summary>
        /// 暂停任务
        /// </summary>
        /// <param name="jobKey"></param>
        /// <param name="jobGroup"></param>
        /// <returns></returns>
        public async Task StopJobAsync(string jobKey, string jobGroup = "")
        {
            if (string.IsNullOrEmpty(jobGroup))
                jobGroup = DefaultJobGroup;

            //检查任务是否存在
            var job = new JobKey(jobKey, jobGroup);
            if (!await scheduler.CheckExists(job))
                return;

            var state = await scheduler.GetTriggerState(new TriggerKey(jobKey));
            if (state == TriggerState.Paused)
                return;

            await scheduler.PauseJob(job);
        }

        /// <summary>
        /// 移除任务
        /// </summary>
        /// <param name="jobKey"></param>
        /// <param name="jobGroup"></param>
        /// <returns></returns>
        public async Task RemoveobAsync(string jobKey, string jobGroup = "")
        {
            if (string.IsNullOrEmpty(jobGroup))
                jobGroup = DefaultJobGroup;

            //检查任务是否存在
            var job = new JobKey(jobKey, jobGroup);
            if (!await scheduler.CheckExists(job))
                return;

            var state = await scheduler.GetTriggerState(new TriggerKey(jobKey));
            if (state == TriggerState.Normal)
                await scheduler.PauseJob(job);

            await scheduler.DeleteJob(job);
        }

        /// <summary>
        /// 立即执行一次
        /// </summary>
        /// <param name="jobKey"></param>
        /// <param name="jobGroup"></param>
        /// <returns></returns>
        public async Task RunJobAsync(string jobKey, string jobGroup)
        {
            if (string.IsNullOrEmpty(jobGroup))
                jobGroup = DefaultJobGroup;

            //检查任务是否存在
            var job = new JobKey(jobKey, jobGroup);
            if (!await scheduler.CheckExists(job))
                throw new Exception($"this job {jobKey} does not exist");

            await scheduler.TriggerJob(job);
        }

        /// <summary>
        /// 手动启动待机列表任务
        /// </summary>
        /// <returns></returns>
        public async Task ManualRunHostedJobsAsync()
        {
            if (!QuartzSchedulderStore.HostedJobOptions.HasValue())
                return;

            foreach (var item in QuartzSchedulderStore.HostedJobOptions!)
            {
                await AddJobAsync(item.Key, item.Value);
            }

            QuartzSchedulderStore.HostedJobOptions = new Dictionary<Type, QuartzSchedulerOption>();
        }

        /// <summary>
        /// 创建类型Cron的触发器
        /// </summary>
        /// <param name="sche"></param>
        /// <returns></returns>
        private static ITrigger CreateCronTrigger(QuartzSchedulerOption sche)
        {
            var trigger = TriggerBuilder.Create();
            trigger = trigger.WithIdentity(sche.JobKey, sche.JobGroup);

            //开始时间
            trigger = trigger.StartAt(sche.BeginTime);

            //结束时间
            if (sche.EndTime.HasValue)
                trigger = trigger.EndAt(sche.EndTime);

            // 作业触发器
            trigger = trigger.WithCronSchedule(sche.Cron, cronScheduleBuilder => cronScheduleBuilder.WithMisfireHandlingInstructionFireAndProceed())
                             .ForJob(sche.JobKey, sche.JobGroup);

            return trigger.Build();
        }

        /// <summary>
        /// 创建类型Simple的触发器
        /// </summary>
        /// <param name="sche"></param>
        /// <returns></returns>
        private static ITrigger CreateSimpleTrigger(QuartzSchedulerOption sche)
        {
            var triggerBulider = TriggerBuilder.Create();

            triggerBulider = triggerBulider.WithIdentity(sche.JobKey, sche.JobGroup)
                                           .StartAt(sche.BeginTime);

            if (sche.EndTime.HasValue)
                triggerBulider = triggerBulider.EndAt(sche.EndTime);

            triggerBulider = triggerBulider.WithSimpleSchedule(x =>
            {
                if (sche.RunTimes > 0)
                {
                    x.WithIntervalInSeconds(sche.IntervalSecond)//执行时间间隔,单位秒
                     .WithRepeatCount(sche.RunTimes)//执行次数、默认从0开始
                     .WithMisfireHandlingInstructionFireNow();
                }
                else
                {
                    //执行时间间隔,单位秒
                    x.WithIntervalInSeconds(sche.IntervalSecond)
                     .RepeatForever()//无限循环
                     .WithMisfireHandlingInstructionFireNow();
                }
            });

            return triggerBulider.ForJob(sche.JobKey, sche.JobGroup).Build();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="message"></param>
        private void InfoLog(string message)
        {
            if (logger != null)
                logger.Info(message);
            else
                _Systemlogger?.LogInformation(message);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="message"></param>
        /// <param name="ex"></param>
        private void ErrorLog(string message, Exception ex)
        {
            if (logger != null)
                logger.Error(message, ex);
            else
                _Systemlogger?.LogError(ex, message);
        }
    }
}
