using System;
using System.Drawing;
using System.Windows.Forms;

namespace PLCCompare
{
    public class MainForm : Form
    {
        // Exposed as internal so FrameController (same project) can read/write them directly,
        // mirroring the "protected" access used in the original Java version.
        internal Label label1, label2, label3;
        internal Label clockLabel;
        internal PictureBox logo;
        internal Label dateLabel;
        internal Label received1, received2, received3, current1, current2, current3;
        internal TextBox text1, text2, text3, text4, text5, text6;
        internal Label match1, match2;
        internal Label condition1, condition2;
        internal Label scanning1, scanning2;
        internal Label count1, count2;
        internal Button reset;
        internal Label status;
        internal Label plcStatus;

        private System.Windows.Forms.Timer clockTimer;
        private static readonly Size DesignSize = new Size(1500, 800);
        private LayoutScaler scaler;

        public MainForm()
        {
            AutoScaleMode = AutoScaleMode.None;
            DoubleBuffered = true; 

            Icon = LoadEmbeddedIcon("AppIcon.ico");
            Text = "Wenglor Barcode Logging System";
            ClientSize = DesignSize;
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            MinimizeBox = true;

            SetLogo();
            SetLabel1();
            SetLabel2();
            SetLabel3();
            SetDateLabel();
            SetClockLabel();
            StartClock();

            SetReceived1();
            SetCurrent1();
            SetReceived2();
            SetCurrent2();
            SetReceived3();
            SetCurrent3();

            SetText1();
            SetText2();
            SetText3();
            SetText4();
            SetText5();
            SetText6();

            SetMatch1();
            SetMatch2();
            SetCondition1();
            SetCondition2();

            SetScanning1();
            SetScanning2();
            SetCount1();
            SetCount2();

            SetResetButton();
            SetStatus();
            SetPlcStatus();

            scaler = new LayoutScaler(this, DesignSize);
            scaler.Capture();
            Load += (s, e) => LayoutScaler.FitToScreen(this, DesignSize);
        }

        // App Icon
        private Icon LoadEmbeddedIcon(string fileName)
        {
            var assembly = System.Reflection.Assembly.GetExecutingAssembly();
            string resourceName = assembly.GetName().Name + "." + fileName;

            using (var stream = assembly.GetManifestResourceStream(resourceName))
            {
                if (stream == null)
                {
                    throw new Exception($"Embedded icon not found: {resourceName}");
                }
                return new Icon(stream);
            }
        }

        // Logo
        private void SetLogo()
        {
            logo = new PictureBox();
            logo.Image = LoadEmbeddedImage("IconWenglor.png");
            logo.SizeMode = PictureBoxSizeMode.StretchImage;
            logo.Bounds = new Rectangle(25, 5, 420, 80);
            Controls.Add(logo);
        }

        private Image LoadEmbeddedImage(string fileName)
        {
            var assembly = System.Reflection.Assembly.GetExecutingAssembly();

            // Embedded resource names are "<DefaultNamespace>.<FileName>" by default
            string resourceName = assembly.GetName().Name + "." + fileName;

            using (var stream = assembly.GetManifestResourceStream(resourceName))
            {
                if (stream == null)
                {
                    throw new Exception($"Embedded resource not found: {resourceName}. Check the exact resource name using assembly.GetManifestResourceNames().");
                }
                return Image.FromStream(stream);
            }
        }

        // Batch No
        private void SetLabel1()
        {
            label1 = new Label();
            label1.Text = "Batch No";
            label1.Font = new Font("Arial", 18, FontStyle.Bold);
            label1.ForeColor = Color.Black;
            label1.Bounds = new Rectangle(50, 175, 320, 33);
            Controls.Add(label1);
        }

        // Rank 1
        private void SetLabel2()
        {
            label2 = new Label();
            label2.Text = "Rank 1";
            label2.Font = new Font("Arial", 18, FontStyle.Bold);
            label2.ForeColor = Color.Black;
            label2.Bounds = new Rectangle(50, 393, 320, 33);
            Controls.Add(label2);
        }

        // Rank 2
        private void SetLabel3()
        {
            label3 = new Label();
            label3.Text = "Rank 2";
            label3.Font = new Font("Arial", 18, FontStyle.Bold);
            label3.ForeColor = Color.Black;
            label3.Bounds = new Rectangle(50, 599, 320, 32);
            Controls.Add(label3);
        }

        // Date
        private void SetDateLabel()
        {
            dateLabel = new Label();
            dateLabel.Font = new Font("Arial", 20, FontStyle.Bold);
            dateLabel.ForeColor = Color.Black;
            dateLabel.Bounds = new Rectangle(53, 140, 250, 50);
            Controls.Add(dateLabel);
            UpdateDate();
        }

        internal void UpdateDate()
        {
            dateLabel.Text = DateTime.Now.ToString("ddd, dd/MM/yyyy");
        }

        // Time
        private void SetClockLabel()
        {
            clockLabel = new Label();
            clockLabel.Font = new Font("Arial", 20, FontStyle.Bold);
            clockLabel.ForeColor = Color.Black;
            clockLabel.Bounds = new Rectangle(330, 140, 200, 50);
            Controls.Add(clockLabel);
            UpdateTime();
        }

        internal void UpdateTime()
        {
            clockLabel.Text = DateTime.Now.ToString("HH:mm:ss");
        }

        // Equivalent of javax.swing.Timer — WinForms' Timer already fires its Tick
        // event on the UI thread automatically, so no extra thread-safety wrapping is needed here.
        private void StartClock()
        {
            clockTimer = new System.Windows.Forms.Timer();
            clockTimer.Interval = 1000; // fires every 1000ms, same cadence as before
            clockTimer.Tick += (s, e) => UpdateTime();
            clockTimer.Start();
        }

        // Received Data 1
        private void SetReceived1()
        {
            received1 = new Label();
            received1.Text = "Received Data";
            received1.Font = new Font("Arial", 14, FontStyle.Underline);
            received1.Bounds = new Rectangle(50, 211, 300, 30);
            Controls.Add(received1);
        }

        // Current Data 1
        private void SetCurrent1()
        {
            current1 = new Label();
            current1.Text = "Current Data";
            current1.Font = new Font("Arial", 14, FontStyle.Underline);
            current1.Bounds = new Rectangle(50, 294, 300, 30);
            Controls.Add(current1);
        }

        // Received Data 2
        private void SetReceived2()
        {
            received2 = new Label();
            received2.Text = "Received Data";
            received2.Font = new Font("Arial", 14, FontStyle.Underline);
            received2.Bounds = new Rectangle(50, 425, 300, 30);
            Controls.Add(received2);
        }

        // Current Data 2
        private void SetCurrent2()
        {
            current2 = new Label();
            current2.Text = "Current Data";
            current2.Font = new Font("Arial", 14, FontStyle.Underline);
            current2.Bounds = new Rectangle(50, 508, 300, 30);
            Controls.Add(current2);
        }

        // Received Data 3
        private void SetReceived3()
        {
            received3 = new Label();
            received3.Text = "Received Data";
            received3.Font = new Font("Arial", 14, FontStyle.Underline);
            received3.Bounds = new Rectangle(50, 632, 300, 30);
            Controls.Add(received3);
        }

        // Current Data 3
        private void SetCurrent3()
        {
            current3 = new Label();
            current3.Text = "Current Data";
            current3.Font = new Font("Arial", 14, FontStyle.Underline);
            current3.Bounds = new Rectangle(50, 712, 300, 30);
            Controls.Add(current3);
        }

        // Shared helper — equivalent of your repeated setTextX() blocks.
        // Wraps the textbox in a frame that paints a black sunken bevel (dark top/left,
        // lighter bottom/right), since TextBox's own Fixed3D border can't be recolored.
        private TextBox MakeReadOnlyTextBox(Rectangle bounds)
        {
            var frame = new Panel();
            frame.Bounds = bounds;
            frame.BackColor = Color.White;
            frame.Padding = new Padding(2);
            frame.Paint += (s, e) =>
            {
                var rect = new Rectangle(0, 0, frame.Width - 1, frame.Height - 1);
                using (var darkPen = new Pen(Color.Black, 2))
                {
                    e.Graphics.DrawLine(darkPen, rect.Left, rect.Top, rect.Right, rect.Top);
                    e.Graphics.DrawLine(darkPen, rect.Left, rect.Top, rect.Left, rect.Bottom);
                }
                using (var lightPen = new Pen(Color.FromArgb(90, 90, 90), 1))
                {
                    e.Graphics.DrawLine(lightPen, rect.Left, rect.Bottom, rect.Right, rect.Bottom);
                    e.Graphics.DrawLine(lightPen, rect.Right, rect.Top, rect.Right, rect.Bottom);
                }
            };
            Controls.Add(frame);

            var tb = new TextBox();
            tb.Font = new Font("Arial", 20, FontStyle.Regular);
            tb.ReadOnly = true;
            tb.BorderStyle = BorderStyle.None;
            tb.Multiline = true;
            tb.Dock = DockStyle.Fill;
            frame.Controls.Add(tb);
            return tb;
        }

        private void SetText1() => text1 = MakeReadOnlyTextBox(new Rectangle(55, 243, 460, 45));
        private void SetText2() => text2 = MakeReadOnlyTextBox(new Rectangle(55, 325, 460, 45));
        private void SetText3() => text3 = MakeReadOnlyTextBox(new Rectangle(55, 458, 460, 45));
        private void SetText4() => text4 = MakeReadOnlyTextBox(new Rectangle(55, 540, 460, 45));
        private void SetText5() => text5 = MakeReadOnlyTextBox(new Rectangle(55, 663, 460, 45));
        private void SetText6() => text6 = MakeReadOnlyTextBox(new Rectangle(55, 745, 460, 45));

        // Match 1
        private void SetMatch1()
        {
            match1 = new Label();
            match1.Text = "Match";
            match1.Font = new Font("Arial", 24, FontStyle.Bold);
            match1.ForeColor = Color.Black;
            match1.Bounds = new Rectangle(940, 390, 320, 33);
            Controls.Add(match1);
        }

        // Match 2
        private void SetMatch2()
        {
            match2 = new Label();
            match2.Text = "Match";
            match2.Font = new Font("Arial", 24, FontStyle.Bold);
            match2.ForeColor = Color.Black;
            match2.Bounds = new Rectangle(940, 590, 320, 33);
            Controls.Add(match2);
        }

        // Condition 1
        private void SetCondition1() {
            var panel = new Panel();
            panel.Bounds = new Rectangle(945, 430, 100, 100);
            panel.Padding = new Padding(2); // matches the border thickness below
            panel.Paint += (s, e) =>
            {
                using (var pen = new Pen(Color.Black, 3))
                {
                    e.Graphics.DrawRectangle(pen, 1, 1, panel.Width - 3, panel.Height - 3);
                }
            };
            Controls.Add(panel);

            condition1 = new Label();
            condition1.Font = new Font("Arial", 32, FontStyle.Bold);
            condition1.TextAlign = ContentAlignment.MiddleCenter;
            condition1.BorderStyle = BorderStyle.None; // panel now draws the border instead
            condition1.BackColor = Color.White;
            
            panel.Controls.Add(condition1);
            condition1.Dock = DockStyle.Fill;
        }

        // Condition 2
        private void SetCondition2() {
            var panel = new Panel();
            panel.Bounds = new Rectangle(945, 630, 100, 100);
            panel.Padding = new Padding(2);
            panel.Paint += (s, e) =>
            {
                using (var pen = new Pen(Color.Black, 3))
                {
                    e.Graphics.DrawRectangle(pen, 1, 1, panel.Width - 3, panel.Height - 3);
                }
            };
            Controls.Add(panel);

            condition2 = new Label();
            condition2.Font = new Font("Arial", 32, FontStyle.Bold);
            condition2.TextAlign = ContentAlignment.MiddleCenter;
            condition2.BorderStyle = BorderStyle.None;
            condition2.BackColor = Color.White;
            
            panel.Controls.Add(condition2);
            condition2.Dock = DockStyle.Fill;
        }

        // Scanning 1
        private void SetScanning1()
        {
            scanning1 = new Label();
            scanning1.Text = "Rank 1 Counter";
            scanning1.Font = new Font("Arial", 24, FontStyle.Bold);
            scanning1.Bounds = new Rectangle(600, 10, 300, 35);
            Controls.Add(scanning1);
        }

        // Scanning 2
        private void SetScanning2()
        {
            scanning2 = new Label();
            scanning2.Text = "Rank 2 Counter";
            scanning2.Font = new Font("Arial", 24, FontStyle.Bold);
            scanning2.Bounds = new Rectangle(1080, 10, 300, 35);
            Controls.Add(scanning2);
        }

        // Count after compare Batch No and Rank 1
        private void SetCount1()
        {
            count1 = new Label();
            count1.Text = "0";
            count1.Font = new Font("Arial", 60, FontStyle.Bold);
            count1.Bounds = new Rectangle(550, 50, 350, 100);
            count1.TextAlign = ContentAlignment.MiddleCenter;
            Controls.Add(count1);
        }

        // Count after compare Batch No and Rank 2
        private void SetCount2()
        {
            count2 = new Label();
            count2.Text = "0";
            count2.Font = new Font("Arial", 60, FontStyle.Bold);
            count2.Bounds = new Rectangle(1035, 50, 350, 100);
            count2.TextAlign = ContentAlignment.MiddleCenter;
            Controls.Add(count2);
        }

        // Reset button — click handling is wired up in FrameController, not here,
        // to keep this class UI-only (same split as the original Java version).
        private void SetResetButton() {
            reset = new Button();
            reset.Text = "RESET";
            reset.Font = new Font("Arial", 16, FontStyle.Bold);
            reset.ForeColor = Color.Black;
            reset.BackColor = Color.White;
            reset.Bounds = new Rectangle(640, 170, 160, 50);
            reset.TextAlign = ContentAlignment.MiddleCenter;
            Controls.Add(reset);
        }

        // Status of COM port connection (open/failed)
        private void SetStatus()
        {
            status = new Label();
            status.Font = new Font("Arial", 12, FontStyle.Bold);
            status.Bounds = new Rectangle(53, 90, 400, 20);
            Controls.Add(status);
        }

        // Status of PLC connection
        private void SetPlcStatus()
        {
            plcStatus = new Label();
            plcStatus.Font = new Font("Arial", 12, FontStyle.Bold);
            plcStatus.Bounds = new Rectangle(53, 115, 400, 20);
            Controls.Add(plcStatus);
        }
    }
}