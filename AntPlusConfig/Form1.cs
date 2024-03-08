using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.IO;
using System.ServiceProcess;


namespace AntPlusConfig
{
    public partial class Form1 : Form
    {
        private string configFilePath = @"config.txt";
        public Form1()
        {
            InitializeComponent();
            LoadSettings();
        }

        private void LoadSettings()
        {
            if (File.Exists(configFilePath))
            {
                string[] lines = File.ReadAllLines(configFilePath);
                if (lines.Length >= 5)
                {
                    textBox1.Text = lines[0];
                    textBox2.Text = lines[1];
                    textBox3.Text = lines[2];
                    textBox4.Text = lines[3];
                    textBox5.Text = lines[4];
                }
            }else
            {
                string[] defaultLines = { "0", "0", "57", "0xB9, 0xA5, 0x21, 0xFB, 0xBD, 0x72, 0xC3, 0x45", "0" };
                File.WriteAllLines(configFilePath, defaultLines);
                textBox1.Text = defaultLines[0];
                textBox2.Text = defaultLines[1];
                textBox3.Text = defaultLines[2];
                textBox4.Text = defaultLines[3];
                textBox5.Text = defaultLines[4];
            }
        }

        private void btnSave_Click(object sender, EventArgs e)
        {
            string[] lines = { textBox1.Text, textBox2.Text, textBox3.Text, textBox4.Text, textBox5.Text };
            File.WriteAllLines(configFilePath, lines);
            MessageBox.Show("Settings saved successfully.");
        }

        private void RestartService()
        {
            string serviceName = "AntPlus";
            using (ServiceController serviceController = new ServiceController(serviceName))
            {
                if (serviceController.Status == ServiceControllerStatus.Running)
                {
                    // If service is running, stop it
                    serviceController.Stop();
                    serviceController.WaitForStatus(ServiceControllerStatus.Stopped);
                }

                // Start the service
                serviceController.Start();
            }
        }

    }
}
