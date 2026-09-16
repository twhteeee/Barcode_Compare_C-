using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace PLCCompare
{
    /// <summary>
    /// Keeps an absolute-positioned layout intact on any screen size.
    ///
    /// The layout is authored once against a fixed "design canvas" (1500 x 800 client pixels).
    /// Capture() records every control's original bounds and font size, then every resize
    /// re-applies them multiplied by a single uniform scale factor:
    ///
    ///     scale = min(clientWidth / designWidth, clientHeight / designHeight)
    ///
    /// Because the same factor is used for X and Y, the proportions of the original design
    /// never change - the whole canvas just grows or shrinks and is centred in the window.
    /// Nothing can drift out of view on a small screen, and nothing clumps into the top-left
    /// corner on a large one.
    /// </summary>
    internal sealed class LayoutScaler
    {
        private readonly Form form;
        private readonly Size designSize;

        private readonly Dictionary<Control, Rectangle> originalBounds = new Dictionary<Control, Rectangle>();
        private readonly Dictionary<Control, float> originalFontSize = new Dictionary<Control, float>();
        private readonly List<Font> createdFonts = new List<Font>();

        private bool captured;
        private float appliedScale = float.NaN;

        public LayoutScaler(Form form, Size designSize)
        {
            if (form == null) throw new ArgumentNullException("form");
            if (designSize.Width <= 0 || designSize.Height <= 0)
                throw new ArgumentException("Design size must be positive.", "designSize");

            this.form = form;
            this.designSize = designSize;
        }

        /// <summary>Scale factor currently in use (1.0 == the original 1500x800 layout).</summary>
        public float Scale
        {
            get { return float.IsNaN(appliedScale) ? 1f : appliedScale; }
        }

        /// <summary>
        /// Records the current layout as the reference design and starts tracking resizes.
        /// Call this once, after every control has been created and added.
        /// </summary>
        public void Capture()
        {
            if (captured) return;

            CaptureTree(form);
            captured = true;

            form.Resize += OnFormResize;
            form.FormClosed += OnFormClosed;

            Apply();
        }

        private void CaptureTree(Control parent)
        {
            foreach (Control c in parent.Controls)
            {
                // Docked/anchored children (e.g. the condition labels filling their panel)
                // position themselves, so only their font needs scaling.
                if (c.Dock == DockStyle.None)
                    originalBounds[c] = c.Bounds;

                if (c.Font != null)
                    originalFontSize[c] = c.Font.SizeInPoints;

                if (c.HasChildren)
                    CaptureTree(c);
            }
        }

        private void OnFormResize(object sender, EventArgs e)
        {
            Apply();
        }

        /// <summary>Recomputes the scale factor and repositions/resizes every control.</summary>
        public void Apply()
        {
            if (!captured) return;

            Size client = form.ClientSize;
            if (client.Width <= 0 || client.Height <= 0) return;

            float scaleX = (float)client.Width / designSize.Width;
            float scaleY = (float)client.Height / designSize.Height;
            float scale = Math.Min(scaleX, scaleY);

            if (scale <= 0f) return;

            // Skip redundant work when the scale hasn't actually changed
            // (e.g. a move event or a resize along the non-limiting axis).
            bool scaleChanged = float.IsNaN(appliedScale) || Math.Abs(scale - appliedScale) > 0.0005f;

            // Centre the scaled canvas inside the client area so the extra space
            // is shared evenly instead of piling up on one side.
            int offsetX = (int)Math.Round((client.Width - designSize.Width * scale) / 2f);
            int offsetY = (int)Math.Round((client.Height - designSize.Height * scale) / 2f);
            if (offsetX < 0) offsetX = 0;
            if (offsetY < 0) offsetY = 0;

            form.SuspendLayout();
            try
            {
                var staleFonts = scaleChanged ? new List<Font>(createdFonts) : null;
                if (scaleChanged) createdFonts.Clear();

                ApplyTree(form, scale, offsetX, offsetY, scaleChanged, true);

                appliedScale = scale;

                // Old fonts are only disposed after the new ones are assigned,
                // otherwise controls would briefly reference a disposed Font.
                if (staleFonts != null)
                    foreach (Font f in staleFonts) f.Dispose();
            }
            finally
            {
                form.ResumeLayout(true);
            }
        }

        private void ApplyTree(Control parent, float scale, int offsetX, int offsetY,
                               bool scaleChanged, bool isTopLevel)
        {
            foreach (Control c in parent.Controls)
            {
                Rectangle b;
                if (originalBounds.TryGetValue(c, out b))
                {
                    // Only direct children of the form get the centring offset;
                    // nested children are already positioned relative to their parent.
                    int x = (int)Math.Round(b.X * scale) + (isTopLevel ? offsetX : 0);
                    int y = (int)Math.Round(b.Y * scale) + (isTopLevel ? offsetY : 0);
                    int w = Math.Max(1, (int)Math.Round(b.Width * scale));
                    int h = Math.Max(1, (int)Math.Round(b.Height * scale));

                    var target = new Rectangle(x, y, w, h);
                    if (c.Bounds != target)
                        c.Bounds = target;
                }

                if (scaleChanged)
                {
                    float size;
                    if (originalFontSize.TryGetValue(c, out size) && c.Font != null)
                    {
                        float scaled = size * scale;
                        if (scaled < 1f) scaled = 1f;

                        Font newFont = new Font(c.Font.FontFamily, scaled, c.Font.Style);
                        createdFonts.Add(newFont);
                        c.Font = newFont;
                    }
                }

                if (c.HasChildren)
                    ApplyTree(c, scale, offsetX, offsetY, scaleChanged, false);

                c.Invalidate(); // repaint hand-drawn borders at the new size
            }
        }

        private void OnFormClosed(object sender, FormClosedEventArgs e)
        {
            foreach (Font f in createdFonts) f.Dispose();
            createdFonts.Clear();
        }

        /// <summary>
        /// Shrinks the window to fit the monitor's working area if the design size
        /// doesn't fit (e.g. a 1100x600 screen), and centres it.
        /// </summary>
        public static void FitToScreen(Form form, Size designSize)
        {
            Rectangle work = Screen.FromControl(form).WorkingArea;

            Size chrome = new Size(form.Width - form.ClientSize.Width,
                                   form.Height - form.ClientSize.Height);

            int width = Math.Min(designSize.Width + chrome.Width, work.Width);
            int height = Math.Min(designSize.Height + chrome.Height, work.Height);

            form.Size = new Size(width, height);
            form.Location = new Point(work.X + (work.Width - width) / 2,
                                      work.Y + (work.Height - height) / 2);
        }
    }
}