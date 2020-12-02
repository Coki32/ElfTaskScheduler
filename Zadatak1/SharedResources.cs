using System;
using System.Collections.Generic;
using System.Text;
using static Zadatak1.ElfTaskData;

namespace Zadatak1
{
    /// <summary>
    /// Class provides a single method used for consuming resources
    /// shared between different tasks. It does locking and unlocking 
    /// and notifies who is holding which resource
    /// </summary>
    class SharedResources
    {
        private static Dictionary<string, dynamic> globals = new Dictionary<string, dynamic>();
        private static List<object> locks = new List<object>();
        private static List<string> names = new List<string>();
        public static void UseResource(string name, ResourceConsumer action, ElfTaskData etd)
        {
            try
            {
                int lockId = -1;
                lock (names) lock(locks) //lock both cause I don't want anyone messing with lists while editing
                {
                    if (!names.Contains(name))
                    {
                        names.Add(name);
                        locks.Add(new object());
                        lockId = names.Count - 1;
                    }
                    else
                        lockId = names.IndexOf(name);
                }
                lock (locks[lockId])//Guess I can't lock on elements of a Dictionary so this is the "solution"
                {
                    etd.TakenResource(name);//flag it as taken
                    etd.WantedResources.Remove(name);//now that you have it, you no longer /WANT/ it
                    //Make sure that the name exists before accessing it
                    if (!globals.ContainsKey(name))
                        globals[name] = new object();
                    dynamic res = globals[name];
                    action(ref res);
                    globals[name] = res;
                }
            }
            finally//make sure we flag it as released in case scheduler wants to know
            {
                etd.ReleasedResource(name);
            }
        }

    }

}
