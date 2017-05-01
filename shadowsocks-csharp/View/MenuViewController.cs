using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Shadowsocks.Controller;
using Shadowsocks.Properties;
using Shadowsocks.Util;

namespace Shadowsocks.View
{
    public class MenuViewController
    {
        private readonly NotifyIcon _notifyIcon;
        // yes this is just a menu view controller
        // when config form is closed, it moves away from RAM
        // and it should just do anything related to the config form

        private readonly ShadowsocksController controller;
        private bool _isFirstRun;
        private bool _isStartupChecking;
        private MenuItem checkPreReleaseToggleItem;
        private ConfigForm configForm;
        private MenuItem ConfigItem;
        private ContextMenu contextMenu1;
        private MenuItem enableItem;
        private MenuItem globalModeItem;
        private Icon icon_base, icon_in, icon_out, icon_both, targetIcon;
        private Bitmap icon_baseBitmap;
        private MenuItem modeItem;
        private MenuItem PACModeItem;
        private MenuItem SeperatorItem;
        private MenuItem ServersItem;
        private MenuItem ShareOverLANItem;

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
            _notifyIcon.MouseDoubleClick += notifyIcon1_DoubleClick;

            LoadCurrentConfiguration();


            ShowConfigForm();
        }

        private void controller_TrafficChanged(object sender, EventArgs e)
        {
            if (icon_baseBitmap == null)
                return;

            Icon newIcon;

            var hasInbound = controller.trafficPerSecondQueue.Last().inboundIncreasement > 0;
            var hasOutbound = controller.trafficPerSecondQueue.Last().outboundIncreasement > 0;

            if (hasInbound && hasOutbound)
                newIcon = icon_both;
            else if (hasInbound)
                newIcon = icon_in;
            else if (hasOutbound)
                newIcon = icon_out;
            else
                newIcon = icon_base;

            if (newIcon != targetIcon)
            {
                targetIcon = newIcon;
                _notifyIcon.Icon = newIcon;
            }
        }

        private void controller_Errored(object sender, ErrorEventArgs e)
        {
            MessageBox.Show(e.GetException().ToString(),
                string.Format(I18N.GetString("Shadowsocks Error: {0}"), e.GetException().Message));
        }

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

        private void controller_ShareOverLANStatusChanged(object sender, EventArgs e)
        {
            ShareOverLANItem.Checked = controller.GetConfigurationCopy().shareOverLan;
        }

        private void controller_EnableGlobalChanged(object sender, EventArgs e)
        {
            globalModeItem.Checked = controller.GetConfigurationCopy().global;
            PACModeItem.Checked = !globalModeItem.Checked;
        }

        private void LoadCurrentConfiguration()
        {
            var config = controller.GetConfigurationCopy();
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
                items.RemoveAt(0);
            var i = 0;
            foreach (var strategy in controller.GetStrategies())
            {
                var item = new MenuItem(strategy.Name);
                item.Tag = strategy.ID;
                item.Click += AStrategyItem_Click;
                items.Add(i, item);
                i++;
            }

            // user wants a seperator item between strategy and servers menugroup
            items.Add(i++, new MenuItem("-"));

            var strategyCount = i;
            var configuration = controller.GetConfigurationCopy();
            foreach (var server in configuration.configs)
            {
                var item = new MenuItem(server.FriendlyName());
                item.Tag = i - strategyCount;
                item.Click += AServerItem_Click;
                items.Add(i, item);
                i++;
            }

            foreach (MenuItem item in items)
                if ((item.Tag != null) &&
                    ((item.Tag.ToString() == configuration.index.ToString()) ||
                     (item.Tag.ToString() == configuration.strategy)))
                    item.Checked = true;
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

        private void configForm_FormClosed(object sender, FormClosedEventArgs e)
        {
            configForm.Dispose();
            configForm = null;
            Utils.ReleaseMemory(true);
            if (_isFirstRun)
            {
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
                ShowConfigForm();
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
            var item = (MenuItem) sender;
            controller.SelectServerIndex((int) item.Tag);
        }

        private void AStrategyItem_Click(object sender, EventArgs e)
        {
            var item = (MenuItem) sender;
            controller.SelectStrategy((string) item.Tag);
        }

        private void UpdateUpdateMenu()
        {
            var configuration = controller.GetConfigurationCopy();
            checkPreReleaseToggleItem.Checked = configuration.checkPreRelease;
        }


        private void checkPreReleaseToggleItem_Click(object sender, EventArgs e)
        {
            var configuration = controller.GetConfigurationCopy();
            controller.ToggleCheckingPreRelease(!configuration.checkPreRelease);
            UpdateUpdateMenu();
        }

        #region Tray Icon

        private void UpdateTrayIcon()
        {
            int dpi;
            var graphics = Graphics.FromHwnd(IntPtr.Zero);
            dpi = (int) graphics.DpiX;
            graphics.Dispose();
            icon_baseBitmap = null;
            if (dpi < 97)
                icon_baseBitmap = Resources.ss16;
            else if (dpi < 121)
                icon_baseBitmap = Resources.ss20;
            else
                icon_baseBitmap = Resources.ss24;
            var config = controller.GetConfigurationCopy();
            var enabled = config.enabled;
            var global = config.global;
            icon_baseBitmap = getTrayIconByState(icon_baseBitmap, enabled, global);

            icon_base = Icon.FromHandle(icon_baseBitmap.GetHicon());
            targetIcon = icon_base;
            icon_in = Icon.FromHandle(AddBitmapOverlay(icon_baseBitmap, Resources.ssIn24).GetHicon());
            icon_out = Icon.FromHandle(AddBitmapOverlay(icon_baseBitmap, Resources.ssOut24).GetHicon());
            icon_both =
                Icon.FromHandle(AddBitmapOverlay(icon_baseBitmap, Resources.ssIn24, Resources.ssOut24).GetHicon());
            _notifyIcon.Icon = targetIcon;
        }

        private Bitmap getTrayIconByState(Bitmap originIcon, bool enabled, bool global)
        {
            var iconCopy = new Bitmap(originIcon);
            for (var x = 0; x < iconCopy.Width; x++)
                for (var y = 0; y < iconCopy.Height; y++)
                {
                    var color = originIcon.GetPixel(x, y);
                    if (color.A != 0)
                    {
                        if (!enabled)
                        {
                            var flyBlue = Color.FromArgb(192, 192, 192);
                            // Multiply with flyBlue
                            var red = color.R*flyBlue.R/255;
                            var green = color.G*flyBlue.G/255;
                            var blue = color.B*flyBlue.B/255;
                            iconCopy.SetPixel(x, y, Color.FromArgb(color.A, red, green, blue));
                        }
                        else if (global)
                        {
                            var flyBlue = Color.FromArgb(25, 125, 191);
                            // Multiply with flyBlue
                            var red = color.R*flyBlue.R/255;
                            var green = color.G*flyBlue.G/255;
                            var blue = color.B*flyBlue.B/255;
                            iconCopy.SetPixel(x, y, Color.FromArgb(color.A, red, green, blue));
                        }
                    }
                    else
                    {
                        iconCopy.SetPixel(x, y, Color.FromArgb(color.A, color.R, color.G, color.B));
                    }
                }
            return iconCopy;
        }

        private Bitmap AddBitmapOverlay(Bitmap original, params Bitmap[] overlays)
        {
            var bitmap = new Bitmap(original.Width, original.Height, PixelFormat.Format64bppArgb);
            var canvas = Graphics.FromImage(bitmap);
            canvas.DrawImage(original, new Point(0, 0));
            foreach (var overlay in overlays)
                canvas.DrawImage(new Bitmap(overlay, original.Size), new Point(0, 0));
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
            contextMenu1 = new ContextMenu(new[]
            {
                enableItem = CreateMenuItem("Enable System Proxy", EnableItem_Click),
                modeItem = CreateMenuGroup("Mode", new[]
                {
                    PACModeItem = CreateMenuItem("PAC", PACModeItem_Click),
                    globalModeItem = CreateMenuItem("Global", GlobalModeItem_Click)
                }),
                ServersItem = CreateMenuGroup("Servers", new[]
                {
                    SeperatorItem = new MenuItem("-"),
                    ConfigItem = CreateMenuItem("Edit Servers...", Config_Click)
                }),
                new MenuItem("-"),
                ShareOverLANItem = CreateMenuItem("Allow Clients from LAN", ShareOverLANItem_Click),
                new MenuItem("-"),
                CreateMenuGroup("Help", new[]
                {
                    CreateMenuGroup("Updates...", new[]
                    {
                        checkPreReleaseToggleItem =
                            CreateMenuItem("Check Pre-release Version", checkPreReleaseToggleItem_Click)
                    }),
                    CreateMenuItem("About...", AboutItem_Click)
                }),
                new MenuItem("-"),
                CreateMenuItem("Quit", Quit_Click)
            });
        }

        #endregion
    }
}