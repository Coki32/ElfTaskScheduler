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
        static void Test1()
        {
            void uzmiIPovecaj(string sta, ElfTaskData etd)
            {
                etd.TakeResource(sta, (ref dynamic res) =>
                {
                    Console.WriteLine($"Uzeo sam {sta}={res}");
                    res += 1;
                    Console.WriteLine($"Pustam {sta}={res}");
                });
            }
            void postaviNa0(string sta, ElfTaskData etd)
            {
                etd.TakeResource(sta, (ref dynamic res) =>
                {
                    if (!(res is int))
                        res = 0;
                });
            }
            ElfTaskScheduler ets = new ElfTaskScheduler(3, true, false);
            ets.ScheduleTask(td => postaviNa0("n", td));
            ets.RefreshTasks();
            ets.ScheduleTask(td =>
            {
                for (int i = 0; i < 100; i++)
                    uzmiIPovecaj("n", td);
            });
            ets.ScheduleTask(td =>
            {
                for (int i = 0; i < 100; i++)
                    uzmiIPovecaj("n", td);
            });
            ets.ScheduleTask(td =>
            {
                Thread.Sleep(5000);
                td.TakeResource("n", (ref dynamic res) =>
                {
                    Console.WriteLine($"Na kraju n={res}");
                });
            });
            ets.RefreshTasks();
            Thread.Sleep(60000);

        }

        static void Test2()
        {
            ElfTaskScheduler ets = new ElfTaskScheduler(2, true, true);
            void RepetitiveSpam(ElfTaskData etd, string name, int n)
            {
                Console.WriteLine($"Pokrecem {name}");
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
            }
            ets.ScheduleTask((etd) => RepetitiveSpam(etd, "First", 10), 18);
            ets.ScheduleTask((etd) => RepetitiveSpam(etd, "Second", 10), 15);
            Thread.Sleep(300);
            Console.WriteLine("Starting third, it should preempt First");
            ets.ScheduleTask((etd) => RepetitiveSpam(etd, "Third", 5), 5);
            Thread.Sleep(15000);
        }

        static void Test3()
        {
            const string resourceName = "n";
            void TakeAndIncrement(ElfTaskData etd, string name) => etd.TakeResource(name, (ref dynamic n) => n = (n is int) ? n + 1 : 1);
            ElfTaskScheduler ets = new ElfTaskScheduler(3, true, true);
            ets.ScheduleTask((etd) =>
            {
                for (int i = 0; i < 100; ++i)
                    TakeAndIncrement(etd, resourceName);
            });
            ets.ScheduleTask((etd) =>
            {
                for (int i = 0; i < 100; ++i)
                    TakeAndIncrement(etd, resourceName);
            });
            Thread.Sleep(200);//let first 2 tasks finish their work before printing total result due to lack of synchronization
            ets.ScheduleTask((etd) =>
            {
                etd.TakeResource(resourceName, (ref dynamic n) => Console.WriteLine($"After they're both done working n is now {n}"));
            });
            Thread.Sleep(10000000);
        }
        static void Main(string[] args)
        {
            Test3();
            return;
            ElfTaskScheduler elfTaskScheduler = new ElfTaskScheduler(2, true, false, -1);

            elfTaskScheduler.ScheduleTask(td => spamFunkcija(td, "10", 20), 10);
            elfTaskScheduler.ScheduleTask(td => spamFunkcija(td, "8", 20), 8);
            elfTaskScheduler.RefreshTasks();
            Thread.Sleep(3000);
            Console.WriteLine("Dodajem zadatak prio 3");
            elfTaskScheduler.ScheduleTask(td => spamFunkcija(td, "3", 10), 3);
            elfTaskScheduler.RefreshTasks();
            Thread.Sleep(3000);
            Console.WriteLine("Dodajem zadatak prio 2");
            elfTaskScheduler.ScheduleTask(td => spamFunkcija(td, "2", 5), 2);
            elfTaskScheduler.RefreshTasks();
            Thread.Sleep(20000);
            //elfTaskScheduler.ubijSve();
        }

        static void spamFunkcija(ElfTaskData td, string name, int umriNakon = -1)
        {
            int c = 0;
            while (true)
            {
                if (c > umriNakon)
                    break;
                c++;
                if (td.IsPaused)
                    td.Pause();
                Console.WriteLine(name);
                Thread.Sleep(200);
            }
            Console.WriteLine($"{name} izbrojao do {c}, umire na {umriNakon}");
        }
    }
}
