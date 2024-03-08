using System.Collections.Generic;
using System.ServiceProcess;
using System.Configuration.Install;


namespace AntPlus_Service
{
    partial class AntInstaller
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary> 
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Component Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.BeforeInstall += new System.Configuration.Install.InstallEventHandler(ProjectInstaller_BeforeInstall);
            components = new System.ComponentModel.Container();
            this.AfterInstall += new InstallEventHandler(ServiceInstaller_AfterInstall);

        }

        private void ProjectInstaller_BeforeInstall(object sender, System.Configuration.Install.InstallEventArgs e)
        {
            List<ServiceController> services = new List<ServiceController>(ServiceController.GetServices());

            foreach (ServiceController s in services)
            {
                if (s.ServiceName == this.serviceInstaller.ServiceName)
                {
                    ServiceInstaller ServiceInstallerObj = new ServiceInstaller();
                    ServiceInstallerObj.Context = new System.Configuration.Install.InstallContext();
                    ServiceInstallerObj.Context = Context;
                    ServiceInstallerObj.ServiceName = "AplifitUSBANT";
                    ServiceInstallerObj.Uninstall(null);

                    break;
                }
            }
        }

        private void ServiceInstaller_AfterInstall(object sender, InstallEventArgs e)
        {
            // Start the service after installation
            using (ServiceController sc = new ServiceController("AplifitUSBANT"))
            {
                sc.Start();
            }
        }

        #endregion
    }
}