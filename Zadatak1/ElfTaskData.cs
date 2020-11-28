using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace Zadatak1
{
    public class ElfTaskData
    {
        public bool IsCanceled { get; private set; }
        internal ManualResetEventSlim PauseToken { get; private set; }
        public bool IsPaused { get; internal set; }
        public int Priority { get; internal set; }
        public ElfTaskData(int priority, bool isPaused = false) => (PauseToken, Priority, IsPaused) = (new ManualResetEventSlim(), priority, isPaused);
        public void Cancel() => IsCanceled = true;

        /// <summary>
        /// Used to block the task from resuming work, the user is supposed to
        /// check the IsPaused property before calling this method. I don't trust the user to do that so
        /// The method will perform a double check
        /// </summary>
        public void Pause()
        {
            if(IsPaused)
                PauseToken.Wait();
        }
    }
}
