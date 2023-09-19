## **Quartz简单封装,开箱即用,使用方便**


**Nuget包安装：`dotnet add package Lycoris.Quartz.Extensions`**

**基于`Quartz`做了一层简单封装，支持Scoped生命周期直接在构造函数注入,简化了使用成本。**

### **一、注册Quartz调度中心**
```csharp
builder.Services.AddQuartzSchedulerCenter();
```

### **二、创建调度任务**

**创建调度任务的两种方式**


**基类会根据设置的任务运行截至事件自动停止任务，并且基类中也做了异常捕捉，除了你业务中必要的业务捕捉要，其他未知异常基类都能帮你及时记录**

**1. 继承扩展的基类**

#### 基类中包含以下两个可读属性
- **`JobTraceId`：当前执行的任务TraceId**
- **`JobName`：当前执行的任务名称**

```csharp
[QuartzJob("测试任务", Trigger = QuartzTriggerEnum.SIMPLE, IntervalSecond = 5)]
public class TestJob : BaseQuartzJob
{
    /// <summary>
    /// 
    /// </summary>
    /// <param name="context"></param>
    /// <returns></returns>
    protected override Task DoWork(IJobExecutionContext context)
    {
        Console.WriteLine($"TestJob => {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        return Task.CompletedTask;
    }
}
```

**创建好调度任务后，还需要设置任务的配置，设置很简单，在任务类上加上特性`[QuartzJob("测试任务", Trigger = QuartzTriggerEnum.SIMPLE, IntervalSecond = 5)]`**

**`QuartzJobAttribute`使用指南:**
- **构造函数入参为定时任务名称**
- **`Trigger`为触发器类型,分为普通定时器`SIMPLE`和Cron定时器`CRON`**
- **`IntervalSecond`：定时秒数，该配置仅对普通定时器有效**
- **`Cron`：如果是Corn定时器，需要配置Cron表达式**
- **`RunTimes`：执行次数，默认是无线循环**

---

- **2. 继承来自`Quartz`的`IJob`接口**
```csharp
public class TestJob : IJob
{
    public Task Execute(IJobExecutionContext context)
    {
        return Task.CompletedTask;
    }
}
```

### **三、注册调度任务**

**注册调度任务(以下`ServiceBuilder`类为演示自行编写的扩展类部分，不包含在当前扩展类库之内，仅做使用示例使用)**
```csharp
public static class ServiceBuilder
{
    // 推荐使用
    public static void AddQuartzJobBuilder(this IServiceCollection services)
    {
        services.AddQuartzJob<TestJob>()
                .AddQuartzJob<TestJob2>()
                .QuartzJobBuild();
    }

    // 推荐使用
    public static void AddQuartzJobBuilder(this IServiceCollection services)
    {
        services.AddQuartzJob(typeof(TestJob), typeof(TestJob2)).QuartzJobBuild();
    }

    // 请优先使用前两种注册方式
    public static void AddQuartzJobBuilder(this IServiceCollection services)
    {
        services.AddQuartzJobAssembly(MethodBase.GetCurrentMethod().ReflectedType.Assembly).QuartzJobBuild();
    }
}
```

**注册任务的另一种方式：注册调度中心时同时注册调度任务**
```csharp
 builder.Services.AddQuartzSchedulerCenter(buider =>
 {
     buider.AddJob<TestJob>();
     buider.AddJob<TestJob2>();
 });
```

### **四、调度任务也支持原生的两个特性**
- **`PersistJobDataAfterExecutionAttribute`: 这一次的结果作为值传给下一次**
```csharp
[PersistJobDataAfterExecution]
public class TestJob : BaseQuartzJob
{
    public TestJob(ILoggerFactory factory) : base(factory.CreateLogger<TestJob>())
    {

    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="context"></param>
    /// <returns></returns>
    protected override Task DoWork(IJobExecutionContext context)
    {
        // 取出的是上一次保存的值
        var val = context.GetJobDataMap("Key");

        _logger.Info($"TestJob => {DateTime.Now:yyyy-MM-dd HH:mm:ss}");

        // 保存这一次的值
        context.AddJobDataMap("Key", "这一次的传值");

        return Task.CompletedTask;
    }
}
```

- **`DisallowConcurrentExecutionAttribute`: 只有上一个任务完成才会执行下一次任务**
  ```csharp
  [DisallowConcurrentExecution]
  public class TestJob : BaseQuartzJob
  ```


### **五、数据库支持**
**扩展不做数据库支持，但是Quartz原生带有各种监听服务，需要使用到数据库做数据持久化的，请自行开发，可实现的接口如下：**
- **`ISchedulerListener`:调度器执行监听**
- **`ITriggerListener`:调度器执行监听**
- **`IJobListener`:调度任务执行监听**

**由于原生的接口有些有很多方法需要实现，如果想偷懒的小伙伴可以继承我处理好的基类：**
- **`SchedulerListener`**
- **`TriggerListener`**
- **`JobListener`**

**仅需重写你需要使用到的监听方法即可**

**注册监听**
```csharp
 builder.Services.AddQuartzSchedulerCenter(buider =>
 {
     buider.AddSchedulerListener<CustomeSchedulerListener>();
     buider.AddTriggerListener<CustomeTriggerListener>();
     buider.AddJobListener<CustomeJobListener>();
 });
```

### **六、自定义日志记录**
- `ILoggerFactory`：扩展的自定义日志工厂，在开发的时候，使用别人的扩展，但是由于自己的日志需要按格式，ES才能进行切割关键词分片等，很多不支持，所以自己开发的时候额外增加了这部分。
- 注意：使用自定义日志工厂时候，还需要自己实现`ILycorisLogger`来配合日志工厂实现自定义日志功能

```csharp
builder.Services.AddQuartzSchedulerCenter(builder =>
{
    // 替换扩展中默认的日志工厂
    builder.AddLycorisLoggerFactory<CustomeLoggerFactory>();
});
```

**PS:如果你使用了多个Lycoris系列扩展,那你可以在注册这些扩展之前使用`builder.Serovces.AddLycorisLoggerFactory<CustomeLoggerFactory>()`进行替换，就不需要在每个扩展中使用`AddLycorisLoggerFactory`进行逐一替换了**