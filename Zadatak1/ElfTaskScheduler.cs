using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using System.Linq;
using System.Threading;
using System.Diagnostics;

namespace Zadatak1
{
    public class ElfTaskScheduler : TaskScheduler, IDisposable
    {
        //Mora biti elfTask
        public delegate void ElfTask(ElfTaskData taskData);

        //private delegate void CleanupDelegate();
        internal class PendingTaskInfo
        {
            public ElfTask Task;
            public int DurationLimit;
            public int Priority;
        }

        /// <summary>
        /// Task priority is an integer [0-20] where higher number indicates
        /// lower priority. Default priority is 10.
        /// </summary>
        public const int MaxPriority = 0;
        public const int MinPriority = 20;
        public const int DefaultPriority = (MinPriority - MaxPriority) / 2;

        public const int NoTimeLimit = 0;

        public const int DeadlockCheckIntervalMs = 1000;//check for deadlocks every second
        public const int DeadlockStopAfter = 10;//kill the application if it's been blocked for more than 5 CheckIntervals

        internal List<PendingTaskInfo> PendingElfTasks { get; private set; } = new List<PendingTaskInfo>();

        internal List<(ElfTask task, ElfTaskData taskData, Task executingTask, Task cleanupTask)> PausedTasks { get; private set; } = new List<(ElfTask, ElfTaskData, Task, Task)>();

        internal (ElfTask task, ElfTaskData taskData, Task executingTask, Task cleanupTask)?[] RunningTasks { get; private set; }

        private DeadlockChecker DeadlockChecker { get; set; }

        public int MaxTaskCount { get => RunningTasks.Length; }

        public int CurrentlyPending { get => PendingElfTasks.Count; }

        public int CurrentlyRunning { get => RunningTasks.Where(rt => rt.HasValue).Count(); }

        public bool IsPreemptive { get; private set; }

        public bool IsRealtime { get; private set; }

        public int GlobalTimeLimit { get; private set; }

        /// <summary>
        /// Creates a new instance of ElfTaskScheduler class
        /// </summary>
        /// <param name="maxThreadCount">Maximum of tasks to be executed at the same time at any given moment</param>
        /// <param name="isPreemptive">Specifies if the scheduled should preemptively swap in tasks with higher priority</param>
        /// <param name="isRealtime">Specifies if the scheduler should schedule new tasks as soon as threads become avilable or not</param>
        /// <param name="globalTimeLimit">If a positive number is passed it then all tasks must complete before given time, otherwise they're aborted.</param>
        public ElfTaskScheduler(int maxThreadCount, bool isPreemptive = false, bool isRealtime = true, int globalTimeLimit = NoTimeLimit)
        {
            if (globalTimeLimit < 0)
                throw new ArgumentException("Time limit must be an integer greater than 0!", "commonTimeLimit");
            if (maxThreadCount <= 0)
                throw new ArgumentException("Task scheduler needs at least one thread to function!", "maxThreadCount");
            RunningTasks = new (ElfTask, ElfTaskData, Task, Task)?[maxThreadCount];
            IsPreemptive = isPreemptive;
            IsRealtime = isRealtime;
            GlobalTimeLimit = globalTimeLimit;
            DeadlockChecker = new DeadlockChecker(this, DeadlockCheckIntervalMs, DeadlockStopAfter);
            DeadlockChecker.Start();
        }

        /// <summary>
        /// Schedules a task to be executed with given priority.
        /// </summary>
        /// <param name="elfTask">Task to be executed. ElfTask receives a single parameter of type <code>ElfTaskData</code></param>
        /// <param name="priority">Priority of the task to be scheduled. Must be in range [0,20] (inclusive)</param>
        /// <param name="timeLimitMs">Time limit in milliseconds to wait before forecefully killing the task</param>
        public void ScheduleTask(ElfTask elfTask, int priority = DefaultPriority, int timeLimitMs = NoTimeLimit)
        {
            if (priority < MaxPriority || priority > MinPriority)
                throw new ArgumentOutOfRangeException("priority", $"Prioroty must be in range [{MinPriority}-{MaxPriority}]");
            if (elfTask == null)
                throw new ArgumentNullException("elfTask", "Task must not be null!");
            int firstHigherPriority;
            if (PendingElfTasks.Count == 0)
                firstHigherPriority = 0;
            else
                firstHigherPriority = PendingElfTasks.FindIndex(pt => pt.Priority > priority);
            if (firstHigherPriority == -1)// -1 if all tasks are higher priority
                firstHigherPriority = PendingElfTasks.Count;//so we drop it in the back
            if (timeLimitMs == NoTimeLimit)
                timeLimitMs = GlobalTimeLimit;
            PendingElfTasks.Insert(firstHigherPriority, new PendingTaskInfo() { Task = elfTask, DurationLimit = timeLimitMs, Priority = priority });
            if (IsRealtime)
                RefreshTasks();
        }

        /// <summary>
        /// Tries to schedule pending and paused tasks on available threads if there's something to be shceduled. It can be
        /// called by users to force a refresh or it will be called after any task ends to signal that there's available 
        /// threads to work with. Due to this, scheduling methods lock certain properties.
        /// </summary>
        public void RefreshTasks()
        {
            RemoveFinishedTasks();
            lock (PendingElfTasks)
                if (PendingElfTasks.Count == 0 && PausedTasks.Count == 0)//if there's no pending tasks, nothing to schedule
                    return;
            int freeThreads;
            lock (RunningTasks)
                freeThreads = RunningTasks.Where(rtd => !rtd.HasValue).Count();
            if (IsPreemptive)//forefully try to swap low priority tasks out
                PreemptiveRefresh();
            else if (freeThreads > 0)//else it's nonpreemptive and there are some free threads to work with
                NonPreemptiveRefresh();
        }

        private void PreemptiveRefresh()
        {
            //It SHOULDNT happen that PendingTasks have higher priority than Paused tasks, but just to be sure
            int lowestPriorityRunningValue() => RunningTasks.Where(rt => rt.HasValue).Select(rt => rt.Value.taskData.Priority).DefaultIfEmpty(MaxPriority - 1).Max();
            bool shouldPreempt()//Any waiting task has higher priority (lower value)
            {
                int localLowest = lowestPriorityRunningValue();//local variable to avoid looping more than it's needed
                return PendingElfTasks.Any(pt => pt.Priority < localLowest) || PausedTasks.Any(pt => pt.taskData.Priority < localLowest);
            };
            if (!shouldPreempt())//nothing to preempt
            {
                if (RunningTasks.All(rt => rt.HasValue)) //all threads are busy
                    return;
                else//nothing to preempt but there are free threads
                    NonPreemptiveRefresh();//it's ok to do nonpreemptive in this case, and it made a problem
                return;//I dislike the else under this if
            }
            //now all threads are busy, but there's something to be booted out
            lock (this)//Lock everything as this loop will read and write to all task categories, Running, Pending and Paused
            {
                while (shouldPreempt())
                {
                    //sort them by priority, then get the first element
                    var lowestPriorityRunning = RunningTasks.OrderByDescending(rt => rt.HasValue ? rt.Value.taskData.Priority : MinPriority + 1).ElementAt(0);
                    var highestPriorityPaused = PausedTasks.OrderBy(pt => pt.taskData.Priority).DefaultIfEmpty((null, null, null, null)).ElementAtOrDefault(0);
                    var highestPriorityPending = PendingElfTasks.Count == 0 ? null : PendingElfTasks[0];
                    if (lowestPriorityRunning.HasValue)//Free tasks are possible in case preemptive was called and some tasks finished
                    {//put lowest priority one to sleep

                        int lowestIndex = Array.IndexOf(RunningTasks, lowestPriorityRunning);
                        lowestPriorityRunning.Value.taskData.IsPaused = true;
                        lowestPriorityRunning.Value.taskData.PauseToken.Reset();

                        if (highestPriorityPending != null && highestPriorityPaused.task == null)//easy case, spawn pending one instead of lowest priority one
                            RunningTasks[lowestIndex] = SpawnNewTask(highestPriorityPending);
                        else if (highestPriorityPending == null && highestPriorityPaused.task != null)//also easy, resume the paused task in place of lowest prio
                            RunningTasks[lowestIndex] = ResumePausedTask(highestPriorityPaused);
                        else//neither is null, check which is higher priority
                        {
                            if (highestPriorityPending.Priority < highestPriorityPaused.taskData.Priority)
                                RunningTasks[lowestIndex] = SpawnNewTask(highestPriorityPending);
                            else
                                RunningTasks[lowestIndex] = ResumePausedTask(highestPriorityPaused);
                        }
                        if (!lowestPriorityRunning.Value.executingTask.IsCompleted)
                            PausedTasks.Add(lowestPriorityRunning.Value);//add it to the paused collection AFTER picking what to spawn/resume
                    }
                    else//All "running" tasks had value of null, but there's work to be done
                        NonPreemptiveRefresh();//safe to do nonpreemptive
                }
            }
        }

        private void NonPreemptiveRefresh()
        {
            const int EmptyPriority = MinPriority + 1;//imposible priority
            lock (this)//Same as with PreemptiveRefresh. Don't want some other thread modifying collections until the refresh is complete
            {
                for (int i = 0; i < RunningTasks.Length; i++)
                {
                    if (!RunningTasks[i].HasValue)
                    {
                        int pausedPrio = PausedTasks.Count == 0 ? EmptyPriority : PausedTasks.OrderBy(pt => pt.taskData.Priority).ElementAt(0).taskData.Priority;
                        int pendingPrio = PendingElfTasks.Count == 0 ? EmptyPriority : PendingElfTasks[0].Priority;
                        if (pendingPrio < pausedPrio)//waiting task should go first, spawn it
                            RunningTasks[i] = SpawnNewTask(PendingElfTasks[0]);
                        else if (pausedPrio == pendingPrio && pausedPrio != EmptyPriority)//if they're equal, resume the one that had started earlier
                            RunningTasks[i] = ResumePausedTask(PausedTasks.OrderBy(pt => pt.taskData.Priority).ElementAt(0));
                        else if (pausedPrio != EmptyPriority)//they're not equal and pending isn't smaller but we have paused => resume the pused one
                            RunningTasks[i] = ResumePausedTask(PausedTasks.OrderBy(pt => pt.taskData.Priority).ElementAt(0));
                        else if (pendingPrio != EmptyPriority)//this should be picked up by the first if, may be unreachable
                            RunningTasks[i] = SpawnNewTask(PendingElfTasks[0]);
                        else
                            break;//nothing to do, no pending, no paused tasks
                    }
                }
            }
        }

        private void RemoveFinishedTasks()
        {
            lock (RunningTasks)//Block others from changing RunningTasks array while cleaning up
                for (int i = 0; i < RunningTasks.Length; ++i)
                    if (RunningTasks[i].HasValue)
                    {
                        (_, ElfTaskData taskData, Task executingTask, _) = RunningTasks[i].Value;
                        if (executingTask.IsCanceled || executingTask.IsCompleted || taskData.IsCanceled || taskData.IsReallyFinished)
                            RunningTasks[i] = null;
                    }
        }

        private (ElfTask task, ElfTaskData taskData, Task executingTask, Task cleanupTask) ResumePausedTask((ElfTask task, ElfTaskData taskData, Task executingTask, Task cleanupTask) pt)
        {
            pt.taskData.PauseToken.Set();//reset the lock
            pt.taskData.IsPaused = false;
            PausedTasks.Remove(pt);//Once resumed it's safe to remove it from PausedTasks list
            return pt;//it's now "running" again
        }

        private (ElfTask task, ElfTaskData taskData, Task executingTask, Task cleanupTask) SpawnNewTask(PendingTaskInfo pendingTaskInfo)
        {
            ElfTaskData taskData = new ElfTaskData(pendingTaskInfo.Priority);
            PendingElfTasks.Remove(pendingTaskInfo);
            return (pendingTaskInfo.Task, taskData,
                Task.Factory.StartNew(() => //construct the actual task to run
                {
                    pendingTaskInfo.Task(taskData);//start the actual main part
                    taskData.IsReallyFinished = true;//some tasks finished without setting their appropriate flags, so this is a workaround
                    RefreshTasks(); //also refresh after the task ends
                }),
                Task.Factory.StartNew(() =>//start a task that attempts to forcefully kill the scheduled task once it's time runs out
                {
                    if (pendingTaskInfo.DurationLimit > 0)
                    {
                        Task.Delay(pendingTaskInfo.DurationLimit).Wait();
                        taskData.Cancel();
                        RefreshTasks();//when killing a task forcefully make sure you refresh tasks after you mark it as canceled
                    }
                }));
        }

        public int[] GetPendingPriorities()
        {
            return PendingElfTasks.Select(pt => pt.Priority).ToArray();
        }

        /// <summary>
        /// It's a hack because tasks waiting don't actually have a Task associated with them so I've mapped pending 
        /// pending tasks into empty tasks to at least return a correct number of tasks, if not the correct tasks themselves.
        /// </summary>
        /// <returns>A collection of tasks currently waiting to be scheduled</returns>
        protected override IEnumerable<Task> GetScheduledTasks()
        {
            return PendingElfTasks.Select(pt => new Task(() => pt.Task(null))).Union(PausedTasks.Select(pt => pt.executingTask));
        }

        /// <summary>
        /// This is a disgusting hack, but it works. If someone used a TaskFactory with this
        /// custom scheduler that task gets wrapped up and passed as a normal ElfTask. Tasks passed
        /// by TaskFactory have no priority so they're assigned DefaultPriority. They also
        /// don't have execution time limit so they will respect global limit passed to the constructor
        /// </summary>
        /// <param name="task"></param>
        protected override void QueueTask(Task task)
        {
#if DEBUG
            Debug.WriteLine("Called QueueTask(Task) method!");
#endif
            ScheduleTask((td) =>
            {
                TryExecuteTask(task);//perform the task on the task's thread
                RefreshTasks();
            });//Default priority
        }

        ///<summary>
        ///No inlining support here
        ///</summary>
        protected override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued)
        {
            return false;
        }

        public void Dispose()
        {
#if DEBUG
            Debug.WriteLine("Disposing...");
#endif
            DeadlockChecker.Stop();//the method will block while waiting to actually stop
        }

        public override int MaximumConcurrencyLevel { get => MaxTaskCount; }
    }
}
