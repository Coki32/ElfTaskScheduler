# OposZadaci2020 - First task - Priority Task Scheduler
# Introduction
This is a primitive implementation of a task scheduler based on priorities. Priority is an integer value between 0 and 20, inclusive. Priority value 0 represents highest priority while value 20 indicates the lowest priority task. Each task __must__ receive one `ElfTaskData` parameter which is used as a means of integrating C#'s `Task`s with this implementation of a scheduler.

A public API is described below, but if you just want to hop into the examples you can [click here](#Examples).
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
public void ScheduleTask(ElfTask elfTask, int priority = DefaultPriority, int timeLimitMs = NoTimeLimit)
```
Parameters:
* _elfTask_ (`ElfTask`) - The task to be executed. See `ElfTask` definition for more information about tasks themselves.
* _priority_ (`int?`) - Priority of the task used when scheduling it relative to other tasks. Possible values are [0-20] inclusive. (Default: `10 = DefaultPriority`)
* _timeLimitMs_ (`int?`) - Time limit in milliseconds for the task to be queued, if the time expires before the task is complete it will be aborted.

Possible exceptions while scheduling a task are:
* `ArgumentOutOfRangeException` - Caused when priority isn't within defined limits of the scheduler.

When scheduling tasks the user must send one `ElfTask` 


## Examples
If used with C# standard library it will treat all tasks as tasks with priority value set to 10.