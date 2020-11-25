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
            void pisiNPuta(int id, int n)
            {
                for (int i = 0; i < n; i++)
                {
                    Console.WriteLine($"{id} ===> {i}");
                    Thread.Sleep(500);
                }
            }
            TaskFactory tf = new TaskFactory(new ElfTaskScheduler(10, true, true));
            tf.StartNew(() => pisiNPuta(1, 5));
            tf.StartNew(() => pisiNPuta(2, 10));
            tf.StartNew(() => pisiNPuta(3, 5));

            Thread.Sleep(6000);

        }
        static void Main(string[] args)
        {
            Test1();
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
