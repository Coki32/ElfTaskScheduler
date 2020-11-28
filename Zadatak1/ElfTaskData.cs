using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace Zadatak1
{
    public class ElfTaskData
    {
        public delegate void ResourceConsumer(ref dynamic value);
        
        internal ManualResetEventSlim PauseToken { get; private set; } = new ManualResetEventSlim();
        public bool IsCanceled { get; private set; }

        public bool IsPaused { get; internal set; }

        public int Priority { get; internal set; }

        internal List<string> HeldResources { get; } = new List<string>();
        internal List<string> WantedResources { get; } = new List<string>();

        public ElfTaskData(int priority, bool isPaused = false) => (Priority, IsPaused) = (priority, isPaused);
        public void Cancel() => IsCanceled = true;

        /// <summary>
        /// Used to block the task from resuming work, the user is supposed to
        /// check the IsPaused property before calling this method. I don't trust the user to do that so
        /// The method will perform a double check
        /// </summary>
        public void Pause()
        {
            if (IsPaused)
                PauseToken.Wait();
        }

        public void TakeResource(string name, ResourceConsumer consumer)
        {
            WantedResources.Add(name);//Before you take it => you want it
            SharedResources.UseResource(name, consumer, this);
            WantedResources.Remove(name);//once you're done with it => you don't want it anymore
        }

        internal void TakenResource(string name) => HeldResources.Add(name);

        internal void ReleasedResource(string name) => HeldResources.Remove(name);

    }
}
