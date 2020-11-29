using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using System.Linq;
using System.Threading;

namespace Zadatak1
{
    public class ElfTaskScheduler : TaskScheduler
    {
        //Mora biti elfTask
        public delegate void ElfTask(ElfTaskData taskData);

        //private delegate void CleanupDelegate();
        private class PendingTaskInfo
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

        private List<PendingTaskInfo> PendingElfTasks { get; set; } = new List<PendingTaskInfo>();

        private List<(ElfTask task, ElfTaskData taskData, Task executingTask, Task cleanupTask)> PausedTasks { get; set; } = new List<(ElfTask, ElfTaskData, Task, Task)>();

        private (ElfTask task, ElfTaskData taskData, Task executingTask, Task cleanupTask)?[] RunningTasks { get; set; }

        public int MaxTaskCount { get => RunningTasks.Length; }

        public int CurrentlyPending { get => PendingElfTasks.Count; }

        /// <summary>
        /// Total number of currently running tasks, including both raw tasks and properly scheduled tasks
        /// </summary>
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
        /// <param name="commonTimeLimit">If a positive number is passed it then all tasks must complete before given time, otherwise they're aborted.</param>
        public ElfTaskScheduler(int maxThreadCount, bool isPreemptive = false, bool isRealtime = true, int commonTimeLimit = NoTimeLimit)
        {
            if (commonTimeLimit < 0)
                throw new ArgumentException("Time limit must be an integer greater than 0!", "commonTimeLimit");
            if (maxThreadCount <= 0)
                throw new ArgumentException("Task scheduler needs at least one thread to function!", "maxThreadCount");
            RunningTasks = new (ElfTask, ElfTaskData, Task, Task)?[maxThreadCount];
            IsPreemptive = isPreemptive;
            IsRealtime = isRealtime;
            GlobalTimeLimit = commonTimeLimit;
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
            if (firstHigherPriority == -1)// -1 ako su svi manji od njega (svi su veceg prioriteta)
                firstHigherPriority = PendingElfTasks.Count;//zato ide na kraj
            if (timeLimitMs == NoTimeLimit)
                timeLimitMs = GlobalTimeLimit;
            PendingElfTasks.Insert(firstHigherPriority, new PendingTaskInfo() { Task = elfTask, DurationLimit = timeLimitMs, Priority = priority });
            if (IsRealtime)
                RefreshTasks();
        }

        //TODO: Ovo obrisi, samo test
        public void ubijSve()
        {
            for (int i = 0; i < RunningTasks.Length; i++)
            {
                if (RunningTasks[i].HasValue)
                {
                    RunningTasks[i].Value.taskData.Cancel();
                    RunningTasks[i] = null;
                }
            }
        }

        /// <summary>
        /// Metoda prerasporedjuje zadatke po prioritetu. Zbog toga sto je poziva cleanup task moguce je da vise Thread-ova istovremeno
        /// pristupi clanovima RunningTasks i PendingTasks zato se zakljucavaju
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
            Func<int> lowestPriorityRunningValue = () => RunningTasks.Where(rt => rt.HasValue).Select(rt => rt.Value.taskData.Priority).DefaultIfEmpty(MaxPriority - 1).Max();
            Func<bool> shouldPreempt = () =>//Any waiting task has higher priority (lower value)
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
            lock (this)
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
                            RunningTasks[lowestIndex] = spawnNewTask(highestPriorityPending);
                        else if (highestPriorityPending == null && highestPriorityPaused.task != null)//also easy, resume the paused task in place of lowest prio
                            RunningTasks[lowestIndex] = ResumePausedTask(highestPriorityPaused);
                        else//neither is null, check which is higher priority
                        {
                            if (highestPriorityPending.Priority < highestPriorityPaused.taskData.Priority)
                                RunningTasks[lowestIndex] = spawnNewTask(highestPriorityPending);
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
            lock (this)//Znaci niko nicemu ne moze pristupiti dok se ovo radi
            {
                for (int i = 0; i < RunningTasks.Length; i++)
                {
                    if (!RunningTasks[i].HasValue)
                    {

                        int pausedPrio = PausedTasks.Count == 0 ? EmptyPriority : PausedTasks.OrderBy(pt => pt.taskData.Priority).ElementAt(0).taskData.Priority;
                        int pendingPrio = PendingElfTasks.Count == 0 ? EmptyPriority : PendingElfTasks[0].Priority;
                        if (pendingPrio < pausedPrio)
                            RunningTasks[i] = spawnNewTask(PendingElfTasks[0]);
                        else if (pausedPrio == pendingPrio && pausedPrio != EmptyPriority)//if they're equal, resume the one that had started earlier
                            RunningTasks[i] = ResumePausedTask(PausedTasks.OrderBy(pt => pt.taskData.Priority).ElementAt(0));
                        else if (pendingPrio != EmptyPriority)//if pending is waiting and we didn't have paused tasks
                            RunningTasks[i] = spawnNewTask(PendingElfTasks[0]);
                        else
                            break;//nothing to do, no pending, no paused tasks
                    }
                }
            }
        }

        private void RemoveFinishedTasks()
        {
            lock (RunningTasks)//Da ne bi jos neki cleanup task probao da cisti isto dva puta
                for (int i = 0; i < RunningTasks.Length; i++)
                    if (RunningTasks[i].HasValue)
                    {
                        (_, ElfTaskData taskData, Task executingTask, _) = RunningTasks[i].Value;
                        if (executingTask.IsCanceled || executingTask.IsCompleted || taskData.IsCanceled)
                            RunningTasks[i] = null;
                    }
        }

        private (ElfTask task, ElfTaskData taskData, Task executingTask, Task cleanupTask) ResumePausedTask((ElfTask task, ElfTaskData taskData, Task executingTask, Task cleanupTask) pt)
        {
            pt.taskData.PauseToken.Set();//reset the lock
            pt.taskData.IsPaused = false;
            PausedTasks.Remove(pt);//Once resumed it's safe to remove it from PausedTasks list
            return pt;
        }

        private (ElfTask task, ElfTaskData taskData, Task executingTask, Task cleanupTask) spawnNewTask(PendingTaskInfo pendingTaskInfo)
        {
            ElfTaskData taskData = new ElfTaskData(pendingTaskInfo.Priority);
            PendingElfTasks.Remove(pendingTaskInfo);

            return (pendingTaskInfo.Task, taskData, Task.Factory.StartNew(() => { pendingTaskInfo.Task(taskData); if (IsRealtime) RefreshTasks(); }),
                Task.Factory.StartNew(() =>//start a task that attempts to forcefully kill the scheduled task once it's time runs out
                {
                    if (pendingTaskInfo.DurationLimit > 0)
                    {
                        Task.Delay(pendingTaskInfo.DurationLimit).Wait();
                        Console.WriteLine($"Task ostao bez vremena, ubijam ga!");
                        taskData.Cancel();
                        if (IsRealtime)
                            RefreshTasks();
                    }
                }));
        }

        public int[] GetPendingPriorities()
        {
            return PendingElfTasks.Select(pt => pt.Priority).ToArray();
        }

        /// <summary>
        /// It's a hack because tasks waiting don't actually have a Task associated
        /// with them so I've mapped pending pending tasks into empty tasks to at least
        /// return a correct number of tasks, if not the correct tasks themselves.
        /// </summary>
        /// <returns></returns>
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
            ScheduleTask((td) =>
            {
                this.TryExecuteTask(task);//Ovo ce uraditi taj task, sad treba kad on zavrsi
                if (IsRealtime)
                    RefreshTasks();
            }, DefaultPriority);
        }

        protected override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued)
        {
            if (task == null)
                throw new ArgumentNullException("task", "task must not be null!");
            if (task.IsCompleted)
                throw new InvalidOperationException("task has already been executed!");
            if (taskWasPreviouslyQueued)//queued tasks must wait their turn
                return false;
            throw new NotImplementedException();
        }

        public override int MaximumConcurrencyLevel { get => MaxTaskCount; }
    }
}
