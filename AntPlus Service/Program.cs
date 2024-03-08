using System;
using System.Collections.Generic;
using System.Linq;
using System.ServiceProcess;
using System.Text;
using System.Threading.Tasks;
using System.Reflection;
using System.Configuration.Install;

namespace AntPlus_Service
{
    internal static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        static void Main(string[] args)
        {
/*            if (Environment.UserInteractive)
            {
                try
                {
                    ManagedInstallerClass.InstallHelper(new[] { Assembly.GetExecutingAssembly().Location });
                }catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                }
            } */
            ServiceBase[] ServicesToRun;
            ServicesToRun = new ServiceBase[]
            {
                new AntPlus_Service()
            };
            ServiceBase.Run(ServicesToRun);
        }
    }
}
