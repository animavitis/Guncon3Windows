// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.Drawing;
using System.Windows.Forms;

namespace Guncon3Console.Ui
{
    /// <summary>The notification-area icon and its menu. Wrapping NotifyIcon keeps the menu building out of
    /// MainForm and puts the icon's one hard rule in one place: it must be hidden before it is disposed, or
    /// Windows leaves a ghost icon behind until the taskbar is hovered.</summary>
    internal sealed class TrayIcon : IDisposable
    {
        private const int BalloonMs = 3000;

        private readonly NotifyIcon _icon;
        private readonly ContextMenuStrip _menu;
        private readonly ToolStripMenuItem _searchAgain;
        private readonly ToolStripMenuItem _recalibrate;
        private readonly ToolStripMenuItem _reload;
        private readonly ToolStripMenuItem _toggleMode;

        public event Action ShowHideClicked;
        public event Action SearchAgainClicked;
        public event Action RecalibrateClicked;
        public event Action ReloadMappingsClicked;
        public event Action ToggleModeClicked;
        public event Action ExitClicked;
        public event Action DoubleClicked;

        public TrayIcon(Icon icon)
        {
            _searchAgain = UiFactory.MenuItem("Search again", () => SearchAgainClicked?.Invoke());
            _searchAgain.Available = false;     // shown only while no gun is connected
            _recalibrate = UiFactory.MenuItem("Recalibrate", () => RecalibrateClicked?.Invoke());
            _reload = UiFactory.MenuItem("Reload mappings", () => ReloadMappingsClicked?.Invoke());
            _toggleMode = UiFactory.MenuItem("Toggle calibration mode", () => ToggleModeClicked?.Invoke());

            _menu = new ContextMenuStrip();
            _menu.Items.Add(UiFactory.MenuItem("Show / Hide", () => ShowHideClicked?.Invoke()));
            _menu.Items.Add(_searchAgain);
            _menu.Items.Add(_recalibrate);
            _menu.Items.Add(_reload);
            _menu.Items.Add(_toggleMode);
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(UiFactory.MenuItem("Exit", () => ExitClicked?.Invoke()));

            _icon = new NotifyIcon
            {
                Icon = icon,
                Text = "GUNCON3",
                ContextMenuStrip = _menu,
                Visible = true
            };
            _icon.DoubleClick += (_, __) => DoubleClicked?.Invoke();
        }

        /// <summary>Hover text. Windows truncates past 63 characters, which "GUNCON3 — N gun(s) connected"
        /// never reaches.</summary>
        public string Tooltip
        {
            get => _icon.Text;
            set => _icon.Text = value;
        }

        /// <summary>Shows the Search again item. <c>Available</c> rather than <c>Visible</c>: a menu item's
        /// Visible getter reports false whenever its menu is not on screen, which for a tray menu is almost
        /// always.</summary>
        public bool SearchAgainVisible
        {
            get => _searchAgain.Available;
            set => _searchAgain.Available = value;
        }

        /// <summary>Greys out the three engine actions while one of them is already running.</summary>
        public bool ActionsEnabled
        {
            get => _recalibrate.Enabled;
            set
            {
                _recalibrate.Enabled = value;
                _reload.Enabled = value;
                _toggleMode.Enabled = value;
            }
        }

        /// <summary>The one-time "still running" notification. Windows ignores it when notifications are off
        /// for the app; nothing depends on it being seen.</summary>
        public void ShowBalloon(string title, string text)
            => _icon.ShowBalloonTip(BalloonMs, title, text, ToolTipIcon.Info);

        public void Dispose()
        {
            _icon.Visible = false;
            _icon.Dispose();
            _menu.Dispose();
        }
    }
}
