using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Zadatak1.Tests
{
    [TestClass]
    public class ElfTester
    {
        [TestMethod]
        public void AllAreScheduled()
        {
            //nonrealtime, nonpreemptive scheduler with no time limit
            ElfTaskScheduler ets = new ElfTaskScheduler(1,false,false);
            int[] testPriorities = new int[] { 5, 15, 5, 12, 13, 12 };
            foreach (int tp in testPriorities)
                ets.ScheduleTask((td) => { }, tp);
            Assert.AreEqual(ets.CurrentlyPending, testPriorities.Length);
        }

        [TestMethod]
        public void ScheduledInCorrectOrder()
        {
            ElfTaskScheduler ets = new ElfTaskScheduler(1, false, false);
            List<int> testPriorities = new List<int> { 5, 15, 5, 12, 13, 12 };
            foreach (int tp in testPriorities)
                ets.ScheduleTask((td) => { }, tp);
            testPriorities.Sort();
            for(int i=0; i<testPriorities.Count; i++)
            {
                Assert.AreEqual(testPriorities[i], ets.GetPendingPriorities()[i]);
            }
        }

        [TestMethod]
        public void NoMissedTasks()
        {
            const int schedulerThreadCount = 5;
            const int spawnerThreadCount = 100;//Spawn 100 tasks from different threads
            const int taskTimeout = 10;
            ElfTaskScheduler ets = new ElfTaskScheduler(schedulerThreadCount, false, false);
            List < Thread > spawners = Enumerable.Range(0, spawnerThreadCount).Select(i => new Thread(() => ets.ScheduleTask((td) => Task.Delay(taskTimeout).Wait(), ElfTaskScheduler.DefaultPriority))).ToList();
            foreach (var t in spawners)
                t.Start();
            foreach (var t in spawners)
                t.Join();
            ets.RefreshTasks();
            Assert.AreEqual(ets.CurrentlyRunning, schedulerThreadCount);//some are running
            Assert.AreEqual(ets.CurrentlyPending, spawnerThreadCount-schedulerThreadCount);//the rest are pending
        }
    }
}
