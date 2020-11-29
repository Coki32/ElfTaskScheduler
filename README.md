# OposZadaci2020 - First task - Priority Task Scheduler
# Introduction
This is a primitive implementation of a task scheduler based on priorities. Priority is an integer value between 0 and 20, inclusive. Priority value 0 represents highest priority while value 20 indicates the lowest priority task. Each task __must__ receive one `ElfTaskData` parameter which is used as a means of integrating C#'s `Task`s with this implementation of a scheduler. Users __must__ use methods of the `ElfTaskData` class to ensure cooperation and proper functioning of the scheduler. If used with C# standard library it will treat all tasks as tasks with priority value set to 10.

A public API is described below, but if you just want to hop into the examples you can [click here](#Examples).
# How to use ElfTaskScheduler
## Obtaining a scheduler instance
To create an instance of an `ElfTaskScheduler` scheduler there are a few parameters that a user can supply to the constructor:
```C#
ElfTaskScheduler(int maxThreadCount, bool isPreemptive = true, bool isRealtime = true, int commonTimeLimit = NoTimeLimit)
```
Parameters:
* _maxThreadCount_  (`int`) - Maximum number of tasks that may run at any given point in time. Must be 1 or greater.
* _isPreemptive_ (`bool?`) - Specifies if the scheduler should replace lower priority tasks with higher priority ones and resume lower priority tasks once it runs out of higher priority tasks. (Default: `true`)
* _isRealtime_ (`bool?`) - Specifies if the scheduler will (try to) start new tasks as soon as they are queued or not. (Default: `true`)
* _commonTimeLimit_ (`int?`)  - Specifies timeout for the each of the tasks passed. If the task breaches the time limit it will be forcefully interrupted. Must be 0 or greater. (Default: `0 = NoTimeLimit`)

Possible exceptions while constructing are:
* `ArgumentException` - Caused by either _commonTimeLimit_ or _maxThreadCount_ being invalid.

## Scheduling a task
To schedule a task the user should use `ScheduleTask` method of the previously created object. Method doesn't return a value but it does receive a few parameters and most of them are optional.
```C#
void ScheduleTask(ElfTask elfTask, int priority = DefaultPriority, int timeLimitMs = NoTimeLimit)
```
Parameters:
* _elfTask_ (`ElfTask`) - The task to be executed. See `ElfTask` definition for more information about tasks themselves.
* _priority_ (`int?`) - Priority of the task used when scheduling it relative to other tasks. Possible values are [0-20] inclusive. (Default: `10 = DefaultPriority`)
* _timeLimitMs_ (`int?`) - Time limit in milliseconds for the task to be queued, if the time expires before the task is complete it will be aborted.

Possible exceptions while scheduling a task are:
* `ArgumentOutOfRangeException` - Caused when priority isn't within defined limits of the scheduler.

When scheduling tasks the user must send one `ElfTask` (more about that in the next section). When a task is scheduled the user can call `RefreshTasks` method to assign tasks to appropriate threads, or it will be called implicitly if `isRealtime` was set to `true` when constructing the scheduler. 

# What are ElfTasks?
An ElfTask is any function that takes one parameter of type `ElfTaskData`. While executing your custom function you should periodically check `ElfTaskData.IsPaused` property and if it's set to `true` you should call `ElfTaskData.Pause()` method. You shouldn't perform loops with a large number of iterations without checking the `IsPaused` property, for example:
```C#
static void BadIdea(ElfTaskData etd){
    int slowSum = 0;
    const int n = 1000000;
    for(int i = 0; i < n; ++i)
        slowSum += i;
    Console.WriteLine($"Sum of all integers up to {n} is {slowSum}");
}
```
Is bad because, as fast as new CPUs are, this will ignore scheduler's hints that the task should pause and let others do some work. A more appropriate implementation would look like:
```C#
static void BadIdea(ElfTaskData etd){
    int slowSum = 0;
    const int n = 1000000;
    for(int i = 0; i < n; ++i){
        if(etd.IsPaused)
            etd.Pause();
        slowSum += i;
    }
    Console.WriteLine($"Sum of all integers up to {n} is {slowSum}");
}
```
Another property you should check is `IsCanceled` and if the flag is set to true you should cleanup after yourself and stop the task as soon as possible. Unlike `IsPaused`, `IsCanceled` means that your task will not be resumed, while `IsPaused` means that a task is temporarily suspended and that it will resume execution once there's nothing more urgent.
## Resources shared between tasks
Another feature you gain from using `ElfTaskData` class is the ability to access resources shared between tasks passed to the scheduler. Resources can be variables of any type so it's on the user to manage types, conversions and handle errors arising from applying invalid operators to the dynamic type. You can access resources only through the `ElfTaskData.TakeResource` method with the following parameters:
* _name_ (`string`) - Resource name, all resources are named and there should be no limits on what a valid name is and what isn't.
* _consumer_ (`ResourceConsumer`) - A `void` function which takes in one `ref` parameter of type `dynamic` which represents the shared resource. If you store the reference outside of this function you will, most likely, cause issues if you write anything to it.

When taking a resource for the very first time it's default value is a blank `object` instance.

If you call `ElfTaskData.Paused()` while you have the resource you will block other threads from taking that resource. This can cause a deadlock in some situations.
# Examples
## The most basic use
```C#
//Nonpreemptive, realtime scheduler
ElfTaskScheduler ets = new ElfTaskScheduler(2, false, true);
ets.ScheduleTask((taskData)=>{
    Console.WriteLine("I'm scheduled first!");
});//When priority is omitted DefaultPriority (10) is used
ets.ScheduleTask((taskData)=>{
    Console.WriteLine("I'm scheduled second!");
}, 5);
ets.ScheduleTask((taskData)=>{
    Console.WriteLine("I'm scheduled third!");
},15);
```
Due to it's realtime nature it's possible that order of these Write statements will be first-second-third because first task will start as soon as it's scheduler (there's no other tasks), second task also starts immediately as there's one more free thread and the third task, even though it's the lowest priority, will likely start right away as first and second task have probably finished by now. If we used a nonrealtime scheduler and called the `RefreshTasks` method manually after scheduling the third task we'd expect the output to be, because of task priorities, second - first - third
## Looping for a very long time
```C#
//preemptive, realtime scheduler
ElfTaskScheduler ets = new ElfTaskScheduler(2, true, true);
void RepetitiveSpam(ElfTaskData etd, string name, int n)
{
    Console.WriteLine($"Pokrecem {name}");
    for (int i = 0; i < n; ++i)
    {
        if (etd.IsPaused)
            etd.Pause();//should we wait
        if (etd.IsCanceled)
            break;//or should we end
        Thread.Sleep(100);//some delay to simulate work
        Console.WriteLine($"{name} does {i}. iteration");
    }
    Console.WriteLine($"{name} finished");//
}
ets.ScheduleTask((etd) => RepetitiveSpam(etd, "First", 10), 18);
ets.ScheduleTask((etd) => RepetitiveSpam(etd, "Second", 10), 15);
Thread.Sleep(300);//simulate a delay between spawning 
Console.WriteLine("Starting third, it should preempt First");
ets.ScheduleTask((etd) => RepetitiveSpam(etd, "Third", 5), 5);
```
In this case tasks spamming "First" and "Second" would run concurrently at first, but when the 300ms delay ends third task would replace the one with lower priority (First in this case). First will continue running once either Third or Second finish. It's very important to use provided `IsPaused` and `IsCanceled` fields to ensure fair use of the threads and allow scheduler to interact with user defined tasks.

## Using shared resources
To obtain a resource you must use `ElfTaskData.TakeResource` method as illustrated below.
```C#
const string resourceName = "n";
void TakeAndIncrement(ElfTaskData etd, string name) => etd.TakeResource(name, (ref dynamic n) => n = (n is int) ? n + 1 : 1);
ElfTaskScheduler ets = new ElfTaskScheduler(3, true, true);
ets.ScheduleTask((etd) =>
{
    for (int i = 0; i < 100; ++i)
        TakeAndIncrement(etd, resourceName);
});
ets.ScheduleTask((etd) =>
{
    for (int i = 0; i < 100; ++i)
        TakeAndIncrement(etd, resourceName);
});
Thread.Sleep(200);//let first 2 tasks finish their work before printing total result due to lack of synchronization
ets.ScheduleTask((etd) =>
{
    etd.TakeResource(resourceName, (ref dynamic n) => Console.WriteLine($"After they're both done working n is now {n}"));
});
```
Biggest innovation compared to previous examples is that now we have access to the shared global (global meaning all tasks can access it) resources using the `TakeResource` method of `ElfTaskData` class. Resource is locked when a task starts executing it's ResourceConsumer and unlocked once it ends.