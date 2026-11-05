using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using Timer = System.Windows.Forms.Timer;

namespace MineCheck
{
    // ------------------------------------------------------------------ палитра
    internal static class Pal
    {
        public static Color Hex(string value) { return ColorTranslator.FromHtml(value); }

        public static readonly Color BgTop = Hex("#0A1018");
        public static readonly Color BgBottom = Hex("#04070B");
        public static readonly Color Card = Hex("#0C131B");
        public static readonly Color CardBorder = Hex("#1A2735");
        public static readonly Color Text = Hex("#E9F1F9");
        public static readonly Color Muted = Hex("#8396AA");
        public static readonly Color Dim = Hex("#4C5D70");
        public static readonly Color Stroke = Hex("#22303F");
        public static readonly Color Accent = Hex("#25E08C");
        public static readonly Color AccentHi = Hex("#46F3A4");
        public static readonly Color AccentLo = Hex("#12A968");
        public static readonly Color AccentInk = Hex("#04180E");
        public static readonly Color EntryBg = Hex("#070C12");
        public static readonly Color Danger = Hex("#FF5F6D");
        public static readonly Color Warn = Hex("#FFC857");
        public static readonly Color GhostHover = Hex("#111A24");
        public static readonly Color SoftText = Hex("#B7C6D6");
    }

    // --------------------------------------------------------------- графика
    internal static class Ui
    {
        public static GraphicsPath Round(RectangleF r, float radius)
        {
            float d = Math.Max(0f, Math.Min(radius * 2f, Math.Min(r.Width, r.Height)));
            GraphicsPath p = new GraphicsPath();
            if (d <= 0.5f)
            {
                p.AddRectangle(r);
                return p;
            }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void FillRound(Graphics g, RectangleF r, float radius, Color color)
        {
            if (color.A == 0) return;
            using (GraphicsPath p = Round(r, radius))
            using (SolidBrush b = new SolidBrush(color))
                g.FillPath(b, p);
        }

        public static void StrokeRound(Graphics g, RectangleF r, float radius, Color color, float width)
        {
            if (color.A == 0 || width <= 0f) return;
            using (GraphicsPath p = Round(r, radius))
            using (Pen pen = new Pen(color, width))
                g.DrawPath(pen, p);
        }

        public static void DrawGlow(Graphics g, RectangleF r, Color center)
        {
            using (GraphicsPath p = new GraphicsPath())
            {
                p.AddEllipse(r);
                using (PathGradientBrush b = new PathGradientBrush(p))
                {
                    b.CenterColor = center;
                    b.SurroundColors = new Color[] { Color.FromArgb(0, center.R, center.G, center.B) };
                    b.FocusScales = new PointF(0.15f, 0.15f);
                    g.FillEllipse(b, r);
                }
            }
        }
    }

    // ------------------------------------------------------------ кнопка
    internal sealed class UiButton : Control
    {
        public Color Fill = Color.Transparent;
        public Color HoverFill = Color.Transparent;
        public Color PressFill = Color.Transparent;
        public Color TextColor = Color.White;
        public Color HoverTextColor = Color.Empty;
        public Color BorderColor = Color.Transparent;
        public float BorderWidth = 1f;
        public int Radius = 10;

        private bool hover;
        private bool press;

        public UiButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor |
                     ControlStyles.ResizeRedraw, true);
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
            TabStop = false;
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; press = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { press = true; Invalidate(); } base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { press = false; Invalidate(); base.OnMouseUp(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            RectangleF r = new RectangleF(0f, 0f, Width, Height);
            Color fill = press ? PressFill : (hover ? HoverFill : Fill);
            if (fill.A > 0)
                Ui.FillRound(g, r, Radius, fill);

            if (BorderColor.A > 0)
            {
                Color bc = hover ? Color.FromArgb(Math.Min(255, BorderColor.A + 50), BorderColor) : BorderColor;
                Ui.StrokeRound(g, RectangleF.Inflate(r, -BorderWidth / 2f, -BorderWidth / 2f),
                               Radius - BorderWidth / 2f, bc, BorderWidth);
            }

            Color fg = hover && HoverTextColor != Color.Empty ? HoverTextColor : TextColor;
            using (SolidBrush b = new SolidBrush(fg))
            using (StringFormat sf = new StringFormat())
            {
                sf.Alignment = StringAlignment.Center;
                sf.LineAlignment = StringAlignment.Center;
                g.DrawString(Text, Font, b, r, sf);
            }
        }
    }

    // ------------------------------------------------------- хранилище кодов
    internal sealed class CodeItem
    {
        public string Code;
        public bool Used;
    }

    internal static class CodesStore
    {
        public const string FileName = "codes.dat";
        private const string LegacyFileName = "Коды для проверок.txt";
        private const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
        private const string UsedMark = "ИСПОЛЬЗОВАН";

        private static readonly Regex CodeRe =
            new Regex("[A-Z0-9]{4}(?:-[A-Z0-9]{4}){3}", RegexOptions.Compiled);

        public static string FilePath
        {
            get
            {
                string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (string.IsNullOrEmpty(local))
                    local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                                         "AppData", "Local");
                return Path.Combine(Path.Combine(local, "MineCheck"), FileName);
            }
        }

        private static string LegacyFilePath
        {
            get
            {
                string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                if (string.IsNullOrEmpty(desktop))
                    desktop = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Desktop");
                return Path.Combine(desktop, LegacyFileName);
            }
        }

        private static void Hide(string path)
        {
            try
            {
                FileAttributes attrs = File.GetAttributes(path);
                if ((attrs & FileAttributes.Hidden) == 0)
                    File.SetAttributes(path, attrs | FileAttributes.Hidden);
            }
            catch { }
        }

        private static void EnsureDirectory(string path)
        {
            try
            {
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
            }
            catch { }
        }

        /// <summary>
        /// Переносит старую базу с рабочего стола в защищённое хранилище приложения.
        /// Старый файл скрывается, чтобы его нельзя было случайно открыть.
        /// </summary>
        private static void MigrateLegacy(string path)
        {
            try
            {
                if (File.Exists(path)) return;
                string legacy = LegacyFilePath;
                if (!File.Exists(legacy)) return;

                EnsureDirectory(path);
                File.Copy(legacy, path, false);
                Hide(path);
                Hide(legacy);
            }
            catch { }
        }

        public static string Normalize(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "";
            StringBuilder body = new StringBuilder(16);
            string upper = raw.ToUpperInvariant();
            for (int i = 0; i < upper.Length && body.Length < 16; i++)
            {
                char ch = upper[i];
                if ((ch >= 'A' && ch <= 'Z') || (ch >= '0' && ch <= '9'))
                    body.Append(ch);
            }
            StringBuilder result = new StringBuilder(19);
            for (int i = 0; i < body.Length; i++)
            {
                if (i > 0 && i % 4 == 0) result.Append('-');
                result.Append(body[i]);
            }
            return result.ToString();
        }

        public static string NewCode()
        {
            byte[] bytes = new byte[16];
            using (RNGCryptoServiceProvider rng = new RNGCryptoServiceProvider())
                rng.GetBytes(bytes);

            StringBuilder sb = new StringBuilder(19);
            for (int i = 0; i < 16; i++)
            {
                if (i > 0 && i % 4 == 0) sb.Append('-');
                sb.Append(Alphabet[bytes[i] % Alphabet.Length]);
            }
            return sb.ToString();
        }

        public static void Generate(int count)
        {
            string path = FilePath;
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            HashSet<string> seen = new HashSet<string>();
            List<string> codes = new List<string>();
            while (codes.Count < count)
            {
                string code = NewCode();
                if (seen.Add(code)) codes.Add(code);
            }

            int digits = Math.Max(3, count.ToString().Length);
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("MINECHECK · одноразовые коды доступа");
            sb.AppendLine(string.Format("Создано: {0:dd.MM.yyyy HH:mm}   ·   Всего кодов: {1}",
                                        DateTime.Now, count));
            sb.AppendLine("Каждый код действует 1 раз: после успешной проверки появится отметка [ИСПОЛЬЗОВАН].");
            sb.AppendLine(new string('-', 68));
            sb.AppendLine();
            for (int i = 0; i < codes.Count; i++)
                sb.AppendLine((i + 1).ToString().PadLeft(digits, '0') + ") " + codes[i]);
            sb.AppendLine();

            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
            Hide(path);
        }

        public static List<CodeItem> Read()
        {
            List<CodeItem> result = new List<CodeItem>();
            string path = FilePath;
            if (!File.Exists(path)) return result;

            string[] lines;
            try { lines = File.ReadAllLines(path, Encoding.UTF8); }
            catch { return result; }

            foreach (string line in lines)
            {
                Match m = CodeRe.Match(line);
                if (!m.Success) continue;
                CodeItem item = new CodeItem();
                item.Code = m.Value;
                item.Used = line.IndexOf(UsedMark, StringComparison.Ordinal) >= 0;
                result.Add(item);
            }
            return result;
        }

        /// <summary>Проверяет и гасит код. Возвращает true, если код принят.</summary>
        public static bool Redeem(string raw, out string reason, out string code)
        {
            code = Normalize(raw);
            reason = "short";
            if (code.Length != 19) return false;

            string path = FilePath;
            if (!File.Exists(path)) { reason = "missing"; return false; }

            string[] lines;
            try { lines = File.ReadAllLines(path, Encoding.UTF8); }
            catch { reason = "locked"; return false; }

            int index = -1;
            bool used = false;
            for (int i = 0; i < lines.Length; i++)
            {
                Match m = CodeRe.Match(lines[i]);
                if (!m.Success || m.Value != code) continue;
                index = i;
                used = lines[i].IndexOf(UsedMark, StringComparison.Ordinal) >= 0;
                break;
            }

            if (index < 0) { reason = "notfound"; return false; }
            if (used) { reason = "used"; return false; }

            lines[index] = lines[index].TrimEnd() +
                           "   [" + UsedMark + " " + DateTime.Now.ToString("dd.MM.yyyy HH:mm") + "]";

            try
            {
                // Windows не даёт перезаписать скрытый файл — снимаем атрибут на время записи
                bool hidden = (File.GetAttributes(path) & FileAttributes.Hidden) != 0;
                if (hidden)
                    File.SetAttributes(path, File.GetAttributes(path) & ~FileAttributes.Hidden);

                File.WriteAllText(path, string.Join("\r\n", lines) + "\r\n", new UTF8Encoding(true));
                if (hidden) Hide(path);
            }
            catch
            {
                reason = "locked";
                return false;
            }

            reason = "ok";
            return true;
        }

        public static bool EnsureFile()
        {
            string path = FilePath;
            EnsureDirectory(path);
            MigrateLegacy(path);

            if (!File.Exists(path) || Read().Count == 0)
            {
                try { Generate(100); return true; }
                catch { return false; }
            }
            Hide(path);
            return false;
        }
    }

    // ------------------------------------------------------------- главное окно
    internal sealed class MainForm : Form
    {
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        private const int DesignWidth = 1000;
        private const int DesignHeight = 620;
        private const int DesignHeightScan = 782;

        private float scale = 1f;
        private float fontFactor = 1f;

        private RectangleF rCard;
        private RectangleF rEntry;
        private RectangleF rDivider;
        private int entryLeft;
        private int layoutHeight = DesignHeight;

        private TextBox codeBox;
        private UiButton btnStart;
        private UiButton btnNewCheck;
        private UiButton btnClose;
        private UiButton btnMin;
        private UiButton btnAbort;
        private UiButton btnReport;
        private UiButton btnOpenPath;
        private UiButton btnAdmin;

        private Font fBrand, fSub, fChip, fEyebrow, fH1, fDesc, fFeature, fCardLabel,
                     fMono, fBtn, fStatus, fRowNum, fRow, fStatCap, fStatVal, fStatSub,
                     fFoot, fLink, fWin, fSuccTitle, fSuccSub, fBig, fScanTitle;

        private int shakeX;

        private bool successMode;
        private bool entryFocus;
        private bool errorFlash;
        private bool placeholder;
        private bool formatting;

        private Timer animTimer;
        private Timer shakeTimer;
        private Timer fadeTimer;
        private Timer statusTimer;
        private int shakeStep;

        private string statusText = "";
        private Color statusColor = Pal.Dim;
        private int statTotal, statUsed, statRemaining;
        private string lastCode = "";

        // ------------------------------------------------------- состояние сканера
        private Scanner scanner;
        private Thread scanThread;
        private CancellationTokenSource scanCancel;
        private ScanReport scanReport;
        private readonly int[] stageState = new int[16];
        private readonly int[] stageFindings = new int[16];
        private readonly int[] stageCrit = new int[16];
        private readonly double[] stageProgress = new double[16];
        private readonly string[] stageNames = new string[]
        {
            "Процессы и окна", "Клиент Minecraft", "Версии с вшитыми читами",
            "Файлы и папки", "Недавние удаления", "Корзина",
            "Реестр", "Активация и команды", "Prefetch",
            "Быстрый доступ", "Браузеры", "USB и устройства",
            "Память процессов", "Java-процессы детально", "Инжекторы и DLL"
        };
        private int foundCritical, foundWarning, foundInfo;
        private Finding topFinding;
        private readonly List<Finding> recentFindings = new List<Finding>();
        private DateTime scanStarted;
        private string elapsedText = "00:00";
        private string scanPercent = "0%";
        private int scanStage;
        private Timer scanTimer;
        private string reportPath = "";
        private string lastLog = "";
        private bool scanDone;
        private bool scanAborted;
        private int liveFiles, liveProcesses;
        private bool adminMode;

        private static readonly StringFormat SfTypo = StringFormat.GenericTypographic;
        private static readonly StringFormat SfCenter = CreateCenterFormat();

        private static StringFormat CreateCenterFormat()
        {
            StringFormat sf = new StringFormat();
            sf.Alignment = StringAlignment.Center;
            sf.LineAlignment = StringAlignment.Center;
            return sf;
        }

        public MainForm()
        {
            Text = "MINECHECK — проверка на читы";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            BackColor = Pal.BgTop;
            AutoScaleMode = AutoScaleMode.None;
            KeyPreview = true;
            DoubleBuffered = true;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Opacity = 0d;
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ClassStyle |= 0x20000; // CS_DROPSHADOW
                return cp;
            }
        }

        private int S(float value) { return (int)Math.Round(value * scale); }
        private float Su(float value) { return value * scale; }

        private Font F(float points, FontStyle style)
        {
            return new Font("Segoe UI", points * fontFactor, style, GraphicsUnit.Point);
        }

        private Font FM(float points, FontStyle style)
        {
            return new Font("Consolas", points * fontFactor, style, GraphicsUnit.Point);
        }

        // ---------------------------------------------------------------- запуск
        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);

            InitializeMainForm();
        }

        private void InitializeMainForm()
        {
            float dpi;
            using (Graphics g = CreateGraphics()) dpi = g.DpiX / 96f;

            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            float fit = Math.Min(wa.Width * 0.96f / DesignWidth, wa.Height * 0.94f / DesignHeightScan);

            scale = Math.Min(dpi, fit);
            if (scale < 0.75f) scale = 0.75f;
            fontFactor = scale / dpi;

            BuildFonts();
            ApplyLayout();
            CreateControls();
            HookEvents();
            SetPlaceholder();

            bool created = CodesStore.EnsureFile();
            RefreshStats();

            adminMode = Program.IsAdmin();
            btnAdmin.Visible = !adminMode;

            if (created) SetStatus("Создано 100 новых одноразовых кодов", Pal.Accent);
            else SetStatus("Введите код, выданный проверяющим", Pal.Dim);

            Location = new Point(wa.X + (wa.Width - Width) / 2, wa.Y + (wa.Height - Height) / 2 + S(4));

            fadeTimer = new Timer();
            fadeTimer.Interval = 18;
            fadeTimer.Tick += delegate
            {
                Opacity = Math.Min(1d, Opacity + 0.16d);
                if (Opacity >= 1d) fadeTimer.Stop();
            };
            fadeTimer.Start();
        }

        private void BuildFonts()
        {
            fBrand = F(12.5f, FontStyle.Bold);
            fSub = F(7.6f, FontStyle.Bold);
            fChip = F(7.2f, FontStyle.Bold);
            fEyebrow = F(7.4f, FontStyle.Bold);
            fH1 = F(22f, FontStyle.Bold);
            fDesc = F(9.4f, FontStyle.Regular);
            fFeature = F(9.2f, FontStyle.Regular);
            fCardLabel = F(7.4f, FontStyle.Bold);
            fMono = FM(16f, FontStyle.Regular);
            fBtn = F(10f, FontStyle.Bold);
            fStatus = F(8.6f, FontStyle.Regular);
            fRowNum = FM(7f, FontStyle.Bold);
            fRow = F(9f, FontStyle.Regular);
            fStatCap = F(7f, FontStyle.Bold);
            fStatVal = F(20f, FontStyle.Bold);
            fStatSub = F(7.6f, FontStyle.Regular);
            fFoot = F(7.8f, FontStyle.Regular);
            fLink = F(7.8f, FontStyle.Bold);
            fWin = F(9f, FontStyle.Regular);
            fSuccTitle = F(14f, FontStyle.Bold);
            fSuccSub = F(8.4f, FontStyle.Regular);
            fBig = F(34f, FontStyle.Bold);
            fScanTitle = F(10.5f, FontStyle.Bold);
        }

        private void ApplyLayout()
        {
            ClientSize = new Size(S(DesignWidth), S(layoutHeight));
            rCard = new RectangleF(S(540), S(96), S(430), S(layoutHeight - 164));
            rEntry = new RectangleF(rCard.X + S(22), rCard.Y + S(46), rCard.Width - S(44), S(58));
            rDivider = new RectangleF(rCard.X + S(22), rCard.Y + S(316), rCard.Width - S(44), 1f);
            entryLeft = (int)rEntry.X + S(14);

            if (codeBox != null)
            {
                codeBox.Width = (int)rEntry.Width - S(28);
                codeBox.Left = entryLeft;
                codeBox.Top = (int)(rEntry.Y + (rEntry.Height - codeBox.Height) / 2f) - S(1);
            }
        }

        private void CreateControls()
        {
            codeBox = new TextBox();
            codeBox.BorderStyle = BorderStyle.None;
            codeBox.BackColor = Pal.EntryBg;
            codeBox.ForeColor = Pal.Text;
            codeBox.Font = fMono;
            codeBox.TextAlign = HorizontalAlignment.Center;
            codeBox.MaxLength = 19;
            codeBox.ShortcutsEnabled = true;
            codeBox.TabStop = false;
            codeBox.Width = (int)rEntry.Width - S(28);
            codeBox.Height = codeBox.PreferredHeight;
            codeBox.Left = entryLeft;
            codeBox.Top = (int)(rEntry.Y + (rEntry.Height - codeBox.Height) / 2f) - S(1);
            Controls.Add(codeBox);

            btnStart = new UiButton();
            btnStart.SetBounds(S(562), S(210), S(386), S(52));
            btnStart.Text = "НАЧАТЬ ПРОВЕРКУ";
            btnStart.Font = fBtn;
            btnStart.Fill = Pal.Accent;
            btnStart.HoverFill = Pal.AccentHi;
            btnStart.PressFill = Pal.AccentLo;
            btnStart.TextColor = Pal.AccentInk;
            btnStart.Radius = S(12);
            btnStart.Click += delegate { Submit(); };
            Controls.Add(btnStart);

            btnNewCheck = new UiButton();
            btnNewCheck.Text = "НОВАЯ ПРОВЕРКА";
            btnNewCheck.Font = fLink;
            btnNewCheck.TextColor = Pal.SoftText;
            btnNewCheck.HoverFill = Pal.GhostHover;
            btnNewCheck.PressFill = Pal.GhostHover;
            btnNewCheck.BorderColor = Pal.Stroke;
            btnNewCheck.BorderWidth = 1f;
            btnNewCheck.Radius = S(10);
            btnNewCheck.Visible = false;
            btnNewCheck.Click += delegate { ResetCheck(); };
            Controls.Add(btnNewCheck);

            btnAbort = new UiButton();
            btnAbort.Text = "ОСТАНОВИТЬ ПРОВЕРКУ";
            btnAbort.Font = fLink;
            btnAbort.TextColor = Pal.Danger;
            btnAbort.HoverFill = Color.FromArgb(28, Pal.Danger);
            btnAbort.PressFill = Color.FromArgb(46, Pal.Danger);
            btnAbort.HoverTextColor = Color.White;
            btnAbort.BorderColor = Color.FromArgb(90, Pal.Danger);
            btnAbort.BorderWidth = 1f;
            btnAbort.Radius = S(10);
            btnAbort.Visible = false;
            btnAbort.Click += delegate { AbortScan(); };
            Controls.Add(btnAbort);

            btnReport = new UiButton();
            btnReport.Text = "ОТКРЫТЬ ОТЧЁТ";
            btnReport.Font = fLink;
            btnReport.TextColor = Pal.SoftText;
            btnReport.HoverFill = Pal.GhostHover;
            btnReport.PressFill = Pal.GhostHover;
            btnReport.BorderColor = Pal.Stroke;
            btnReport.BorderWidth = 1f;
            btnReport.Radius = S(10);
            btnReport.Visible = false;
            btnReport.Click += delegate { OpenReport(); };
            Controls.Add(btnReport);

            btnOpenPath = new UiButton();
            btnOpenPath.Text = "ОТКРЫТЬ ЧИТ";
            btnOpenPath.Font = fLink;
            btnOpenPath.Fill = Pal.Danger;
            btnOpenPath.HoverFill = Color.FromArgb(255, 232, 92, 92);
            btnOpenPath.PressFill = Color.FromArgb(255, 176, 52, 52);
            btnOpenPath.TextColor = Color.White;
            btnOpenPath.HoverTextColor = Color.White;
            btnOpenPath.Radius = S(10);
            btnOpenPath.Visible = false;
            btnOpenPath.Click += delegate
            {
                if (topFinding != null) OpenFindingPath(topFinding.Path);
            };
            Controls.Add(btnOpenPath);

            btnAdmin = new UiButton();
            btnAdmin.SetBounds(S(DesignWidth - 30 - 196), S(DesignHeight - 54), S(196), S(30));
            btnAdmin.Text = "ЗАПУСТИТЬ ОТ АДМИНИСТРАТОРА";
            btnAdmin.Font = fLink;
            btnAdmin.TextColor = Pal.Muted;
            btnAdmin.HoverFill = Pal.GhostHover;
            btnAdmin.PressFill = Pal.GhostHover;
            btnAdmin.HoverTextColor = Pal.Text;
            btnAdmin.BorderColor = Pal.Stroke;
            btnAdmin.BorderWidth = 1f;
            btnAdmin.Radius = S(8);
            btnAdmin.Visible = false;
            btnAdmin.Click += delegate { RelaunchAsAdmin(); };
            Controls.Add(btnAdmin);

            btnClose = new UiButton();
            btnClose.SetBounds(S(942), S(28), S(28), S(28));
            btnClose.Text = "✕";
            btnClose.Font = fWin;
            btnClose.TextColor = Pal.Muted;
            btnClose.HoverFill = Pal.Danger;
            btnClose.HoverTextColor = Color.White;
            btnClose.Radius = S(8);
            btnClose.Click += delegate { Application.Exit(); };
            Controls.Add(btnClose);

            btnMin = new UiButton();
            btnMin.SetBounds(S(908), S(28), S(28), S(28));
            btnMin.Text = "–";
            btnMin.Font = fWin;
            btnMin.TextColor = Pal.Muted;
            btnMin.HoverFill = Pal.GhostHover;
            btnMin.HoverTextColor = Pal.Text;
            btnMin.Radius = S(8);
            btnMin.Click += delegate { WindowState = FormWindowState.Minimized; };
            Controls.Add(btnMin);

            animTimer = new Timer();
            animTimer.Interval = 16;
            animTimer.Tick += delegate { if (successMode) Invalidate(); };

            scanTimer = new Timer();
            scanTimer.Interval = 250;
            scanTimer.Tick += delegate
            {
                if (!successMode) return;
                TimeSpan span = DateTime.Now - scanStarted;
                elapsedText = ((int)span.TotalMinutes).ToString("00") + ":" + span.Seconds.ToString("00");

                double done = 0;
                for (int i = 0; i < stageState.Length; i++)
                {
                    if (stageState[i] == 2) done += 1;
                    else if (stageState[i] == 1) done += stageProgress[i];
                }
                scanPercent = ((int)Math.Round(done / stageState.Length * 100)) + "%";
                Invalidate();
            };

            shakeTimer = new Timer();
            shakeTimer.Interval = 36;
            shakeTimer.Tick += delegate
            {
                int[] steps = new int[] { -9, 7, -5, 3, -2, 0 };
                shakeStep++;
                if (shakeStep >= steps.Length)
                {
                    shakeTimer.Stop();
                    shakeStep = 0;
                    ApplyShake(0);
                    return;
                }
                ApplyShake(S(steps[shakeStep]));
            };

            statusTimer = new Timer();
            statusTimer.Interval = 3200;
            statusTimer.Tick += delegate
            {
                statusTimer.Stop();
                if (!successMode && !errorFlash)
                    SetStatus("Введите код, выданный проверяющим", Pal.Dim);
            };
        }

        private void HookEvents()
        {
            codeBox.Enter += delegate
            {
                entryFocus = true;
                if (placeholder) ClearPlaceholder();
                Invalidate();
            };
            codeBox.Leave += delegate
            {
                entryFocus = false;
                if (codeBox.TextLength == 0) SetPlaceholder();
                Invalidate();
            };
            codeBox.TextChanged += delegate { OnCodeTextChanged(); };
            codeBox.KeyDown += delegate(object s, KeyEventArgs ev)
            {
                if (ev.KeyCode == Keys.Enter)
                {
                    ev.SuppressKeyPress = true;
                    Submit();
                }
            };

            KeyDown += delegate(object s, KeyEventArgs ev)
            {
                if (ev.KeyCode == Keys.Escape)
                {
                    if (successMode)
                    {
                        if (scanDone) ResetCheck();
                    }
                    else Application.Exit();
                }
                else if (ev.KeyCode == Keys.Enter && !successMode)
                {
                    Submit();
                }
            };

            MouseDown += delegate(object s, MouseEventArgs ev)
            {
                if (ev.Y < S(78) && ev.Button == MouseButtons.Left)
                {
                    ReleaseCapture();
                    SendMessage(Handle, 0xA1, (IntPtr)0x2, IntPtr.Zero);
                }
            };

            KeyPress += delegate(object s, KeyPressEventArgs ev)
            {
                if (successMode || !placeholder) return;
                if (char.IsControl(ev.KeyChar) || ev.KeyChar == ' ') return;

                ClearPlaceholder();
                codeBox.Focus();
                codeBox.Text = ev.KeyChar.ToString();
                codeBox.SelectionStart = codeBox.Text.Length;
                ev.Handled = true;
            };
        }

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            ActiveControl = null;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try
            {
                int round = 2;
                DwmSetWindowAttribute(Handle, 33, ref round, sizeof(int));
            }
            catch
            {
                using (GraphicsPath p = Ui.Round(new RectangleF(0, 0, Width, Height), S(14)))
                    Region = new Region(p);
            }
        }

        // ------------------------------------------------------------------ ввод
        private void SetPlaceholder()
        {
            placeholder = true;
            formatting = true;
            codeBox.ForeColor = Pal.Dim;
            codeBox.Text = PlaceholderText;
            codeBox.SelectionStart = 0;
            formatting = false;
        }

        private void ClearPlaceholder()
        {
            placeholder = false;
            formatting = true;
            codeBox.Text = "";
            codeBox.ForeColor = Pal.Text;
            formatting = false;
        }

        private const string PlaceholderText = "XXXX-XXXX-XXXX-XXXX";

        private void OnCodeTextChanged()
        {
            if (formatting) return;

            if (placeholder)
            {
                if (codeBox.Text == PlaceholderText) return;
                placeholder = false;
                codeBox.ForeColor = Pal.Text;
            }

            if (errorFlash)
            {
                errorFlash = false;
                if (statusColor == Pal.Danger) SetStatus("", Pal.Dim);
                Invalidate();
            }

            string current = codeBox.Text;
            string normalized = CodesStore.Normalize(current);
            if (normalized != current)
            {
                formatting = true;
                codeBox.Text = normalized;
                codeBox.SelectionStart = normalized.Length;
                formatting = false;
            }
        }

        private void Submit()
        {
            if (successMode) return;

            if (placeholder || codeBox.TextLength == 0)
            {
                Fail("Введите одноразовый код", false);
                return;
            }

            string reason;
            string code;
            if (CodesStore.Redeem(codeBox.Text, out reason, out code))
            {
                StartScan(code);
                return;
            }

            if (reason == "short") Fail("Код неполный — нужно 16 символов", true);
            else if (reason == "used") Fail("Этот код уже был использован", true);
            else if (reason == "locked") Fail("Хранилище кодов занято — повторите через пару секунд", true);
            else if (reason == "missing") Fail("База кодов не найдена — перезапустите программу", false);
            else Fail("Код не найден — проверьте ввод", true);
        }

        private void Fail(string message, bool shake)
        {
            errorFlash = true;
            statusTimer.Stop();
            SetStatus(message, Pal.Danger);
            if (shake)
            {
                shakeStep = 0;
                shakeTimer.Start();
            }
            Invalidate();
        }

        private void ApplyShake(int offset)
        {
            shakeX = offset;
            codeBox.Left = entryLeft + offset;
            Rectangle r = Rectangle.FromLTRB((int)rEntry.X - S(14), (int)rEntry.Y - S(10),
                                            (int)rEntry.Right + S(14), (int)rEntry.Bottom + S(10));
            Invalidate(r);
        }

        private void SetStatus(string text, Color color)
        {
            statusText = text;
            statusColor = color;
            Invalidate(new Rectangle((int)rCard.X, (int)(rCard.Y + S(170)),
                                     (int)rCard.Width, S(30)));
        }

        // ------------------------------------------------------------ сканирование
        private void StartScan(string code)
        {
            successMode = true;
            scanDone = false;
            scanAborted = false;
            lastCode = code;
            scanStarted = DateTime.Now;
            elapsedText = "00:00";
            scanPercent = "0%";
            scanStage = 0;
            foundCritical = 0;
            foundWarning = 0;
            foundInfo = 0;
            liveFiles = 0;
            liveProcesses = 0;
            lastLog = "";
            recentFindings.Clear();
            topFinding = null;
            reportPath = "";

            for (int i = 0; i < stageState.Length; i++)
            {
                stageState[i] = 0;
                stageFindings[i] = 0;
                stageProgress[i] = 0;
                stageCrit[i] = 0;
            }

            codeBox.Visible = false;
            btnStart.Visible = false;
            btnAdmin.Visible = false;
            btnReport.Visible = false;
            btnOpenPath.Visible = false;
            statusText = "";
            errorFlash = false;
            shakeTimer.Stop();
            ApplyShake(0);
            RefreshStats();

            SwitchLayout(DesignHeightScan);
            LayoutScanButtons();
            btnAbort.Text = "ОСТАНОВИТЬ ПРОВЕРКУ";
            btnAbort.Visible = true;

            scanner = new Scanner();
            scanner.StageStarted += OnStageStarted;
            scanner.StageProgress += OnStageProgress;
            scanner.StageFinished += OnStageFinished;
            scanner.Found += OnFound;
            scanner.Log += OnScanLog;

            scanCancel = new CancellationTokenSource();
            CancellationToken token = scanCancel.Token;

            scanTimer.Start();
            animTimer.Start();
            Invalidate();

            scanThread = new Thread(delegate()
            {
                ScanReport result = null;
                try
                {
                    result = scanner.Run(code, token);
                }
                catch (Exception ex)
                {
                    result = new ScanReport();
                    result.Code = code;
                    result.Cancelled = true;
                    result.Duration = DateTime.Now - scanStarted;
                    OnUi(delegate { SetStatus("Сканирование прервано: " + ex.Message, Pal.Danger); });
                }
                ScanReport captured = result;
                OnUi(delegate { FinishScan(captured); });
            });
            scanThread.IsBackground = true;
            scanThread.Start();
        }

        private void OnUi(MethodInvoker action)
        {
            try
            {
                if (IsDisposed || !IsHandleCreated) return;
                BeginInvoke(action);
            }
            catch { }
        }

        private void OnStageStarted(int index, string name)
        {
            OnUi(delegate
            {
                if (index < 0 || index >= stageState.Length) return;
                scanStage = index;
                if (index < stageNames.Length && !string.IsNullOrEmpty(name)) stageNames[index] = name;
                stageState[index] = 1;
                stageProgress[index] = 0;
            });
        }

        private void OnStageProgress(int index, double value)
        {
            OnUi(delegate
            {
                if (index < 0 || index >= stageState.Length) return;
                if (stageState[index] != 1) return;
                stageProgress[index] = value;
            });
        }

        private void OnStageFinished(int index, int count)
        {
            OnUi(delegate
            {
                if (index < 0 || index >= stageState.Length) return;
                stageState[index] = 2;
                stageFindings[index] = count;
                stageProgress[index] = 1;
                scanStage = index + 1;
            });
        }

        private void OnFound(Finding finding)
        {
            OnUi(delegate
            {
                if (finding.Level == Severity.Critical)
                {
                    foundCritical++;
                    if (scanStage >= 0 && scanStage < stageCrit.Length) stageCrit[scanStage]++;
                }
                else if (finding.Level == Severity.Warning) foundWarning++;
                else foundInfo++;

                recentFindings.Insert(0, finding);
                if (recentFindings.Count > 60) recentFindings.RemoveAt(recentFindings.Count - 1);

                if (finding.Confirmed && finding.Level == Severity.Critical && HasOpenablePath(finding.Path))
                {
                    if (topFinding == null || finding.Score >= topFinding.Score) topFinding = finding;
                }
            });
        }

        private void OnScanLog(string message)
        {
            OnUi(delegate { lastLog = message ?? ""; });
        }

        private void FinishScan(ScanReport report)
        {
            if (!successMode) return;

            scanReport = report;
            scanDone = true;
            scanTimer.Stop();
            animTimer.Stop();
            btnAbort.Visible = false;
            btnAbort.Text = "ОСТАНОВИТЬ ПРОВЕРКУ";

            if (report != null)
            {
                liveFiles = report.FilesScanned;
                liveProcesses = report.ProcessesScanned;
                scanAborted = report.Cancelled;

                TimeSpan span = report.Duration;
                elapsedText = ((int)span.TotalMinutes).ToString("00") + ":" + span.Seconds.ToString("00");

                // повторная проверка могла изменить уровень находки — пересчитываем итог
                foundCritical = 0;
                foundWarning = 0;
                foundInfo = 0;
                topFinding = null;
                recentFindings.Clear();

                for (int i = 0; i < report.Findings.Count; i++)
                {
                    Finding f = report.Findings[i];
                    if (f.Level == Severity.Critical) foundCritical++;
                    else if (f.Level == Severity.Warning) foundWarning++;
                    else foundInfo++;

                    if (f.Confirmed && f.Level == Severity.Critical && HasOpenablePath(f.Path))
                    {
                        if (topFinding == null || f.Score >= topFinding.Score) topFinding = f;
                    }
                }

                for (int level = 0; level <= 2; level++)
                {
                    Severity want = (Severity)level;
                    for (int i = report.Findings.Count - 1; i >= 0 && recentFindings.Count < 60; i--)
                        if (report.Findings[i].Level == want) recentFindings.Add(report.Findings[i]);
                }
            }

            if (!scanAborted)
            {
                for (int i = 0; i < stageState.Length; i++)
                    if (stageState[i] != 2) stageState[i] = 2;
                scanPercent = "100%";
            }

            reportPath = SaveReport(report);

            btnReport.Visible = true;
            btnNewCheck.Visible = true;
            btnOpenPath.Visible = topFinding != null;
            LayoutScanButtons();
            Invalidate();
        }

        private void AbortScan()
        {
            if (!successMode || scanDone) return;

            if (scanCancel != null)
            {
                try { scanCancel.Cancel(); } catch { }
            }
            btnAbort.Text = "ОСТАНОВКА…";
        }

        private string SaveReport(ScanReport report)
        {
            try
            {
                string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                if (string.IsNullOrEmpty(desktop))
                    desktop = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

                string path = Path.Combine(desktop,
                    "MINECHECK отчёт " + DateTime.Now.ToString("dd.MM.yyyy HH-mm") + ".txt");

                StringBuilder sb = new StringBuilder();
                sb.AppendLine("MINECHECK · ОТЧЁТ О ПРОВЕРКЕ");
                sb.AppendLine(new string('=', 64));
                sb.AppendLine("Код: " + lastCode);
                sb.AppendLine("Начало: " + scanStarted.ToString("dd.MM.yyyy HH:mm:ss"));
                if (report != null)
                    sb.AppendLine("Длительность: " + ((int)report.Duration.TotalMinutes).ToString("00") + ":" +
                                  report.Duration.Seconds.ToString("00"));
                sb.AppendLine("Режим: " + (adminMode ? "администратор" : "ограниченный (без прав администратора)"));
                if (report != null)
                {
                    sb.AppendLine("Проверено процессов: " + report.ProcessesScanned);
                    sb.AppendLine("Просмотрено файлов: " + report.FilesScanned);
                    sb.AppendLine("Проверено JAR-файлов: " + report.JarsScanned);
                    sb.AppendLine("Сессия сканирования: с " + report.SessionStart.ToString("dd.MM.yyyy HH:mm:ss"));
                }
                sb.AppendLine("Найдено: критичных " + foundCritical +
                              " · предупреждений " + foundWarning +
                              " · инфо " + foundInfo);
                if (topFinding != null)
                {
                    sb.AppendLine();
                    sb.AppendLine("--- ГЛАВНАЯ НАХОДКА (ЧИТ) ---");
                    sb.AppendLine("Софт: " + CheatName(topFinding));
                    if (topFinding.Detail.Length > 0) sb.AppendLine("Признаки: " + topFinding.Detail);
                    sb.AppendLine("Путь: " + topFinding.Path);
                    sb.AppendLine("Статус: подтверждено повторной проверкой");
                }
                if (report != null && report.Cancelled)
                    sb.AppendLine("Проверка была остановлена до завершения.");
                sb.AppendLine();

                List<Finding> findings = report != null ? report.Findings : new List<Finding>();
                AppendFindings(sb, findings, Severity.Critical, "КРИТИЧНЫЕ");
                AppendFindings(sb, findings, Severity.Warning, "ПРЕДУПРЕЖДЕНИЯ");
                AppendFindings(sb, findings, Severity.Info, "ИНФОРМАЦИЯ");

                if (findings.Count == 0)
                    sb.AppendLine("Признаков читерского ПО не обнаружено.");

                sb.AppendLine();
                sb.AppendLine("Файл создан автоматически программой MINECHECK.");

                File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
                return path;
            }
            catch { return ""; }
        }

        private static void AppendFindings(StringBuilder sb, List<Finding> findings, Severity level, string header)
        {
            int count = 0;
            foreach (Finding f in findings) if (f.Level == level) count++;
            if (count == 0) return;

            sb.AppendLine("--- " + header + " (" + count + ") ---");
            int index = 0;
            foreach (Finding f in findings)
            {
                if (f.Level != level) continue;
                index++;
                sb.AppendLine(index.ToString() + ". [" + f.Category + "] " + f.Title);
                if (f.Detail.Length > 0) sb.AppendLine("   " + f.Detail);
                if (f.Path.Length > 0)
                {
                    // Добавляем путь как кликабельную ссылку
                    sb.AppendLine("   Путь: " + f.Path);
                }
                sb.AppendLine();
            }
        }

        private void OpenReport()
        {
            try
            {
                string path = reportPath;
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) path = SaveReport(scanReport);
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;

                Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
            }
            catch { }
        }

        private static bool HasOpenablePath(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            try
            {
                if (Directory.Exists(path)) return true;
                if (!File.Exists(path)) return false;

                string name = Path.GetFileName(path).ToLowerInvariant();
                if (name == "java.exe" || name == "javaw.exe" || name == "svchost.exe" ||
                    name == "explorer.exe" || name == "cmd.exe" || name == "powershell.exe" ||
                    name == "rundll32.exe" || name == "conhost.exe")
                    return false;

                string ext = Path.GetExtension(path).ToLowerInvariant();
                return ext == ".jar" || ext == ".exe" || ext == ".dll" ||
                       ext == ".bat" || ext == ".cmd" || ext == ".ps1" ||
                       ext == ".zip" || ext == ".rar" || ext == ".7z";
            }
            catch { return false; }
        }

        private void OpenFindingPath(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path)) return;

                if (File.Exists(path))
                {
                    // Открываем папку и выделяем сам файл чита
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = "/select, \"" + path + "\"",
                        UseShellExecute = true
                    });
                }
                else if (Directory.Exists(path))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = "\"" + path + "\"",
                        UseShellExecute = true
                    });
                }
                else if (path.StartsWith("PID"))
                {
                    SetStatus("Процесс " + path.Substring(4) + " — подробности в отчёте", Pal.Accent);
                }
                else if (path.StartsWith("HKLM") || path.StartsWith("HKCU"))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "regedit.exe",
                        UseShellExecute = true
                    });
                }
            }
            catch
            {
                SetStatus("Не удалось открыть папку с файлом", Pal.Warn);
            }
        }

        private void ResetCheck()
        {
            if (scanCancel != null)
            {
                try { scanCancel.Cancel(); } catch { }
                scanCancel = null;
            }
            scanTimer.Stop();
            animTimer.Stop();
            successMode = false;
            scanDone = false;
            scanAborted = false;
            scanThread = null;
            scanner = null;
            scanReport = null;
            reportPath = "";
            lastLog = "";
            topFinding = null;
            recentFindings.Clear();

            btnNewCheck.Visible = false;
            btnAbort.Visible = false;
            btnAbort.Text = "ОСТАНОВИТЬ ПРОВЕРКУ";
            btnReport.Visible = false;
            btnOpenPath.Visible = false;
            btnStart.Visible = true;
            codeBox.Visible = true;
            btnAdmin.Visible = !adminMode;

            SwitchLayout(DesignHeight);
            ClearPlaceholder();
            SetStatus("Введите код, выданный проверяющим", Pal.Dim);

            if (!Visible) return;
            BeginInvoke(new MethodInvoker(delegate
            {
                if (!IsDisposed) codeBox.Focus();
            }));
            Invalidate();
        }

        private void SwitchLayout(int height)
        {
            if (layoutHeight == height) return;

            Rectangle wa = Screen.FromControl(this).WorkingArea;
            layoutHeight = height;
            ApplyLayout();

            if (Top + Height > wa.Bottom - S(4)) Top = wa.Bottom - Height - S(4);
            if (Top < wa.Top) Top = wa.Top;
            Left = wa.X + (wa.Width - Width) / 2;
        }

        private void LayoutScanButtons()
        {
            int y = (int)rCard.Bottom - S(58);
            int x = (int)rCard.X + S(22);
            int full = (int)rCard.Width - S(44);
            int half = (full - S(12)) / 2;

            btnAbort.SetBounds(x, y, full, S(40));
            btnNewCheck.SetBounds(x, y, half, S(40));
            btnReport.SetBounds(x + half + S(12), y, half, S(40));
            btnOpenPath.SetBounds(x, y - S(52), full, S(40));
        }

        private void RelaunchAsAdmin()
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(Application.ExecutablePath);
                psi.Verb = "runas";
                psi.UseShellExecute = true;
                Program.ReleaseInstance();
                Process.Start(psi);
                Application.Exit();
            }
            catch
            {
                SetStatus("Запуск от администратора отменён", Pal.Danger);
            }
        }

        // ------------------------------------------------------------------ коды
        private void RefreshStats()
        {
            List<CodeItem> items = CodesStore.Read();
            int used = 0;
            foreach (CodeItem item in items) if (item.Used) used++;
            statTotal = items.Count;
            statUsed = used;
            statRemaining = items.Count - used;
        }

        // --------------------------------------------------------------- отрисовка
        protected override void OnPaintBackground(PaintEventArgs e)
        {
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            DrawBackground(g);
            DrawHeader(g);
            DrawHero(g);
            DrawCard(g);
            DrawFooter(g);
        }

        private void DrawBackground(Graphics g)
        {
            Rectangle full = new Rectangle(0, 0, ClientSize.Width, ClientSize.Height);
            using (LinearGradientBrush lg = new LinearGradientBrush(full, Pal.BgTop, Pal.BgBottom, 90f))
                g.FillRectangle(lg, full);

            int step = S(46);
            using (Pen pen = new Pen(Color.FromArgb(11, 140, 180, 220), 1f))
            {
                for (int x = step; x < ClientSize.Width; x += step)
                    g.DrawLine(pen, x, 0, x, ClientSize.Height);
                for (int y = step; y < ClientSize.Height; y += step)
                    g.DrawLine(pen, 0, y, ClientSize.Width, y);
            }

            Ui.DrawGlow(g, new RectangleF(S(380), S(-360), S(1040), S(1040)), Color.FromArgb(40, 37, 224, 140));
            Ui.DrawGlow(g, new RectangleF(S(-320), S(180), S(820), S(820)), Color.FromArgb(30, 56, 132, 214));
        }

        private void DrawHeader(Graphics g)
        {
            RectangleF logo = new RectangleF(S(30), S(22), S(40), S(40));
            using (GraphicsPath p = Ui.Round(logo, S(11)))
            using (LinearGradientBrush lg = new LinearGradientBrush(logo, Pal.AccentHi, Pal.AccentLo, 100f))
                g.FillPath(lg, p);

            using (Pen pen = new Pen(Pal.AccentInk, Su(3.1f)))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                pen.LineJoin = LineJoin.Round;
                PointF[] pts = new PointF[]
                {
                    new PointF(logo.X + S(11), logo.Y + S(21)),
                    new PointF(logo.X + S(18), logo.Y + S(28)),
                    new PointF(logo.X + S(30), logo.Y + S(13))
                };
                g.DrawLines(pen, pts);
            }

            DrawSpaced(g, "MINECHECK", fBrand, Pal.Text, S(84), S(23), Su(1.4f));
            DrawSpaced(g, "ПРОВЕРКА НА ЧИТЫ · SCREENSHARE", fSub, Pal.Muted, S(85), S(47), Su(1.1f));

            DrawChip(g);
        }

        private void DrawChip(Graphics g)
        {
            string text;
            Color accent;
            if (!successMode) { text = "СИСТЕМА ГОТОВА"; accent = Pal.Accent; }
            else if (!scanDone) { text = "ИДЁТ ПРОВЕРКА"; accent = Pal.AccentHi; }
            else if (scanAborted) { text = "ОСТАНОВЛЕНО"; accent = Pal.Warn; }
            else { text = "ПРОВЕРКА ЗАВЕРШЕНА"; accent = Pal.Accent; }

            float textW = SpacedWidth(g, text, fChip, Su(1f));
            float h = S(28);
            float w = textW + S(38);
            RectangleF r = new RectangleF(S(894) - w, S(28), w, h);

            Ui.FillRound(g, r, h / 2f, Color.FromArgb(26, accent.R, accent.G, accent.B));
            Ui.StrokeRound(g, RectangleF.Inflate(r, -0.5f, -0.5f), h / 2f,
                           Color.FromArgb(70, accent.R, accent.G, accent.B), 1f);

            float dotR = S(3f);
            float dotX = r.X + S(15);
            float dotY = r.Y + h / 2f;
            using (SolidBrush b = new SolidBrush(accent))
                g.FillEllipse(b, dotX - dotR, dotY - dotR, dotR * 2, dotR * 2);

            Color c = Color.FromArgb(230, accent);
            DrawSpaced(g, text, fChip, c, dotX + S(10), r.Y + (h - S(11)) / 2f - S(1), Su(1f));
        }

        private void DrawHero(Graphics g)
        {
            if (successMode)
            {
                DrawScanHero(g);
                return;
            }

            DrawSpaced(g, "АНТИЧИТ · КОНТРОЛЬ ЧИСТОТЫ", fEyebrow, Pal.Accent, S(30), S(106), Su(1.3f));

            g.DrawString("Проверка", fH1, new SolidBrush(Pal.Text), S(30), S(122), SfTypo);
            g.DrawString("на читы", fH1, new SolidBrush(Pal.Accent), S(30), S(160), SfTypo);

            RectangleF descRect = new RectangleF(S(30), S(210), S(462), S(72));
            using (StringFormat sf = new StringFormat())
            {
                sf.Alignment = StringAlignment.Near;
                sf.LineAlignment = StringAlignment.Near;
                using (SolidBrush b = new SolidBrush(Color.FromArgb(235, Pal.Muted)))
                    g.DrawString("Введите одноразовый код, который выдал проверяющий. " +
                                 "Если код верный — запустится глубокая проверка системы игрока.",
                                 fDesc, b, descRect, sf);
            }

            DrawFeature(g, S(302), "Проверка процессов, модов и памяти");
            DrawFeature(g, S(338), "Поиск следов чит-клиентов и читов");
            DrawFeature(g, S(374), "Подробный отчёт о находках");

            RectangleF info = new RectangleF(S(30), S(432), S(462), S(56));
            Ui.FillRound(g, info, S(14), Color.FromArgb(18, 140, 180, 220));
            Ui.StrokeRound(g, RectangleF.Inflate(info, -0.5f, -0.5f), S(14), Color.FromArgb(60, 80, 110, 140), 1f);

            using (SolidBrush b = new SolidBrush(Pal.SoftText))
                g.DrawString("ОДНОРАЗОВЫЙ ДОСТУП", fCardLabel, b, info.X + S(16), info.Y + S(11), SfTypo);

            using (SolidBrush b = new SolidBrush(Pal.Muted))
                g.DrawString("Один код = одна полная проверка системы", fDesc, b,
                             info.X + S(16), info.Y + S(27), SfTypo);
        }

        private void DrawScanHero(Graphics g)
        {
            float x = S(30);

            DrawSpaced(g, "ЖИВОЙ ОТЧЁТ", fEyebrow, Pal.Accent, x, S(106), Su(1.3f));

            int total = foundCritical + foundWarning + foundInfo;
            Color countColor = foundCritical > 0 ? Pal.Danger : (foundWarning > 0 ? Pal.Warn : Pal.Text);

            using (SolidBrush b = new SolidBrush(countColor))
                g.DrawString(total.ToString(), fBig, b, x, S(126), SfTypo);

            float numW = g.MeasureString(total.ToString(), fBig, PointF.Empty, SfTypo).Width;
            using (SolidBrush b = new SolidBrush(Pal.Muted))
                g.DrawString("находок", fDesc, b, x + numW + S(10), S(156), SfTypo);

            // три счётчика
            DrawMiniStat(g, x, S(196), "КРИТИЧНО", foundCritical, Pal.Danger);
            DrawMiniStat(g, x + S(152), S(196), "ВНИМАНИЕ", foundWarning, Pal.Warn);
            DrawMiniStat(g, x + S(304), S(196), "ИНФО", foundInfo, Pal.Muted);

            using (Pen pen = new Pen(Color.FromArgb(255, 20, 31, 43), 1f))
                g.DrawLine(pen, x, S(252), x + S(462), S(252));

            DrawSpaced(g, "ПОСЛЕДНИЕ НАХОДКИ", fCardLabel, Pal.Dim, x, S(270), Su(1.1f));

            int shown = Math.Min(5, recentFindings.Count);
            if (shown == 0)
            {
                using (SolidBrush b = new SolidBrush(Pal.Dim))
                    g.DrawString(scanDone ? "Ничего подозрительного не найдено" : "Следы читов не обнаружены",
                                 fDesc, b, x, S(298), SfTypo);
            }
            else
            {
                for (int i = 0; i < shown; i++)
                {
                    Finding f = recentFindings[i];
                    Color dot = f.Level == Severity.Critical ? Pal.Danger :
                                (f.Level == Severity.Warning ? Pal.Warn : Pal.Muted);
                    float y = S(294) + i * S(50);

                    using (SolidBrush b = new SolidBrush(dot))
                        g.FillEllipse(b, x, y + S(5), S(6), S(6));

                    string title = f.Title;
                    if (title.Length > 50) title = title.Substring(0, 49) + "…";
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(240, Pal.Text)))
                        g.DrawString(title, fRow, b, x + S(16), y, SfTypo);

                    string sub = f.Category;
                    if (f.Detail.Length > 0) sub += " · " + f.Detail;
                    if (sub.Length > 62) sub = sub.Substring(0, 61) + "…";
                    using (SolidBrush b = new SolidBrush(Pal.Dim))
                        g.DrawString(sub, fStatSub, b, x + S(16), y + S(18), SfTypo);
                }
            }

            // нижняя плашка — прижата к низу карточки
            RectangleF info = new RectangleF(S(30), rCard.Bottom - S(84) - S(16), S(462), S(84));
            Ui.FillRound(g, info, S(14), Color.FromArgb(18, 140, 180, 220));
            Ui.StrokeRound(g, RectangleF.Inflate(info, -0.5f, -0.5f), S(14), Color.FromArgb(60, 80, 110, 140), 1f);

            string stageText = scanDone
                ? (scanAborted ? "Проверка остановлена" : "Все этапы завершены")
                : "Этап " + Math.Min(scanStage + 1, stageState.Length) + " из " + stageState.Length;

            using (SolidBrush b = new SolidBrush(Pal.SoftText))
                g.DrawString("СОСТОЯНИЕ", fCardLabel, b, info.X + S(16), info.Y + S(11), SfTypo);

            using (SolidBrush b = new SolidBrush(Pal.Text))
                g.DrawString(stageText, fDesc, b, info.X + S(16), info.Y + S(28), SfTypo);

            DateTime since = scanReport != null ? scanReport.SessionStart : scanStarted;
            using (SolidBrush b = new SolidBrush(Pal.Dim))
                g.DrawString("Процессов: " + liveProcesses + "   ·   Файлов: " + liveFiles +
                             "   ·   с " + since.ToString("HH:mm"), fStatSub, b,
                             info.X + S(16), info.Y + S(50), SfTypo);

            string mode = adminMode ? "Полный доступ" : "Ограниченный режим";
            using (SolidBrush b = new SolidBrush(adminMode ? Pal.Accent : Pal.Warn))
                g.DrawString(mode, fStatSub, b,
                             info.Right - S(16) - g.MeasureString(mode, fStatSub).Width,
                             info.Y + S(50), SfTypo);
        }

        private void DrawMiniStat(Graphics g, float x, float y, string caption, int value, Color color)
        {
            DrawSpaced(g, caption, fStatCap, Pal.Dim, x, y, Su(1.05f));
            using (SolidBrush b = new SolidBrush(color))
                g.DrawString(value.ToString(), fSuccTitle, b, x, y + S(14), SfTypo);
        }

        private void DrawFeature(Graphics g, int y, string text)
        {
            using (SolidBrush dot = new SolidBrush(Color.FromArgb(200, Pal.Accent)))
                g.FillEllipse(dot, S(31), y + S(4), S(7), S(7));
            using (SolidBrush b = new SolidBrush(Pal.Muted))
                g.DrawString(text, fFeature, b, S(50), y, SfTypo);
        }

        private void DrawCard(Graphics g)
        {
            RectangleF card = rCard;

            for (int i = 5; i >= 1; i--)
            {
                RectangleF sr = RectangleF.Inflate(card, Su(i * 2.4f), Su(i * 2.4f));
                sr.Offset(0, Su(i * 1.6f));
                Ui.FillRound(g, sr, Su(22), Color.FromArgb(9, 0, 0, 0));
            }

            Ui.FillRound(g, card, Su(22), Pal.Card);
            using (GraphicsPath p = Ui.Round(card, Su(22)))
            using (LinearGradientBrush lg = new LinearGradientBrush(card, Color.White, Color.White, 90f))
            {
                ColorBlend blend = new ColorBlend(3);
                blend.Colors = new Color[]
                {
                    Color.FromArgb(16, 255, 255, 255),
                    Color.FromArgb(0, 255, 255, 255),
                    Color.FromArgb(0, 255, 255, 255)
                };
                blend.Positions = new float[] { 0f, 0.55f, 1f };
                lg.InterpolationColors = blend;
                lg.WrapMode = WrapMode.TileFlipXY;
                g.FillPath(lg, p);
            }

            Ui.StrokeRound(g, RectangleF.Inflate(card, -0.5f, -0.5f), Su(22), Pal.CardBorder, 1f);

            if (successMode)
            {
                DrawScan(g);
            }
            else
            {
                DrawForm(g);
                DrawStats(g);
            }
        }

        private void DrawForm(Graphics g)
        {
            float x = rCard.X + S(22);

            DrawSpaced(g, "КОД ДОСТУПА", fCardLabel, Pal.Muted, x, rCard.Y + S(20), Su(1.2f));

            float badgeW = SpacedWidth(g, "1 КОД = 1 ПРОВЕРКА", fCardLabel, Su(1.2f));
            DrawSpaced(g, "1 КОД = 1 ПРОВЕРКА", fCardLabel, Pal.Dim,
                       rCard.Right - S(22) - badgeW, rCard.Y + S(20), Su(1.2f));

            DrawEntry(g);

            if (statusText.Length > 0)
            {
                RectangleF sr = new RectangleF(rCard.X + S(22), rCard.Y + S(176), rCard.Width - S(44), S(20));
                using (SolidBrush b = new SolidBrush(statusColor))
                using (StringFormat sf = new StringFormat())
                {
                    sf.Alignment = StringAlignment.Center;
                    sf.LineAlignment = StringAlignment.Center;
                    g.DrawString(statusText, fStatus, b, sr, sf);
                }
            }

            DrawSteps(g);
        }

        private void DrawEntry(Graphics g)
        {
            RectangleF r = rEntry;
            r.Offset(shakeX, 0);

            Color border = Pal.Stroke;
            if (errorFlash) border = Pal.Danger;
            else if (entryFocus) border = Pal.Accent;

            if (entryFocus || errorFlash)
            {
                Color glow = errorFlash ? Pal.Danger : Pal.Accent;
                for (int i = 3; i >= 1; i--)
                {
                    int pad = (int)Su(i * 2.4f);
                    Ui.StrokeRound(g, RectangleF.Inflate(r, pad, pad), Su(14 + i * 2),
                                   Color.FromArgb(14, glow.R, glow.G, glow.B), Su(2f));
                }
            }

            Ui.FillRound(g, r, Su(14), Pal.EntryBg);
            Ui.StrokeRound(g, RectangleF.Inflate(r, -0.5f, -0.5f), Su(14), border, 1f);
        }

        private void DrawSteps(Graphics g)
        {
            string[] steps = new string[]
            {
                "Клиент и сборка игры",
                "Процессы, драйверы, моды",
                "Конфиги и следы читов"
            };

            float x = rCard.X + S(22);
            float y = rCard.Y + S(204);

            for (int i = 0; i < steps.Length; i++)
            {
                float cy = y + i * S(32);
                RectangleF circle = new RectangleF(x, cy, S(22), S(22));

                Ui.FillRound(g, circle, S(11), Color.FromArgb(18, 140, 180, 220));
                Ui.StrokeRound(g, RectangleF.Inflate(circle, -0.5f, -0.5f), S(11), Pal.Stroke, 1f);

                using (StringFormat sf = new StringFormat())
                {
                    sf.Alignment = StringAlignment.Center;
                    sf.LineAlignment = StringAlignment.Center;
                    using (SolidBrush b = new SolidBrush(Pal.Dim))
                        g.DrawString((i + 1).ToString(), fRowNum, b, circle, sf);
                }

                using (SolidBrush b = new SolidBrush(Pal.Muted))
                    g.DrawString(steps[i], fRow, b, x + S(34), cy + S(4), SfTypo);

                using (SolidBrush b = new SolidBrush(Pal.Dim))
                    g.DrawString("ожидает", fStatSub, b,
                                 rCard.Right - S(22) - g.MeasureString("ожидает", fStatSub).Width - S(4),
                                 cy + S(5), SfTypo);
            }

            using (Pen pen = new Pen(Color.FromArgb(255, 20, 31, 43), 1f))
                g.DrawLine(pen, rDivider.X, rDivider.Y, rDivider.Right, rDivider.Y);
        }

        private void DrawScan(Graphics g)
        {
            float x = rCard.X + S(22);
            float w = rCard.Width - S(44);

            // ---------------------------------------------------------- заголовок
            DrawSpaced(g, "ПРОВЕРКА СИСТЕМЫ", fCardLabel, Pal.Muted, x, rCard.Y + S(20), Su(1.2f));

            string right = elapsedText + "  ·  " + scanPercent;
            using (SolidBrush b = new SolidBrush(scanDone ? Pal.SoftText : Pal.Accent))
                g.DrawString(right, fStatSub, b,
                             rCard.Right - S(22) - g.MeasureString(right, fStatSub).Width,
                             rCard.Y + S(18), SfTypo);

            // ---------------------------------------------------------- прогресс
            double done = 0;
            for (int i = 0; i < stageState.Length; i++)
            {
                if (stageState[i] == 2) done += 1;
                else if (stageState[i] == 1) done += stageProgress[i];
            }
            if (scanDone && !scanAborted) done = stageState.Length;
            float frac = (float)(done / stageState.Length);
            if (frac < 0f) frac = 0f;
            if (frac > 1f) frac = 1f;

            RectangleF track = new RectangleF(x, rCard.Y + S(46), w, S(6));
            Ui.FillRound(g, track, track.Height / 2f, Color.FromArgb(255, 16, 25, 35));

            if (frac > 0.001f)
            {
                RectangleF fill = new RectangleF(track.X, track.Y, Math.Max(track.Height, track.Width * frac), track.Height);
                using (LinearGradientBrush lg = new LinearGradientBrush(
                           new RectangleF(track.X, track.Y, Math.Max(1f, track.Width), track.Height),
                           Pal.AccentLo, Pal.AccentHi, 0f))
                using (GraphicsPath clip = Ui.Round(track, track.Height / 2f))
                {
                    Region old = g.Clip;
                    g.SetClip(clip);
                    g.FillRectangle(lg, fill);
                    g.Clip = old;
                }
            }

            // ---------------------------------------------------------- этапы
            float listY = rCard.Y + S(68);
            float rowH = S(23f);
            for (int i = 0; i < stageState.Length; i++)
            {
                float cy = listY + i * rowH;
                RectangleF circle = new RectangleF(x, cy, S(18), S(18));
                int state = stageState[i];
                bool bad = stageCrit[i] > 0;
                bool warn = !bad && stageFindings[i] > 0;

                Color accent = state == 0 ? Pal.Dim
                             : (state == 1 ? Pal.AccentHi
                             : (bad ? Pal.Danger : (warn ? Pal.Warn : Pal.Accent)));

                if (state == 2)
                {
                    Ui.FillRound(g, circle, S(9), Color.FromArgb(225, accent));

                    if (bad || warn)
                    {
                        using (Pen pen = new Pen(Pal.Card, Su(1.8f)))
                        {
                            pen.StartCap = LineCap.Round;
                            pen.EndCap = LineCap.Round;
                            g.DrawLine(pen, circle.X + circle.Width / 2f, circle.Y + S(4.5f),
                                            circle.X + circle.Width / 2f, circle.Y + S(10f));
                        }
                        using (SolidBrush b = new SolidBrush(Pal.Card))
                            g.FillEllipse(b, circle.X + circle.Width / 2f - S(1f),
                                             circle.Y + S(12f), S(2.2f), S(2.2f));
                    }
                    else
                    {
                        using (Pen pen = new Pen(Pal.Card, Su(1.6f)))
                        {
                            pen.StartCap = LineCap.Round;
                            pen.EndCap = LineCap.Round;
                            pen.LineJoin = LineJoin.Round;
                            PointF[] pts = new PointF[]
                            {
                                new PointF(circle.X + S(4.5f), circle.Y + S(9f)),
                                new PointF(circle.X + S(7.5f), circle.Y + S(12.5f)),
                                new PointF(circle.X + S(13.5f), circle.Y + S(5.8f))
                            };
                            g.DrawLines(pen, pts);
                        }
                    }
                }
                else
                {
                    Ui.FillRound(g, circle, S(9), Color.FromArgb(16, 140, 180, 220));
                    Ui.StrokeRound(g, circle, S(9), state == 1 ? accent : Pal.Stroke, 1f);

                    if (state == 1)
                    {
                        using (Pen pen = new Pen(Pal.AccentHi, Su(1.7f)))
                        using (GraphicsPath arc = new GraphicsPath())
                        {
                            float sweep = (float)(360.0 * stageProgress[i]);
                            if (sweep < 6f) sweep = 6f;
                            arc.AddArc(circle.X + Su(1.2f), circle.Y + Su(1.2f),
                                       circle.Width - Su(2.4f), circle.Height - Su(2.4f), -90f, sweep);
                            g.DrawPath(pen, arc);
                        }
                    }

                    using (StringFormat sf = new StringFormat())
                    {
                        sf.Alignment = StringAlignment.Center;
                        sf.LineAlignment = StringAlignment.Center;
                        using (SolidBrush b = new SolidBrush(state == 1 ? Pal.Text : Pal.Dim))
                            g.DrawString((i + 1).ToString(), fRowNum, b, circle, sf);
                    }
                }

                string name = i < stageNames.Length ? stageNames[i] : "Этап " + (i + 1);
                using (SolidBrush b = new SolidBrush(state == 0 ? Pal.Dim : (state == 1 ? Pal.Text : Pal.SoftText)))
                    g.DrawString(name, fRow, b, x + S(29), cy + S(2), SfTypo);

                string status;
                Color statusColor;
                if (state == 0) { status = "ожидает"; statusColor = Pal.Dim; }
                else if (state == 1)
                {
                    status = stageProgress[i] <= 0.001
                        ? "проверка…"
                        : "проверка " + ((int)Math.Round(stageProgress[i] * 100)) + "%";
                    statusColor = Pal.AccentHi;
                }
                else if (stageFindings[i] == 0) { status = "чисто"; statusColor = Pal.Accent; }
                else
                {
                    status = stageFindings[i] + " наход.";
                    statusColor = bad ? Pal.Danger : Pal.Warn;
                }

                using (SolidBrush b = new SolidBrush(statusColor))
                    g.DrawString(status, fStatSub, b,
                                 rCard.Right - S(22) - g.MeasureString(status, fStatSub).Width,
                                 cy + S(3), SfTypo);
            }

            // ---------------------------------------------------------- итог
            using (Pen pen = new Pen(Color.FromArgb(255, 20, 31, 43), 1f))
                g.DrawLine(pen, x, rCard.Y + S(420), rCard.Right - S(22), rCard.Y + S(420));

            string verdict;
            Color verdictColor;
            string cheatName = topFinding != null ? CheatName(topFinding) : "";

            if (!scanDone) { verdict = "ИДЁТ СКАНИРОВАНИЕ"; verdictColor = Pal.AccentHi; }
            else if (scanAborted) { verdict = "ПРОВЕРКА ОСТАНОВЛЕНА"; verdictColor = Pal.Warn; }
            else if (topFinding != null && topFinding.Confirmed)
            {
                verdict = "НАЙДЕН ЧИТ: " + Short(cheatName, 34);
                verdictColor = Pal.Danger;
            }
            else if (foundCritical > 0) { verdict = "ОБНАРУЖЕНЫ ЧИТЫ"; verdictColor = Pal.Danger; }
            else if (foundWarning > 0) { verdict = "ЕСТЬ ПОДОЗРИТЕЛЬНЫЕ СЛЕДЫ"; verdictColor = Pal.Warn; }
            else { verdict = "СИСТЕМА ЧИСТА"; verdictColor = Pal.Accent; }

            DrawSpaced(g, verdict, fScanTitle, verdictColor, x, rCard.Y + S(436), Su(1.1f));

            string counts = "Критично: " + foundCritical +
                            "   ·   Внимание: " + foundWarning +
                            "   ·   Инфо: " + foundInfo;
            using (SolidBrush b = new SolidBrush(Pal.Muted))
                g.DrawString(counts, fStatSub, b, x, rCard.Y + S(462), SfTypo);

            string detail;
            if (scanDone && topFinding != null && topFinding.Path.Length > 0)
                detail = topFinding.Path;
            else if (!string.IsNullOrEmpty(lastLog))
                detail = lastLog;
            else if (scanDone)
                detail = "Проверка завершена. Отчёт сохранён на рабочем столе.";
            else
                detail = "Собираем данные о системе…";

            if (detail.Length > 74) detail = detail.Substring(0, 73) + "…";
            using (SolidBrush b = new SolidBrush(scanDone && topFinding != null ? Pal.SoftText : Pal.Dim))
                g.DrawString(detail, fStatSub, b, x, rCard.Y + S(486), SfTypo);

            if (!adminMode)
            {
                using (SolidBrush b = new SolidBrush(Pal.Warn))
                    g.DrawString("Без прав администратора часть журналов недоступна",
                                 fStatSub, b, x, rCard.Y + S(510), SfTypo);
            }
        }

        private static string Short(string text, int max)
        {
            if (string.IsNullOrEmpty(text)) return "";
            text = text.Replace("\r", " ").Replace("\n", " ").Trim();
            return text.Length <= max ? text : text.Substring(0, max - 1) + "…";
        }

        private static string CheatName(Finding f)
        {
            if (f == null) return "";
            if (!string.IsNullOrEmpty(f.Path))
            {
                string name = Path.GetFileName(f.Path);
                if (!string.IsNullOrEmpty(name)) return name;
            }
            return f.Title;
        }

        private void DrawStats(Graphics g)
        {
            float x = rCard.X + S(22);
            float y = rCard.Y + S(334);

            using (Pen pen = new Pen(Color.FromArgb(255, 20, 31, 43), 1f))
                g.DrawLine(pen, rDivider.X, rDivider.Y, rDivider.Right, rDivider.Y);

            DrawStat(g, x, y, S(180), "ОСТАЛОСЬ КОДОВ",
                     statRemaining.ToString(), Pal.Accent,
                     "из " + statTotal.ToString() + " доступно");

            DrawStat(g, x + S(196), y, S(180), "ПРОВЕРЕНО",
                     statUsed.ToString(), Pal.Text,
                     "всего проверок");

            using (SolidBrush b = new SolidBrush(Pal.Dim))
                g.DrawString("Каждый код действует только один раз", fStatSub, b,
                             x, rCard.Bottom - S(30), SfTypo);
        }

        private void DrawStat(Graphics g, float x, float y, float w, string caption,
                              string value, Color valueColor, string sub)
        {
            DrawSpaced(g, caption, fStatCap, Pal.Dim, x, y, Su(1.15f));

            RectangleF valueRect = new RectangleF(x, y + S(13), w, S(30));
            using (SolidBrush b = new SolidBrush(valueColor))
                g.DrawString(value, fStatVal, b, valueRect, SfTypo);

            float valueW = g.MeasureString(value, fStatVal, PointF.Empty, SfTypo).Width;
            using (SolidBrush b = new SolidBrush(Pal.Dim))
                g.DrawString(sub, fStatSub, b, x, y + S(45), SfTypo);
        }

        private void DrawFooter(Graphics g)
        {
            using (SolidBrush b = new SolidBrush(Pal.Dim))
                g.DrawString("Одноразовый доступ · 1 код = 1 полная проверка", fFoot, b,
                             S(30), S(layoutHeight - 44), SfTypo);
        }

        // ------------------------------------------------------------- утилиты
        private void DrawSpaced(Graphics g, string text, Font font, Color color,
                                float x, float y, float spacing)
        {
            float cx = x;
            using (SolidBrush brush = new SolidBrush(color))
            {
                for (int i = 0; i < text.Length; i++)
                {
                    string part = text.Substring(i, 1);
                    g.DrawString(part, font, brush, cx, y, SfTypo);
                    cx += g.MeasureString(part, font, PointF.Empty, SfTypo).Width + spacing;
                    if (part == " ") cx += spacing * 2.2f;
                }
            }
        }

        private float SpacedWidth(Graphics g, string text, Font font, float spacing)
        {
            float w = 0f;
            for (int i = 0; i < text.Length; i++)
            {
                string part = text.Substring(i, 1);
                w += g.MeasureString(part, font, PointF.Empty, SfTypo).Width + spacing;
                if (part == " ") w += spacing * 2.2f;
            }
            return w - spacing;
        }
    }

    internal static class Program
    {
        [DllImport("user32.dll")]
        private static extern bool SetProcessDpiAwarenessContext(IntPtr value);

        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr FindWindow(string className, string windowName);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int cmd);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        private static System.Threading.Mutex instanceMutex;

        public static bool IsAdmin()
        {
            try
            {
                using (System.Security.Principal.WindowsIdentity identity =
                       System.Security.Principal.WindowsIdentity.GetCurrent())
                {
                    System.Security.Principal.WindowsPrincipal principal =
                        new System.Security.Principal.WindowsPrincipal(identity);
                    return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
                }
            }
            catch { return false; }
        }

        public static void ReleaseInstance()
        {
            try
            {
                if (instanceMutex == null) return;
                instanceMutex.ReleaseMutex();
                instanceMutex.Dispose();
                instanceMutex = null;
            }
            catch { }
        }

        [STAThread]
        private static void Main()
        {
            bool created;
            instanceMutex = new System.Threading.Mutex(true, "MineCheck.SingleInstance", out created);
            if (!created)
            {
                IntPtr existing = FindWindow(null, "MINECHECK — проверка на читы");
                if (existing != IntPtr.Zero)
                {
                    ShowWindow(existing, 9);
                    SetForegroundWindow(existing);
                }
                return;
            }

            try { SetProcessDpiAwarenessContext(new IntPtr(-4)); }
            catch
            {
                try { SetProcessDPIAware(); } catch { }
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            Application.ThreadException += delegate(object s, System.Threading.ThreadExceptionEventArgs e)
            {
                MessageBox.Show(e.Exception.Message, "MINECHECK", MessageBoxButtons.OK, MessageBoxIcon.Error);
            };

            Application.Run(new MainForm());
        }
    }
}
