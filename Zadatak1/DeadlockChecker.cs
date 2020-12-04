using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Linq;
using System.Diagnostics;

namespace Zadatak1
{
    /// <summary>
    /// Takes one ElfTaskScheduler instance and periodically checks to make sure that there's no deadlocks.
    /// </summary>
    /// <remarks>Couldn't come up with an elf-related name.</remarks>
    class DeadlockChecker
    {
        internal enum CheckerState { NotStarted, Starting, Running, Stopping, Stopped };
        private ElfTaskScheduler Ets { get; set; }

        private int Period { get; set; }

        private Thread Worker { get; set; } = null;

        private int LockedTimes { get; set; } = 0;

        private int LockedPeriod { get; set; }

        internal CheckerState State { get; private set; } = CheckerState.NotStarted;
        internal DeadlockChecker(ElfTaskScheduler ets, int periodInMs, int lockedPeriods) => (Ets, Period, LockedPeriod) = (ets, periodInMs, lockedPeriods);

        internal void Start()
        {
            if (Worker != null)
                return;
            State = CheckerState.Starting;
            Worker = new Thread(() =>
            {
                State = CheckerState.Running;
                while (State == CheckerState.Running)
                {
                    CheckForDeadlocks();
                    Thread.Sleep(Period);
                }
#if DEBUG
                Debug.WriteLine("Stao...");
#endif
                State = CheckerState.Stopped;
            });
            Worker.Start();
        }

        internal void Stop()
        {
            State = CheckerState.Stopping;
            while (State != CheckerState.Stopped)
                Thread.Sleep(10);//check every 10ms if it has actually stoppped
        }

        private void CheckForDeadlocks()
        {
            int blockedByPaused = 0, blockedByRunning = 0;
            foreach (var running in Ets.RunningTasks.Where(rt => rt.HasValue))
            {
                var WantedByCurrent = running.Value.taskData.WantedResources;
                var OthersAreHolding = Ets.RunningTasks
                    .Where(rt => rt.HasValue && rt.Value.executingTask != running.Value.executingTask)
                    .Select(rt => new List<string>(rt.Value.taskData.HeldResources))//Create explicit copies cause other threads are writing to the lists and I want a snapshot of the current state
                    .Where(hr => hr.Count > 0);
                if (Ets.PausedTasks.Count > 0)
                {
                    var HeldByPaused = Ets.PausedTasks.Select(pt => pt.taskData.HeldResources).Aggregate((l1, l2) => l1.Union(l2).ToList());
                    if (HeldByPaused.Count() > 0)
                    {
                        if (HeldByPaused.Intersect(WantedByCurrent).Count() > 0)
                            blockedByPaused++;
                        //here we could find out which paused task has the resource and push it's priority up but that 
                        //would probably introduce more issues so I've skipped that part. Deadlock resolution is to kill
                        //the whole process.
                    }
                    if (OthersAreHolding.Count() > 0)
                    {
                        //There's still a 50% chance of this throwing "Sequence contains no elements" exception and I have no more Count()s to check.
                        //OthersAreHolding is Enumberable containing lists so just merge lists together if there are some lists, not before, otherwise you get an exception
                        var OthersHoldingList = OthersAreHolding.Count() == 1 ? OthersAreHolding.First() : OthersAreHolding.Where(l => l.Count > 0).Aggregate((l1, l2) => l1.Union(l2).ToList());
                        if (OthersHoldingList.Intersect(WantedByCurrent).Count() > 0)
                            blockedByRunning++;

                    }
                }
            }
            //They're blocked by running tasks and there's hope for recovery so 
            if (blockedByRunning > 0 || blockedByPaused > 0)
                LockedTimes++;
            else
                LockedTimes = 0;//otherwise it seems to have resolved itself and that's it
            if (LockedTimes >= LockedPeriod)
            {
                Debug.WriteLine($"All tasks have been blocked for the {LockedTimes}. time, nothing seems to be moving, shutting down.");
                Environment.Exit(-1);
            }
        }
    }
}
