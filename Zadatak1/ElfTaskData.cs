using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace Zadatak1
{
    /// <summary>
    /// Used as a means of communication between the scheduler and a task and also between tasks via shared resources.
    /// </summary>
    public class ElfTaskData
    {
        public delegate void ResourceConsumer(ref dynamic value);
        
        /// <summary>
        /// Used to signalize pause, much nicer than waiting for a monitor elsewhere to release
        /// </summary>
        internal ManualResetEventSlim PauseToken { get; private set; } = new ManualResetEventSlim();

        public bool IsCanceled { get; private set; }

        public bool IsPaused { get; internal set; }

        internal bool IsReallyFinished { get; set; } = false;

        public int Priority { get; internal set; }

        /// <summary>
        /// Keeping track of resources task associated with this Data instance is holding
        /// </summary>
        internal List<string> HeldResources { get; } = new List<string>();
        
        /// <summary>
        /// And also keeping track of which resources it wants to get a hold of.
        /// </summary>
        internal List<string> WantedResources { get; } = new List<string>();

        internal ElfTaskData(int priority, bool isPaused = false) => (Priority, IsPaused) = (priority, isPaused);
        internal void Cancel() => IsCanceled = true;

        /// <summary>
        /// Used to block the task from resuming work, the user is supposed to check the IsPaused property before calling
        /// this method. I don't trust the user to do that so the method will perform a double check.
        /// </summary>
        public void Pause()
        {
            if (IsPaused)
                PauseToken.Wait();
        }

        public void TakeResource(string name, ResourceConsumer consumer)
        {
            WantedResources.Add(name);//Before you take it => you want it
            SharedResources.UseResource(name, consumer, this);//will remove it from wanted once it gets it
        }

        internal void TakenResource(string name) => HeldResources.Add(name);

        internal void ReleasedResource(string name) => HeldResources.Remove(name);

    }
}
