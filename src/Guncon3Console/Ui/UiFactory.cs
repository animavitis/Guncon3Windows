// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.Windows.Forms;

namespace Guncon3Console.Ui
{
    /// <summary>The two tool-strip items this app builds over and over: a text-only strip button and a menu
    /// item, each wired to one callback.</summary>
    internal static class UiFactory
    {
        public static ToolStripButton Button(string text, Action onClick)
        {
            var button = new ToolStripButton(text) { DisplayStyle = ToolStripItemDisplayStyle.Text };
            button.Click += (_, __) => onClick();
            return button;
        }

        public static ToolStripMenuItem MenuItem(string text, Action onClick)
            => new ToolStripMenuItem(text, null, (_, __) => onClick());
    }
}
