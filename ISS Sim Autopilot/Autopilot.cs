using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using CefSharp;
using CefSharp.WinForms;

namespace ISS_Sim_Autopilot
{
    public partial class Autopilot : Form
    {
        private ChromiumWebBrowser browser;

        public Autopilot()
        {
            InitializeComponent();
            InitBrowser();
        }

        private void InitBrowser()
        {
            Cef.Initialize(new CefSettings());
            Cef.EnableHighDPISupport();
            browser = new ChromiumWebBrowser("https://google.com/");
            Controls.Add(browser);
            browser.Parent = panelBrowser;
            browser.Dock = DockStyle.Fill;
        }

        private void buttonStart_Click(object sender, EventArgs e)
        {
            
            if(timerAutopilot.Enabled)
            {
                EndAutopilot();
                buttonStart.Text = "Initiate Autopilot";
            }
            else
            {
                StartAutopilot();
                buttonStart.Text = "Terminate Autopilot";
            }
        }

        private void buttonRefresh_Click(object sender, EventArgs e)
        {
            browser.Load("https://iss-sim.spacex.com/");
            //browser.Reload();
        }

        private void timerAutopilot_Tick(object sender, EventArgs e)
        {
            AutopilotTick();
        }
    }
}
