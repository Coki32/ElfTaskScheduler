# OposZadaci2020 - First task - Priority Task Scheduler
# Introduction
This is a primitive implementation of a task scheduler based on priorities. Priority is an integer value between 0 and 20, inclusive. Priority value 0 represents highest priority while value 20 indicates the lowest priority task. Each task __must__ receive one `ElfTaskData` parameter which is used as a means of integrating C#'s `Task`s with this implementation of a scheduler. Users __must__ use methods of the `ElfTaskData` class to ensure cooperation and proper functioning of the scheduler. If used with C# standard library it will treat all tasks as tasks with priority value set to 10.

A public API is described below, but if you just want to hop into the examples you can [click here](#Examples).
# How to use ElfTaskScheduler
## Obtaining a scheduler instance
To create an instance of an `ElfTaskScheduler` scheduler there are a few parameters that a user can supply to the constructor:
```C#
ElfTaskScheduler(int maxThreadCount, bool isPreemptive = true, bool isRealtime = true, int globalTimeLimit = NoTimeLimit)
```
Parameters:
* _maxThreadCount_  (`int`) - Maximum number of tasks that may run at any given point in time. Must be 1 or greater.
* _isPreemptive_ (`bool?`) - Specifies if the scheduler should replace lower priority tasks with higher priority ones and resume lower priority tasks once it runs out of higher priority tasks. (Default: `true`)
* _isRealtime_ (`bool?`) - Specifies if the scheduler will (try to) start new tasks as soon as they are queued or not. (Default: `true`)
* _globalTimeLimit_ (`int?`)  - Specifies timeout for the each of the tasks passed. If the task breaches the time limit it will be forcefully interrupted. Must be 0 or greater. (Default: `0 = NoTimeLimit`)

Possible exceptions while constructing are:
* `ArgumentException` - Caused by either _globalTimeLimit_ or _maxThreadCount_ being invalid.

## Scheduling a task
To schedule a task the user should use `ScheduleTask` method of the previously created object. Method doesn't return a value but it does receive a few parameters and most of them are optional.
```C#
void ScheduleTask(ElfTask elfTask, int priority = DefaultPriority, int timeLimitMs = NoTimeLimit)
```
Parameters:
* _elfTask_ (`ElfTask`) - The task to be executed. See `ElfTask` definition for more information about tasks themselves.
* _priority_ (`int?`) - Priority of the task used when scheduling it relative to other tasks. Possible values are [0-20] inclusive. (Default: `10 = DefaultPriority`)
* _timeLimitMs_ (`int?`) - Time limit in milliseconds for the task to be queued, if the time expires before the task is complete it will be aborted. Negative values are not allowed, value 0 means that there's no time limit and greater than 0 is time limit in milliseconds. (Default: `0 = NoTimeLimit`)

Possible exceptions while scheduling a task are:
* `ArgumentOutOfRangeException` - Caused when priority isn't within defined limits of the scheduler.

When scheduling tasks the user must send one `ElfTask` (more about that in the next section). When a task is scheduled the user can call `RefreshTasks` method to assign tasks to appropriate threads, or it will be called implicitly if `isRealtime` was set to `true` when constructing the scheduler.

Very basic and very primitive support for deadlock checks has been implemented using the `DeadlockChecker` class which is constructed at the same time as the scheduler. Checker will periodically check which tasks are holding which resources and wanting others and if there's any paused tasks which hold some wanted resources. `DeadlockChecker` parameters are defines as public fields of the `ElfTaskScheduler` class and they are `DeadlockCheckIntervalMs` which represents the period of deadlock checks in milliseconds and `DeadlockStopAfter` which is used as a counter for how many consecutive periods were all tasks blocked. Deadlock is "resolved" by terminating the program.

## Cleaning up after yourself
The scheduler implements `IDisposable` and you may use it with a `using` block to make sure that `DeadlockChecker` thread is properly finished after the scheduler is no longer in use. `using` block was used in examples where that was possible.

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
using(ElfTaskScheduler ets = new ElfTaskScheduler(2, false, true))
{
    ets.ScheduleTask((taskData)=>{
        Console.WriteLine("I'm scheduled first!");
    });//When priority is omitted DefaultPriority (10) is used
    ets.ScheduleTask((taskData)=>{
        Console.WriteLine("I'm scheduled second!");
    }, 5);
    ets.ScheduleTask((taskData)=>{
        Console.WriteLine("I'm scheduled third!");
    },15);
}
```
Due to it's realtime nature it's possible that order of these Write statements will be first-second-third because first task will start as soon as it's scheduler (there's no other tasks), second task also starts immediately as there's one more free thread and the third task, even though it's the lowest priority, will likely start right away as first and second task have probably finished by now. If we used a nonrealtime scheduler and called the `RefreshTasks` method manually after scheduling the third task we'd expect the output to be, because of task priorities, second - first - third
## Looping for a very long time
```C#
const int numberOfTasks = 2;
using (ElfTaskScheduler ets = new ElfTaskScheduler(numberOfTasks, true, true))
{
    SemaphoreSlim semaphore = new SemaphoreSlim(0);
    void RepetitiveSpam(ElfTaskData etd, string name, int n)
    {
        Console.WriteLine($"Starting {name}");
        for (int i = 0; i < n; ++i)
        {
            if (etd.IsPaused)
                etd.Pause();
            if (etd.IsCanceled)
                break;
            Thread.Sleep(100);
            Console.WriteLine($"{name} does {i}. iteration");
        }
        Console.WriteLine($"{name} finished");
        semaphore.Release();
    }
    ets.ScheduleTask((etd) => RepetitiveSpam(etd, "First", 10), 18);
    ets.ScheduleTask((etd) => RepetitiveSpam(etd, "Second", 10), 15);
    Thread.Sleep(400);//simulate a delay between tasks
    Console.WriteLine("Starting third, it should preempt First");
    ets.ScheduleTask((etd) => RepetitiveSpam(etd, "Third", 5), 5);
    while (semaphore.CurrentCount != 3)//we expect 3 tasks to complete running, regardles of having only 2 threads in the scheduler
        Thread.Sleep(10);//check ever 10 ms
}
```
In this case tasks spamming "First" and "Second" would run concurrently at first, but when the 300ms delay ends third task would replace the one with lower priority (First in this case). First will continue running once either Third or Second finish. It's very important to use provided `IsPaused` and `IsCanceled` fields to ensure fair use of the threads and allow scheduler to interact with user defined tasks.

## Using shared resources
To obtain a resource you must use `ElfTaskData.TakeResource` method as illustrated below.
```C#
const string resourceName = "n";
void TakeAndIncrement(ElfTaskData etd, string name) => etd.TakeResource(name, (ref dynamic n) => n = (n is int) ? n + 1 : 1);
using (ElfTaskScheduler ets = new ElfTaskScheduler(3, true, true))//3 as a demonstration that not all have to be running
{
    Barrier barrier = new Barrier(3);//3 cause we have 2 workers and 1 main thread
    ets.ScheduleTask((etd) =>
    {
        for (int i = 0; i < 100; ++i)
            TakeAndIncrement(etd, resourceName);
        barrier.SignalAndWait();
    });
    ets.ScheduleTask((etd) =>
    {
        for (int i = 0; i < 100; ++i)
            TakeAndIncrement(etd, resourceName);
        barrier.SignalAndWait();
    });
    barrier.SignalAndWait();//wait for the first 2 to finish
    barrier.RemoveParticipant();//2 participants remaining, this thread and worker to print theresult
    ets.ScheduleTask((etd) =>
    {
        etd.TakeResource(resourceName, (ref dynamic n) => Console.WriteLine($"After they're both done working n is now {n}"));
        barrier.SignalAndWait();
    });
    barrier.SignalAndWait();
}
```
Biggest innovation compared to previous examples is that now we have access to the shared global (global meaning all tasks can access it) resources using the `TakeResource` method of `ElfTaskData` class. Resource is locked when a task starts executing it's ResourceConsumer and unlocked once it ends.

## Using usage with TaskFactory
You can use the `ElfTaskScheduler` as a valid `TaskScheduler` instance in combination with a `TaskFactory`. When used in this way there is no option to pass in tasks with different priorities and all tasks are assigned default priority value of `10`. This approach gives you a scheduler that works as FCSF scheduler cause newer tasks with same priority are added to the back of the queue.
```C#
const int delay = 200;
const int numberOfTasks = 10;
SemaphoreSlim semaphore = new SemaphoreSlim(0);
TaskFactory tf = new TaskFactory(new ElfTaskScheduler(1, true, true));
void BitOfSpam(int howMuch, string ofWhat, string name, int delayMs = delay)
{
    for (int i = 0; i < howMuch; ++i)
    {
        Thread.Sleep(delayMs);
        Console.WriteLine($"[{name}]: {ofWhat}");
    }
    semaphore.Release();
}
Action wrapper(int i) => () => BitOfSpam(10, $"Spam text {i}", $"Task{i}");
for (int i = 0; i < numberOfTasks; ++i)
    tf.StartNew(wrapper(i));//wrap the local variable 'i'
while (semaphore.CurrentCount != numberOfTasks)//wait for all to release
    Thread.Sleep(10);
```
## Doing something useful
We can use the scheduler to parallelize some useful stuff like function integration, example is bellow and it's available as `Test5` of the demo project.
```C#
int numberOfTasks = Environment.ProcessorCount;
const double dx = 0.000000001;
const double A = -1.845, B = 2;
double segmentLength = (B - A) / numberOfTasks;
Barrier barrier = new Barrier(numberOfTasks + 1);//+1 for the current running thread
double sampleFunc(double x) => x * x * x - x * x - 2 * x + 6;

double integrateOnePart(double from, double to, double step, Func<double, double> f)
{
    double subRes = 0.0;
    for (double x = from; x < to; x += step)
        subRes += f(x) * dx;
    return subRes;
}

ElfTaskScheduler.ElfTask segmenter(int i)//Introducing a function to capture the 'i' which was used at the task creation, not the last one
{
    return (ElfTaskData td) =>
    {
        double from = A + i * segmentLength, to = A + (i + 1) * segmentLength;
        double myRes = integrateOnePart(from, to, dx, sampleFunc);
        Console.WriteLine($"Area under the segment [{from}, {to}) is {myRes}");
        td.TakeResource("p", (ref dynamic p) => p += myRes);
        barrier.SignalAndWait();
    };
}

using (ElfTaskScheduler ets = new ElfTaskScheduler(numberOfTasks, true, true))
{
    //make sure this executes first, not touching the barrier, we only let workers do that, this task should end without affecting the barrier
    ets.ScheduleTask((td) => td.TakeResource("p", (ref dynamic p) => p = 0.0), 0);
    //spawn the calculating tasks...
    for (int i = 0; i < numberOfTasks; ++i)
        ets.ScheduleTask(segmenter(i), i + 1);

    barrier.SignalAndWait();
    barrier.RemoveParticipants(numberOfTasks - 1);//leave only 2 participants, this one and one remaining worker

    ets.ScheduleTask((td) =>
    {
        td.TakeResource("p", (ref dynamic p) => Console.WriteLine($"Area under the curve in range from {A} to {B} is {p} "));
        barrier.SignalAndWait();
    });
    barrier.SignalAndWait();
}
```