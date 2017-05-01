using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;
using Shadowsocks.Controller;
using Shadowsocks.Properties;

namespace Shadowsocks.View
{
    public class MenuViewController
    {
        private readonly NotifyIcon _notifyIcon;
        // yes this is just a menu view controller
        // when config form is closed, it moves away from RAM
        // and it should just do anything related to the config form

        private readonly ShadowsocksController controller;
        private ContextMenu contextMenu1;
        private Icon icon_base, targetIcon;
        private Bitmap icon_baseBitmap;
        public MenuViewController(ShadowsocksController controller)
        {
            this.controller = controller;

            LoadMenu();

            _notifyIcon = new NotifyIcon();
            UpdateTrayIcon();
            _notifyIcon.Visible = true;
            _notifyIcon.ContextMenu = contextMenu1;

        }

        private void Quit_Click(object sender, EventArgs e)
        {
            controller.Stop();
            _notifyIcon.Visible = false;
            Application.Exit();
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

            icon_base = Icon.FromHandle(icon_baseBitmap.GetHicon());
            targetIcon = icon_base;
            _notifyIcon.Icon = targetIcon;
        }

       

        #endregion

        #region MenuItems and MenuGroups

        private MenuItem CreateMenuItem(string text, EventHandler click)
        {
            return new MenuItem(text,click);
        }

        private void LoadMenu()
        {
            contextMenu1 = new ContextMenu(new[]
            {
                CreateMenuItem("Quit", Quit_Click)
            });
        }

        #endregion
    }
}