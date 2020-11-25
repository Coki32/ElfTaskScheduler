using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Zadatak1.Tests
{
    [TestClass]
    public class ElfTester
    {
        [TestMethod]
        public void AllAreScheduled()
        {
            //nonrealtime, nonpreemptive scheduler with no time limit
            ElfTaskScheduler ets = new ElfTaskScheduler(1,false,false,-1);
            int[] testPriorities = new int[] { 5, 15, 5, 12, 13, 12 };
            foreach (int tp in testPriorities)
                ets.ScheduleTask((td) => { }, tp);
            Assert.AreEqual(ets.CurrentlyPending, testPriorities.Length);
        }

        [TestMethod]
        public void ScheduledInCorrectOrder()
        {
            ElfTaskScheduler ets = new ElfTaskScheduler(1, false, false, -1);
            List<int> testPriorities = new List<int> { 5, 15, 5, 12, 13, 12 };
            foreach (int tp in testPriorities)
                ets.ScheduleTask((td) => { }, tp);
            testPriorities.Sort();
            for(int i=0; i<testPriorities.Count; i++)
            {
                Assert.AreEqual(testPriorities[i], ets.GetPendingPriorities()[i]);
            }
        }
    }
}
