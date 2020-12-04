using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;


namespace Zadatak1.Demo
{
    class Program
    {
        static readonly int SecondsScale = 1000;

        /// <summary>
        /// This sample demonstrates how functions are scheduled using the defalt task factory
        /// on a scheduler with as many threads as there are processors on the host computer.
        /// More details in the README.md file.
        /// </summary>
        static void Test1()
        {
            const int delay = 20;
            const int numberOfTasks = 10;
            SemaphoreSlim semaphore = new SemaphoreSlim(0);
            TaskFactory tf = new TaskFactory(new ElfTaskScheduler(4, true, true));
            void BitOfSpam(int howMuch, string ofWhat, string name, int delayMs = delay)
            {
                for (int i = 0; i < howMuch; ++i)
                {
                    Thread.Sleep(delayMs);
                    Console.WriteLine($"[{name}]: {ofWhat}");
                }
                semaphore.Release();
            }
            Action wrapper(int i) => () => BitOfSpam(10, $"Spam text {i}", $"Task{i}");
            for (int i = 0; i < numberOfTasks; ++i)
                tf.StartNew(wrapper(i));//wrap the local variable 'i'
            while (semaphore.CurrentCount != numberOfTasks)//wait for all to release
                Thread.Sleep(10);
        }

        static void Test2()
        {
            //When having more tasks than concurrent threads you should use semaphore and count them instead of barriers
            //barriers will block and wait instead of ever finishing the task and letting some other task take the free thread
            const int numberOfTasks = 2;
            using (ElfTaskScheduler ets = new ElfTaskScheduler(numberOfTasks, true, true))
            {
                SemaphoreSlim semaphore = new SemaphoreSlim(0);
                void RepetitiveSpam(ElfTaskData etd, string name, int n)
                {
                    Console.WriteLine($"Starting {name}");
                    for (int i = 0; i < n; ++i)
                    {
                        if (etd.IsPaused)
                            etd.Pause();
                        if (etd.IsCanceled)
                            break;
                        Thread.Sleep(100);
                        Console.WriteLine($"{name} does {i}. iteration");
                    }
                    Console.WriteLine($"{name} finished");
                    semaphore.Release();
                }
                ets.ScheduleTask((etd) => RepetitiveSpam(etd, "First", 10), 18);
                ets.ScheduleTask((etd) => RepetitiveSpam(etd, "Second", 10), 15);
                Thread.Sleep(400);//simulate a delay between tasks
                Console.WriteLine("Starting third, it should preempt First");
                ets.ScheduleTask((etd) => RepetitiveSpam(etd, "Third", 5), 5);
                while (semaphore.CurrentCount != 3)//we expect 3 tasks to complete running, regardles of having only 2 threads in the scheduler
                    Thread.Sleep(10);//check ever 10 ms
            }
        }

        /// <summary>
        /// Described in 
        /// </summary>
        static void Test3()
        {
            const string resourceName = "n";
            void TakeAndIncrement(ElfTaskData etd, string name) => etd.TakeResource(name, (ref dynamic n) => n = (n is int) ? n + 1 : 1);
            using (ElfTaskScheduler ets = new ElfTaskScheduler(3, true, true))//3 as a demonstration that not all have to be running
            {
                Barrier barrier = new Barrier(3);//3 cause we have 2 workers and 1 main thread
                ets.ScheduleTask((etd) =>
                {
                    for (int i = 0; i < 100; ++i)
                        TakeAndIncrement(etd, resourceName);
                    barrier.SignalAndWait();
                });
                ets.ScheduleTask((etd) =>
                {
                    for (int i = 0; i < 100; ++i)
                        TakeAndIncrement(etd, resourceName);
                    barrier.SignalAndWait();
                });
                barrier.SignalAndWait();//wait for the first 2 to finish
                barrier.RemoveParticipant();//2 participants remaining, this thread and worker to print theresult
                ets.ScheduleTask((etd) =>
                {
                    etd.TakeResource(resourceName, (ref dynamic n) => Console.WriteLine($"After they're both done working n is now {n}"));
                    barrier.SignalAndWait();
                });
                barrier.SignalAndWait();
            }
        }

        static void Test4()//deadlock test
        {
            const int willKillIt = 30;//30  seconds
            const int wontKillIt = 3; //3 seconds
            const int pickOne = wontKillIt;
            const int numberOfTasks = 3;
            Barrier barrier = new Barrier(numberOfTasks + 1);
            using (ElfTaskScheduler ets = new ElfTaskScheduler(numberOfTasks, true, true))
            {
                ets.ScheduleTask((td) =>
                {
                    Console.WriteLine("First task starting");
                    td.TakeResource("y", (ref dynamic njv) =>
                    {
                        Console.WriteLine("First task took resource...");
                        Thread.Sleep(pickOne * 1000);//pretend to be working for a very long time
                    });
                    barrier.SignalAndWait();
                }, 15);//low priority so others will preempt it
                Thread.Sleep(100);//make sure the first one had the time to take it
                void simpleTask(ElfTaskData td, string name)
                {
                    Console.WriteLine($"{name} task asking for the resource");
                    td.TakeResource("y", (ref dynamic x) =>
                    {
                        Console.WriteLine($"{name} task got the resource!");
                    });
                    Console.WriteLine($"{name} task finished/released resource!");
                    barrier.SignalAndWait();
                };
                ets.ScheduleTask((td) => simpleTask(td, "First"));
                ets.ScheduleTask((td) => simpleTask(td, "Second"));
                ets.ScheduleTask((td) => simpleTask(td, "Third"));
                barrier.SignalAndWait();
            }
        }


        /// <summary>
        /// Here we'll try to "solve" a real problem: integrate a simple function using a tiny step
        /// x^{3}-x^{2\ }-\ 2x+\ 6 from -1.845 (f=0) to 2  (f=6), step=0.000000001
        /// </summary>
        static void Test5()
        {
            int numberOfTasks = Environment.ProcessorCount;
            const double dx = 0.000000001;
            const double A = -1.845, B = 2;
            double segmentLength = (B - A) / numberOfTasks;
            Barrier barrier = new Barrier(numberOfTasks + 1);//+1 for the current running thread
            double sampleFunc(double x) => x * x * x - x * x - 2 * x + 6;

            double integrateOnePart(double from, double to, double step, Func<double, double> f)
            {
                double subRes = 0.0;
                for (double x = from; x < to; x += step)
                    subRes += f(x) * dx;
                return subRes;
            }

            ElfTaskScheduler.ElfTask segmenter(int i)//Introducing a function to capture the 'i' which was used at the task creation, not the last one
            {
                return (ElfTaskData td) =>
                {
                    double from = A + i * segmentLength, to = A + (i + 1) * segmentLength;
                    double myRes = integrateOnePart(from, to, dx, sampleFunc);
                    Console.WriteLine($"Area under the segment [{from}, {to}) is {myRes}");
                    td.TakeResource("p", (ref dynamic p) => p += myRes);
                    barrier.SignalAndWait();
                };
            }

            using (ElfTaskScheduler ets = new ElfTaskScheduler(numberOfTasks, true, true))
            {
                //make sure this executes first, not touching the barrier, we only let workers do that, this task should end without affecting the barrier
                ets.ScheduleTask((td) => td.TakeResource("p", (ref dynamic p) => p = 0.0), 0);
                //spawn the calculating tasks...
                for (int i = 0; i < numberOfTasks; ++i)
                    ets.ScheduleTask(segmenter(i), i + 1);

                barrier.SignalAndWait();
                barrier.RemoveParticipants(numberOfTasks - 1);//leave only 2 participants, this one and one remaining worker

                ets.ScheduleTask((td) =>
                {
                    td.TakeResource("p", (ref dynamic p) => Console.WriteLine($"Area under the curve in range from {A} to {B} is {p} "));
                    barrier.SignalAndWait();
                });
                barrier.SignalAndWait();
            }
        }
        static void Main(string[] args)
        {
            Test1();
            Test2();
            Test3();
            Test4();
            Test5();
        }
    }
}
