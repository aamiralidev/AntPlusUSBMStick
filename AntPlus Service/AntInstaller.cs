using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Configuration.Install;
using System.Linq;
using System.Threading.Tasks;
using System.ServiceProcess;
using System.ComponentModel;

namespace AntPlus_Service
{
    [RunInstaller(true)]
    public partial class AntInstaller : System.Configuration.Install.Installer
    {
        private ServiceProcessInstaller processInstaller;
        private ServiceInstaller serviceInstaller;
        public AntInstaller()
        {
            InitializeComponent();
            processInstaller = new ServiceProcessInstaller();
            processInstaller.Account = ServiceAccount.LocalSystem;

            serviceInstaller = new ServiceInstaller();
            serviceInstaller.ServiceName = "AplifitUSBANT";
            serviceInstaller.DisplayName = "Aplifit USB ANT";
            serviceInstaller.Description = "collect sensor data using antplus";
            serviceInstaller.StartType = ServiceStartMode.Automatic;

            Installers.Add(serviceInstaller);
            Installers.Add(processInstaller);
        }
    }
}
