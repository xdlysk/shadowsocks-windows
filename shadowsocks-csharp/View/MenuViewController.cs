using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;

using Shadowsocks.Controller;
using Shadowsocks.Model;
using Shadowsocks.Properties;
using Shadowsocks.Util;
using System.Linq;

namespace Shadowsocks.View
{
    public class MenuViewController
    {
        // yes this is just a menu view controller
        // when config form is closed, it moves away from RAM
        // and it should just do anything related to the config form

        private ShadowsocksController controller;
        private UpdateChecker updateChecker;

        private NotifyIcon _notifyIcon;
        private Bitmap icon_baseBitmap;
        private Icon icon_base, icon_in, icon_out, icon_both, targetIcon;
        private ContextMenu contextMenu1;

        private bool _isFirstRun;
        private bool _isStartupChecking;
        private MenuItem enableItem;
        private MenuItem modeItem;
        private MenuItem ShareOverLANItem;
        private MenuItem SeperatorItem;
        private MenuItem ConfigItem;
        private MenuItem ServersItem;
        private MenuItem globalModeItem;
        private MenuItem PACModeItem;
        private MenuItem checkPreReleaseToggleItem;
        private ConfigForm configForm;

        public MenuViewController(ShadowsocksController controller)
        {
            this.controller = controller;

            LoadMenu();

            controller.EnableStatusChanged += controller_EnableStatusChanged;
            controller.ConfigChanged += controller_ConfigChanged;
            controller.ShareOverLANStatusChanged += controller_ShareOverLANStatusChanged;
            controller.EnableGlobalChanged += controller_EnableGlobalChanged;
            controller.Errored += controller_Errored;

            _notifyIcon = new NotifyIcon();
            UpdateTrayIcon();
            _notifyIcon.Visible = true;
            _notifyIcon.ContextMenu = contextMenu1;
            _notifyIcon.BalloonTipClicked += notifyIcon1_BalloonTipClicked;
            _notifyIcon.MouseDoubleClick += notifyIcon1_DoubleClick;
            _notifyIcon.BalloonTipClosed += _notifyIcon_BalloonTipClosed;

            this.updateChecker = new UpdateChecker();
            updateChecker.CheckUpdateCompleted += updateChecker_CheckUpdateCompleted;

            LoadCurrentConfiguration();

            Configuration config = controller.GetConfigurationCopy();

            if (config.isDefault)
            {
                _isFirstRun = true;
                ShowConfigForm();
            }
            else if(config.autoCheckUpdate)
            {
                _isStartupChecking = true;
                updateChecker.CheckUpdate(config, 3000);
            }
        }

        private void controller_TrafficChanged(object sender, EventArgs e)
        {
            if (icon_baseBitmap == null)
                return;

            Icon newIcon;

            bool hasInbound = controller.trafficPerSecondQueue.Last().inboundIncreasement > 0;
            bool hasOutbound = controller.trafficPerSecondQueue.Last().outboundIncreasement > 0;

            if (hasInbound && hasOutbound)
                newIcon = icon_both;
            else if (hasInbound)
                newIcon = icon_in;
            else if (hasOutbound)
                newIcon = icon_out;
            else
                newIcon = icon_base;

            if (newIcon != this.targetIcon)
            {
                this.targetIcon = newIcon;
                _notifyIcon.Icon = newIcon;
            }
        }

        void controller_Errored(object sender, System.IO.ErrorEventArgs e)
        {
            MessageBox.Show(e.GetException().ToString(), String.Format(I18N.GetString("Shadowsocks Error: {0}"), e.GetException().Message));
        }

        #region Tray Icon

        private void UpdateTrayIcon()
        {
            int dpi;
            Graphics graphics = Graphics.FromHwnd(IntPtr.Zero);
            dpi = (int)graphics.DpiX;
            graphics.Dispose();
            icon_baseBitmap = null;
            if (dpi < 97)
            {
                // dpi = 96;
                icon_baseBitmap = Resources.ss16;
            }
            else if (dpi < 121)
            {
                // dpi = 120;
                icon_baseBitmap = Resources.ss20;
            }
            else
            {
                icon_baseBitmap = Resources.ss24;
            }
            Configuration config = controller.GetConfigurationCopy();
            bool enabled = config.enabled;
            bool global = config.global;
            icon_baseBitmap = getTrayIconByState(icon_baseBitmap, enabled, global);

            icon_base = Icon.FromHandle(icon_baseBitmap.GetHicon());
            targetIcon = icon_base;
            icon_in = Icon.FromHandle(AddBitmapOverlay(icon_baseBitmap, Resources.ssIn24).GetHicon());
            icon_out = Icon.FromHandle(AddBitmapOverlay(icon_baseBitmap, Resources.ssOut24).GetHicon());
            icon_both = Icon.FromHandle(AddBitmapOverlay(icon_baseBitmap, Resources.ssIn24, Resources.ssOut24).GetHicon());
            _notifyIcon.Icon = targetIcon;

            string serverInfo = null;
            if (controller.GetCurrentStrategy() != null)
            {
                serverInfo = controller.GetCurrentStrategy().Name;
            }
            else
            {
                serverInfo = config.GetCurrentServer().FriendlyName();
            }
            // show more info by hacking the P/Invoke declaration for NOTIFYICONDATA inside Windows Forms
            string text = I18N.GetString("Shadowsocks") + " " + UpdateChecker.Version + "\n" +
                          (enabled ?
                              I18N.GetString("System Proxy On: ") + (global ? I18N.GetString("Global") : I18N.GetString("PAC")) :
                              String.Format(I18N.GetString("Running: Port {0}"), config.localPort))  // this feedback is very important because they need to know Shadowsocks is running
                          + "\n" + serverInfo;
            ViewUtils.SetNotifyIconText(_notifyIcon, text);
        }

        private Bitmap getTrayIconByState(Bitmap originIcon, bool enabled, bool global)
        {
            Bitmap iconCopy = new Bitmap(originIcon);
            for (int x = 0; x < iconCopy.Width; x++)
            {
                for (int y = 0; y < iconCopy.Height; y++)
                {
                    Color color = originIcon.GetPixel(x, y);
                    if (color.A != 0)
                    {
                        if (!enabled)
                        {
                            Color flyBlue = Color.FromArgb(192, 192, 192);
                            // Multiply with flyBlue
                            int red = color.R * flyBlue.R / 255;
                            int green = color.G * flyBlue.G / 255;
                            int blue = color.B * flyBlue.B / 255;
                            iconCopy.SetPixel(x, y, Color.FromArgb(color.A, red, green, blue));
                        }
                        else if (global)
                        {
                            Color flyBlue = Color.FromArgb(25, 125, 191);
                            // Multiply with flyBlue
                            int red   = color.R * flyBlue.R / 255;
                            int green = color.G * flyBlue.G / 255; 
                            int blue  = color.B * flyBlue.B / 255;
                            iconCopy.SetPixel(x, y, Color.FromArgb(color.A, red, green, blue));
                        }
                    }
                    else
                    {
                        iconCopy.SetPixel(x, y, Color.FromArgb(color.A, color.R, color.G, color.B));
                    }
                }
            }
            return iconCopy;
        }

        private Bitmap AddBitmapOverlay(Bitmap original, params Bitmap[] overlays)
        {
            Bitmap bitmap = new Bitmap(original.Width, original.Height, PixelFormat.Format64bppArgb);
            Graphics canvas = Graphics.FromImage(bitmap);
            canvas.DrawImage(original, new Point(0, 0));
            foreach (Bitmap overlay in overlays)
            {
                canvas.DrawImage(new Bitmap(overlay, original.Size), new Point(0, 0));
            }
            canvas.Save();
            return bitmap;
        }

        #endregion

        #region MenuItems and MenuGroups

        private MenuItem CreateMenuItem(string text, EventHandler click)
        {
            return new MenuItem(I18N.GetString(text), click);
        }

        private MenuItem CreateMenuGroup(string text, MenuItem[] items)
        {
            return new MenuItem(I18N.GetString(text), items);
        }

        private void LoadMenu()
        {
            this.contextMenu1 = new ContextMenu(new MenuItem[] {
                this.enableItem = CreateMenuItem("Enable System Proxy", new EventHandler(this.EnableItem_Click)),
                this.modeItem = CreateMenuGroup("Mode", new MenuItem[] {
                    this.PACModeItem = CreateMenuItem("PAC", new EventHandler(this.PACModeItem_Click)),
                    this.globalModeItem = CreateMenuItem("Global", new EventHandler(this.GlobalModeItem_Click))
                }),
                this.ServersItem = CreateMenuGroup("Servers", new MenuItem[] {
                    this.SeperatorItem = new MenuItem("-"),
                    this.ConfigItem = CreateMenuItem("Edit Servers...", new EventHandler(this.Config_Click))
                }),
               
                new MenuItem("-"),
                this.ShareOverLANItem = CreateMenuItem("Allow Clients from LAN", new EventHandler(this.ShareOverLANItem_Click)),
                new MenuItem("-"),
                CreateMenuGroup("Help", new MenuItem[] {
                    CreateMenuGroup("Updates...", new MenuItem[] {
                        CreateMenuItem("Check for Updates...", new EventHandler(this.checkUpdatesItem_Click)),
                        new MenuItem("-"),
                        this.checkPreReleaseToggleItem = CreateMenuItem("Check Pre-release Version", new EventHandler(this.checkPreReleaseToggleItem_Click)),
                    }),
                    CreateMenuItem("About...", new EventHandler(this.AboutItem_Click)),
                }),
                new MenuItem("-"),
                CreateMenuItem("Quit", new EventHandler(this.Quit_Click))
            });
        }

        #endregion

        private void controller_ConfigChanged(object sender, EventArgs e)
        {
            LoadCurrentConfiguration();
            UpdateTrayIcon();
        }

        private void controller_EnableStatusChanged(object sender, EventArgs e)
        {
            enableItem.Checked = controller.GetConfigurationCopy().enabled;
            modeItem.Enabled = enableItem.Checked;
        }

        void controller_ShareOverLANStatusChanged(object sender, EventArgs e)
        {
            ShareOverLANItem.Checked = controller.GetConfigurationCopy().shareOverLan;
        }

        void controller_EnableGlobalChanged(object sender, EventArgs e)
        {
            globalModeItem.Checked = controller.GetConfigurationCopy().global;
            PACModeItem.Checked = !globalModeItem.Checked;
        }

        void controller_FileReadyToOpen(object sender, ShadowsocksController.PathEventArgs e)
        {
            string argument = @"/select, " + e.Path;

            System.Diagnostics.Process.Start("explorer.exe", argument);
        }

        void ShowBalloonTip(string title, string content, ToolTipIcon icon, int timeout)
        {
            _notifyIcon.BalloonTipTitle = title;
            _notifyIcon.BalloonTipText = content;
            _notifyIcon.BalloonTipIcon = icon;
            _notifyIcon.ShowBalloonTip(timeout);
        }

        void updateChecker_CheckUpdateCompleted(object sender, EventArgs e)
        {
            if (updateChecker.NewVersionFound)
            {
                ShowBalloonTip(String.Format(I18N.GetString("Shadowsocks {0} Update Found"), updateChecker.LatestVersionNumber + updateChecker.LatestVersionSuffix), I18N.GetString("Click here to update"), ToolTipIcon.Info, 5000);
            }
            else if (!_isStartupChecking)
            {
                ShowBalloonTip(I18N.GetString("Shadowsocks"), I18N.GetString("No update is available"), ToolTipIcon.Info, 5000);
            }
            _isStartupChecking = false;
        }

        void notifyIcon1_BalloonTipClicked(object sender, EventArgs e)
        {
            if (updateChecker.NewVersionFound)
            {
                updateChecker.NewVersionFound = false; /* Reset the flag */
                if (System.IO.File.Exists(updateChecker.LatestVersionLocalName))
                {
                    string argument = "/select, \"" + updateChecker.LatestVersionLocalName + "\"";
                    System.Diagnostics.Process.Start("explorer.exe", argument);
                }
            }
        }

        private void _notifyIcon_BalloonTipClosed(object sender, EventArgs e)
        {
            if (updateChecker.NewVersionFound)
            {
                updateChecker.NewVersionFound = false; /* Reset the flag */
            }
        }

        private void LoadCurrentConfiguration()
        {
            Configuration config = controller.GetConfigurationCopy();
            UpdateServersMenu();
            enableItem.Checked = config.enabled;
            modeItem.Enabled = config.enabled;
            globalModeItem.Checked = config.global;
            PACModeItem.Checked = !config.global;
            ShareOverLANItem.Checked = config.shareOverLan;
            UpdateUpdateMenu();
        }

        private void UpdateServersMenu()
        {
            var items = ServersItem.MenuItems;
            while (items[0] != SeperatorItem)
            {
                items.RemoveAt(0);
            }
            int i = 0;
            foreach (var strategy in controller.GetStrategies())
            {
                MenuItem item = new MenuItem(strategy.Name);
                item.Tag = strategy.ID;
                item.Click += AStrategyItem_Click;
                items.Add(i, item);
                i++;
            }

            // user wants a seperator item between strategy and servers menugroup
            items.Add( i++, new MenuItem("-") );

            int strategyCount = i;
            Configuration configuration = controller.GetConfigurationCopy();
            foreach (var server in configuration.configs)
            {
                MenuItem item = new MenuItem(server.FriendlyName());
                item.Tag = i - strategyCount;
                item.Click += AServerItem_Click;
                items.Add(i, item);
                i++;
            }

            foreach (MenuItem item in items)
            {
                if (item.Tag != null && (item.Tag.ToString() == configuration.index.ToString() || item.Tag.ToString() == configuration.strategy))
                {
                    item.Checked = true;
                }
            }
        }

        private void ShowConfigForm()
        {
            if (configForm != null)
            {
                configForm.Activate();
            }
            else
            {
                configForm = new ConfigForm(controller);
                configForm.Show();
                configForm.Activate();
                configForm.FormClosed += configForm_FormClosed;
            }
        }

        void configForm_FormClosed(object sender, FormClosedEventArgs e)
        {
            configForm.Dispose();
            configForm = null;
            Utils.ReleaseMemory(true);
            if (_isFirstRun)
            {
                CheckUpdateForFirstRun();
                ShowFirstTimeBalloon();
                _isFirstRun = false;
            }
        }

        private void Config_Click(object sender, EventArgs e)
        {
            ShowConfigForm();
        }

        private void Quit_Click(object sender, EventArgs e)
        {
            controller.Stop();
            _notifyIcon.Visible = false;
            Application.Exit();
        }

        private void CheckUpdateForFirstRun()
        {
            Configuration config = controller.GetConfigurationCopy();
            if (config.isDefault) return;
            _isStartupChecking = true;
            updateChecker.CheckUpdate(config, 3000);
        }

        private void ShowFirstTimeBalloon()
        {
            _notifyIcon.BalloonTipTitle = I18N.GetString("Shadowsocks is here");
            _notifyIcon.BalloonTipText = I18N.GetString("You can turn on/off Shadowsocks in the context menu");
            _notifyIcon.BalloonTipIcon = ToolTipIcon.Info;
            _notifyIcon.ShowBalloonTip(0);
        }

        private void AboutItem_Click(object sender, EventArgs e)
        {
            Process.Start("https://github.com/shadowsocks/shadowsocks-windows");
        }

        private void notifyIcon1_DoubleClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                ShowConfigForm();
            }
        }

        private void EnableItem_Click(object sender, EventArgs e)
        {
            controller.ToggleEnable(!enableItem.Checked);
        }

        private void GlobalModeItem_Click(object sender, EventArgs e)
        {
            controller.ToggleGlobal(true);
        }

        private void PACModeItem_Click(object sender, EventArgs e)
        {
            controller.ToggleGlobal(false);
        }

        private void ShareOverLANItem_Click(object sender, EventArgs e)
        {
            ShareOverLANItem.Checked = !ShareOverLANItem.Checked;
            controller.ToggleShareOverLAN(ShareOverLANItem.Checked);
        }


        private void AServerItem_Click(object sender, EventArgs e)
        {
            MenuItem item = (MenuItem)sender;
            controller.SelectServerIndex((int)item.Tag);
        }

        private void AStrategyItem_Click(object sender, EventArgs e)
        {
            MenuItem item = (MenuItem)sender;
            controller.SelectStrategy((string)item.Tag);
        }

        private void UpdateUpdateMenu()
        {
            Configuration configuration = controller.GetConfigurationCopy();
            checkPreReleaseToggleItem.Checked = configuration.checkPreRelease;
        }


        private void checkPreReleaseToggleItem_Click(object sender, EventArgs e)
        {
            Configuration configuration = controller.GetConfigurationCopy();
            controller.ToggleCheckingPreRelease(!configuration.checkPreRelease);
            UpdateUpdateMenu();
        }

        private void checkUpdatesItem_Click(object sender, EventArgs e)
        {
            updateChecker.CheckUpdate(controller.GetConfigurationCopy());
        }

    }
}
