using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;
using System.Web.Script.Serialization;

namespace AiAnimeTool
{
    public class RenameItem { public string oldPath; public string newPath; }
    public class CfgProject { public string location; public string numStyle; }
    public class CfgRename { public string folder; public string numStyle; public bool open; public bool close; public List<RenameItem> undo; }
    public class CfgImage { public string baseUrl; public string apiKey; public string model; public string size; public string quality; public string proxy; public List<string> models; }
    public class CfgWindow { public int w; public int h; }
    public class CfgUpdate { public string manifest; public bool enabled = true; public string skipped; }
    public class AppConfig { public CfgProject project; public CfgRename rename; public CfgImage image; public CfgWindow window; public CfgUpdate update; }

    public static class Program
    {
        public static string ConfigPath;
        public static AppConfig Config;
        public static JavaScriptSerializer Json = new JavaScriptSerializer();
        public const int WinW = 1300;
        public const int WinH = 860;
        public const int TitleH = 52;
        public const int SideW = 220;

        [DllImport("user32.dll")]
        static extern bool SetProcessDPIAware();

        [STAThread]
        static void Main()
        {
            try { SetProcessDPIAware(); } catch { }
            // 单实例：已经开着一个，就把它唤到前台然后自己退出。
            // 两个窗口叠在一起时，后面那个的标题栏会从前面那个的上边缘露出来，
            // 看起来就像凭空多了一条系统边框（用户反复报的那个“不明程序框”很大一部分就是它）。
            if (AlreadyRunning()) return;
            // 安全协议：.NET Framework 4.0 默认只有 Ssl3+Tls1.0，现代接口一律要求 TLS1.2 以上，
            // 不显式指定就会出现「请求被中止: 未能创建 SSL/TLS 安全通道」。
            try { ServicePointManager.SecurityProtocol = (SecurityProtocolType)(3072 | 12288); }
            catch { try { ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12; } catch { } }
            ServicePointManager.Expect100Continue = false;
            try { ServicePointManager.DefaultConnectionLimit = 8; } catch { }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            string dir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
            ConfigPath = Path.Combine(dir, "config.json");
            LoadConfig();
            Application.Run(new MainForm());
        }

        static System.Threading.Mutex _mutex;
        static bool AlreadyRunning()
        {
            bool created = false;
            try { _mutex = new System.Threading.Mutex(true, "AiSeriesTool_SingleInstance_320", out created); }
            catch { return false; }                 // 拿不到互斥体就照常启动，绝不因为单实例机制启动失败
            if (created) return false;
            // 把已有窗口叫到前台（最小化的先还原），然后本进程直接退出
            try
            {
                IntPtr h = FindWindow(null, "AI系列生产工具");
                if (h != IntPtr.Zero)
                {
                    if (IsIconic(h)) ShowWindow(h, 9);      // SW_RESTORE
                    SetForegroundWindow(h);
                }
            }
            catch { }
            return true;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindow(string cls, string title);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int cmd);
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);

        public static void LoadConfig()
        {
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            Config = new AppConfig();
            Config.project = new CfgProject(); Config.project.location = desktop; Config.project.numStyle = "1";
            Config.rename = new CfgRename(); Config.rename.folder = desktop; Config.rename.numStyle = "1";
            try
            {
                if (File.Exists(ConfigPath))
                {
                    var c = Json.Deserialize<AppConfig>(File.ReadAllText(ConfigPath, Encoding.UTF8));
                    if (c != null) Config = c;
                    if (Config.project == null) Config.project = new CfgProject();
                    if (string.IsNullOrEmpty(Config.project.location)) Config.project.location = desktop;
                    if (string.IsNullOrEmpty(Config.project.numStyle)) Config.project.numStyle = "1";
                    if (Config.rename == null) Config.rename = new CfgRename();
                    if (string.IsNullOrEmpty(Config.rename.folder)) Config.rename.folder = desktop;
                    if (string.IsNullOrEmpty(Config.rename.numStyle)) Config.rename.numStyle = "1";
                    if (Config.image == null) Config.image = new CfgImage();
                    if (string.IsNullOrEmpty(Config.image.baseUrl)) Config.image.baseUrl = "https://api.openai.com/v1";
                    if (string.IsNullOrEmpty(Config.image.model)) Config.image.model = "gpt-image-1";
                    if (string.IsNullOrEmpty(Config.image.size)) Config.image.size = "auto";
                    if (Config.image.models == null) Config.image.models = new List<string>();
                    if (Config.window == null) Config.window = new CfgWindow();
                    if (Config.update == null) Config.update = new CfgUpdate();
                }
            }
            catch { }
            if (Config.image == null) Config.image = new CfgImage();
            if (Config.image.models == null) Config.image.models = new List<string>();
            if (Config.window == null) Config.window = new CfgWindow();
            if (Config.update == null) Config.update = new CfgUpdate();
        }

        public static void SaveConfig()
        {
            try { File.WriteAllText(ConfigPath, Json.Serialize(Config), Encoding.UTF8); }
            catch { }
        }

        public static string FormatNum(int n, string style)
        {
            if (style == "01") return n.ToString("D2");
            if (style == "一") return ToChinese(n);
            return n.ToString();
        }

        public static string StyleName(string style)
        {
            if (style == "01") return "01（补零）";
            if (style == "一") return "一（大写）";
            return "1（默认）";
        }

        static string[] cn = { "零", "一", "二", "三", "四", "五", "六", "七", "八", "九" };
        public static string ToChinese(int n)
        {
            if (n < 10) return cn[n];
            if (n < 100)
            {
                int t = n / 10, u = n % 10;
                string r = t > 1 ? cn[t] : "";
                r += "十";
                if (u > 0) r += cn[u];
                return r;
            }
            if (n < 1000)
            {
                int h = n / 100, r = n % 100;
                string s = cn[h] + "百";
                if (r > 0) { if (r < 10) s += "零" + cn[r]; else s += ToChinese(r); }
                return s;
            }
            int th = n / 1000, rest = n % 1000;
            string res = cn[th] + "千";
            if (rest > 0) { if (rest < 100) res += "零" + ToChinese(rest); else res += ToChinese(rest); }
            return res;
        }
    }

    // 右键菜单统一成圆角、白底、淡紫悬停的自绘样式：
    // 原生的 ContextMenuStrip 是直角 + 系统灰边，和我们整套自绘 UI 不搭。
    public static class ThemeMenu
    {
        public static void Apply(ContextMenuStrip m)
        {
            if (m == null) return;
            m.Renderer = new Skin();
            m.BackColor = Color.White;
            m.ShowImageMargin = false;
            m.ShowCheckMargin = false;
            m.AutoSize = true;
            m.Padding = new Padding(10, 9, 10, 9);
            m.Font = new Font("微软雅黑", 10.5F, FontStyle.Bold);
            m.Height = 0;
            foreach (ToolStripItem it in m.Items) SizeUp(it);
            m.ItemAdded += (s, e) => SizeUp(e.Item);
            EventHandler round = (s, e) => RoundIt(m);
            m.Opened += round;
            m.SizeChanged += round;
        }

        // 原生项高只有二十几像素，点起来太窄：每项自己撑出上下留白，整块菜单变胖
        static void SizeUp(ToolStripItem it)
        {
            if (it == null || it is ToolStripSeparator) return;
            it.Padding = new Padding(16, 9, 16, 9);
            // 菜单项就一行字，统一居中对齐；字体已经加粗，字号在 Apply 里统一设
            it.TextAlign = ContentAlignment.MiddleCenter;
            it.Font = new Font("微软雅黑", 10.5F, FontStyle.Bold);
        }

        public static void RoundIt(ContextMenuStrip m)
        {
            try
            {
                Rectangle rc = new Rectangle(0, 0, m.Width, m.Height);
                m.Region = new Region(Ui.Rounded(rc, 12));
            }
            catch { }
        }

        class Skin : ToolStripRenderer
        {
            protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                // 圆角边缘的抗锯齿：窗体裁剪(Region)是硬边、天生锯齿，所以不把白底铺满，
                // 而是缩进画白底 + 外面套一圈描边色。锯齿落在描边色那一圈上，
                // 从桌面上看就是一条淡淡的光边，不再是一排白牙。
                Rectangle outer = new Rectangle(0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
                using (GraphicsPath po = Ui.Rounded(outer, 12))
                using (SolidBrush bo = new SolidBrush(Color.FromArgb(222, 228, 242)))
                    g.FillPath(bo, po);
                Rectangle inner = new Rectangle(1, 1, e.ToolStrip.Width - 3, e.ToolStrip.Height - 3);
                using (GraphicsPath pi = Ui.Rounded(inner, 11))
                    using (SolidBrush bi = new SolidBrush(Color.White))
                        g.FillPath(bi, pi);
            }

            protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e) { }

            protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                Rectangle rc = new Rectangle(4, e.Item.Bounds.Y + 1, e.ToolStrip.Width - 8, e.Item.Bounds.Height - 2);
                using (GraphicsPath p = Ui.Rounded(rc, 9))
                {
                    bool on = e.Item.Selected || e.Item.Pressed;
                    using (SolidBrush b = new SolidBrush(on ? Color.FromArgb(238, 241, 253) : Color.White))
                        g.FillPath(b, p);
                }
            }

            protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
            {
                e.TextColor = e.Item.Selected ? Color.FromArgb(102, 112, 186) : Color.FromArgb(52, 60, 84);
                e.TextFormat |= TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix;
                base.OnRenderItemText(e);
            }

            protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
            {
                Rectangle rc = e.Item.ContentRectangle;
                using (SolidBrush b = new SolidBrush(Color.FromArgb(234, 238, 248)))
                    e.Graphics.FillRectangle(b, new Rectangle(rc.X + 4, rc.Y + rc.Height / 2, rc.Width - 8, 1));
            }
        }
    }

    /// <summary>
    /// 单条圆角菜单（右键「复制图像」专用）。
    /// ContextMenuStrip 的圆角只能靠 Window Region 硬裁，边缘必然是台阶状锯齿，
    /// 怎么叠描边都盖不住；这里改成逐像素 alpha 的分层窗口，圆角是真正的抗锯齿。
    /// </summary>
    public class RoundMenu : Form
    {
        string[] texts; Action[] acts; int hot = -1;
        const int PadX = 34, PadY = 17, Shadow = 10, Radius = 14, RowH = 46;

        [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hWnd);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
        [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr hDC);
        [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr hDC);
        [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr hDC, IntPtr hObject);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr hObject);
        [DllImport("user32.dll")] static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize,
            IntPtr hdcSrc, ref POINT pptSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("gdi32.dll")] static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFO pbmi, uint usage, out IntPtr ppvBits, IntPtr hSection, uint offset);
        [DllImport("msvcrt.dll", EntryPoint = "memset", CallingConvention = CallingConvention.Cdecl)]
        static extern IntPtr Memset(IntPtr dest, int c, IntPtr count);
        [StructLayout(LayoutKind.Sequential)]
        struct BITMAPINFOHEADER { public uint biSize; public int biWidth; public int biHeight; public ushort biPlanes; public ushort biBitCount; public uint biCompression; public uint biSizeImage; public int biXPelsPerMeter; public int biYPelsPerMeter; public uint biClrUsed; public uint biClrImportant; }
        [StructLayout(LayoutKind.Sequential)]
        struct BITMAPINFO { public BITMAPINFOHEADER bmiHeader; }
        [StructLayout(LayoutKind.Sequential)] struct POINT { public int x; public int y; }
        [StructLayout(LayoutKind.Sequential)] struct SIZE { public int cx; public int cy; }
        [StructLayout(LayoutKind.Sequential)] struct BLENDFUNCTION { public byte BlendOp; public byte BlendFlags; public byte SourceConstantAlpha; public byte AlphaFormat; }
        const int ULW_ALPHA = 2, AC_SRC_ALPHA = 1;

        public RoundMenu(string[] items, Action[] actions)
        {
            texts = items ?? new string[] { "" };
            acts = actions ?? new Action[] { null };
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            BackColor = Color.White;
            Font = new Font("黑体", 10.5F, FontStyle.Regular);
            int tw = 0;
            for (int i = 0; i < texts.Length; i++)
            {
                int w = TextRenderer.MeasureText(texts[i], Font, Size.Empty, TextFormatFlags.NoPadding).Width;
                if (w > tw) tw = w;
            }
            int cw = Math.Max(140, tw + PadX * 2);
            int ch = texts.Length <= 1
                ? Math.Max(52, TextRenderer.MeasureText(texts[0], Font, Size.Empty, TextFormatFlags.NoPadding).Height + PadY * 2)
                : texts.Length * RowH + 14;
            Size = new Size(cw + Shadow * 2, ch + Shadow * 2);
            paint = new Rectangle(Shadow, Shadow, cw, ch);
            Deactivate += (s, e) => Close();
            KeyPreview = true;
            KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) Close(); };
        }

        public RoundMenu(string text, Action onClick) : this(new string[] { text }, new Action[] { onClick }) { }

        Rectangle paint;
        public void Popup(Point screen)
        {
            Rectangle wa = Screen.FromPoint(screen).WorkingArea;
            int x = screen.X - Shadow, y = screen.Y - Shadow;
            if (x + Width > wa.Right) x = wa.Right - Width - 4;
            if (y + Height > wa.Bottom) y = screen.Y - Height + Shadow;
            if (x < wa.Left) x = wa.Left;
            if (y < wa.Top) y = wa.Top;
            Location = new Point(x, y);
            Show();
            // 弹出层永远压在最上面：主窗万一被置顶，菜单也不能藏到它背后去
            try { SetWindowPos(Handle, new IntPtr(-1), x, y, Width, Height, 0x0001 | 0x0002); } catch { }
            Render();
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x00080000 | 0x00000080;   // LAYERED | TOOLWINDOW
                cp.Style &= ~0x00C00000;
                cp.Style &= ~0x000F0000;
                cp.Style |= unchecked((int)0x80000000);
                return cp;
            }
        }

        protected override void OnShown(EventArgs e) { base.OnShown(e); Render(); }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int nh = -1;
            if (paint.Contains(e.Location) && texts.Length > 1)
            {
                int i = (e.Y - paint.Y - 7) / RowH;
                if (i >= 0 && i < texts.Length && e.Y < paint.Y + 7 + texts.Length * RowH) nh = i;
            }
            else if (paint.Contains(e.Location)) nh = 0;
            if (nh != hot) { hot = nh; Render(); }
        }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); if (hot != -1) { hot = -1; Render(); } }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (paint.Contains(e.Location)) hot = texts.Length > 1 ? Math.Max(0, (e.Y - paint.Y - 7) / RowH) : 0;
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            int i = hot;
            Close();
            try { if (i >= 0 && i < acts.Length && acts[i] != null) acts[i](); } catch { }
        }
        protected override void OnFormClosed(FormClosedEventArgs e) { base.OnFormClosed(e); }

        void Render()
        {
            // 关键：分层位图必须自己建 DIB 再逐像素搬进去。
            // 走 Bitmap.GetHbitmap() 的话 GDI 会把 alpha 通道清零，窗口就在、画不出东西。
            int w = Width, h = Height;
            Bitmap bmp = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.Clear(Color.Transparent);

                // 淡淡的投影，让菜单从图片上浮起来（纯 alpha 渐变，不会出硬边）
                for (int i = Shadow; i >= 1; i--)
                {
                    int a = (int)(18 * (1f - (float)(i - 1) / Shadow));
                    if (a <= 0) continue;
                    Rectangle sr = new Rectangle(paint.X - i + 1, paint.Y - i + 2, paint.Width + (i - 1) * 2, paint.Height + (i - 1) * 2);
                    using (GraphicsPath sp = Ui.Rounded(sr, Radius + i / 2))
                    using (SolidBrush sb = new SolidBrush(Color.FromArgb(a, 40, 50, 80)))
                        g.FillPath(sb, sp);
                }

                using (GraphicsPath cp = Ui.Rounded(paint, Radius))
                {
                    using (SolidBrush b = new SolidBrush(Color.White)) g.FillPath(b, cp);
                    using (Pen pen = new Pen(Color.FromArgb(222, 228, 242), 1.4f))
                    {
                        pen.Alignment = PenAlignment.Inset;
                        g.DrawPath(pen, cp);
                    }
                }

                using (StringFormat sf = new StringFormat())
                {
                    sf.Alignment = StringAlignment.Center;
                    sf.LineAlignment = StringAlignment.Center;
                    sf.FormatFlags |= StringFormatFlags.NoWrap;
                    for (int i = 0; i < texts.Length; i++)
                    {
                        Rectangle ir = texts.Length <= 1
                            ? paint
                            : new Rectangle(paint.X + 5, paint.Y + 7 + i * RowH, paint.Width - 10, RowH - 4);
                        if (i == hot && texts.Length > 1)
                        {
                            using (GraphicsPath hp = Ui.Rounded(ir, 10))
                            using (SolidBrush hb = new SolidBrush(Color.FromArgb(241, 244, 255)))
                                g.FillPath(hb, hp);
                        }
                        using (SolidBrush tb = new SolidBrush(i == hot ? Color.FromArgb(102, 112, 186) : Color.FromArgb(52, 60, 84)))
                            g.DrawString(texts[i], Font, tb, new RectangleF(ir.X, ir.Y, ir.Width, ir.Height), sf);
                    }
                }
            }

            BITMAPINFO bmi = new BITMAPINFO();
            bmi.bmiHeader.biSize = 40;
            bmi.bmiHeader.biWidth = w;
            bmi.bmiHeader.biHeight = -h;
            bmi.bmiHeader.biPlanes = 1;
            bmi.bmiHeader.biBitCount = 32;
            bmi.bmiHeader.biCompression = 0;
            IntPtr bits;
            IntPtr dib = CreateDIBSection(IntPtr.Zero, ref bmi, 0, out bits, IntPtr.Zero, 0);
            if (dib == IntPtr.Zero) { bmp.Dispose(); return; }
            IntPtr mem = CreateCompatibleDC(IntPtr.Zero);
            IntPtr oldObj = SelectObject(mem, dib);

            // 逐像素把 GDI+ 的位图搬进 DIB，顺手预乘 alpha（UpdateLayeredWindow 要的是预乘值）
            System.Drawing.Imaging.BitmapData bd = bmp.LockBits(new Rectangle(0, 0, w, h),
                System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            byte[] row = new byte[bd.Stride];
            byte[] outRow = new byte[w * 4];
            for (int y = 0; y < h; y++)
            {
                System.Runtime.InteropServices.Marshal.Copy(new IntPtr(bd.Scan0.ToInt64() + (long)y * bd.Stride), row, 0, bd.Stride);
                for (int x = 0; x < w; x++)
                {
                    int i = x * 4;
                    int a = row[i + 3];
                    outRow[i] = (byte)(row[i] * a / 255);
                    outRow[i + 1] = (byte)(row[i + 1] * a / 255);
                    outRow[i + 2] = (byte)(row[i + 2] * a / 255);
                    outRow[i + 3] = (byte)a;
                }
                System.Runtime.InteropServices.Marshal.Copy(outRow, 0, new IntPtr(bits.ToInt64() + (long)y * w * 4), w * 4);
            }
            bmp.UnlockBits(bd);
            bmp.Dispose();

            IntPtr screen = GetDC(IntPtr.Zero);
            POINT dst = new POINT { x = Left, y = Top };
            POINT src = new POINT { x = 0, y = 0 };
            SIZE sz = new SIZE { cx = w, cy = h };
            BLENDFUNCTION bf = new BLENDFUNCTION { BlendOp = 0, BlendFlags = 0, SourceConstantAlpha = 255, AlphaFormat = AC_SRC_ALPHA };
            bool ok = UpdateLayeredWindow(Handle, screen, ref dst, ref sz, mem, ref src, 0, ref bf, ULW_ALPHA);
            ReleaseDC(IntPtr.Zero, screen);
            SelectObject(mem, oldObj);
            DeleteDC(mem);
            DeleteObject(dib);
        }

        static RoundMenu open;
        public static void Show(string text, Point screen, Action onClick)
        {
            Show(new string[] { text }, screen, new Action[] { onClick });
        }

        public static void Show(string[] texts, Point screen, Action[] acts)
        {
            if (open != null && !open.IsDisposed) { try { open.Close(); } catch { } }
            RoundMenu m = new RoundMenu(texts, acts);
            open = m;
            m.Popup(screen);
        }
    }

    public static class Ui
    {
        public static GraphicsPath Rounded(Rectangle r, int rad)
        {
            GraphicsPath p = new GraphicsPath();
            int d = rad * 2;
            if (d > r.Width) d = r.Width;
            if (d > r.Height) d = r.Height;
            rad = d / 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
        public static Color Shift(Color c, float f)
        {
            int r, g, b;
            if (f >= 0)
            {
                r = (int)(c.R + (255 - c.R) * f);
                g = (int)(c.G + (255 - c.G) * f);
                b = (int)(c.B + (255 - c.B) * f);
            }
            else
            {
                r = (int)(c.R * (1 + f));
                g = (int)(c.G * (1 + f));
                b = (int)(c.B * (1 + f));
            }
            if (r > 255) r = 255; if (g > 255) g = 255; if (b > 255) b = 255;
            if (r < 0) r = 0; if (g < 0) g = 0; if (b < 0) b = 0;
            return Color.FromArgb(r, g, b);
        }

        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
        const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        const int DWMWCP_DONOTROUND = 1;
        const int DWMWCP_ROUND = 2;
        const int DWMWCP_ROUNDSMALL = 3;

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
        static extern IntPtr GetExLong64(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
        static extern IntPtr GetExLong32(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
        static extern IntPtr SetExLong64(IntPtr hWnd, int nIndex, IntPtr val);
        [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
        static extern IntPtr SetExLong32(IntPtr hWnd, int nIndex, IntPtr val);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);

        /// <summary>给任意控件打开双缓冲（Panel 的 DoubleBuffered 是 protected，只能反射设）。</summary>
        public static void SetDoubleBuffered(Control c)
        {
            if (c == null) return;
            try
            {
                System.Reflection.PropertyInfo pi = typeof(Control).GetProperty("DoubleBuffered",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                if (pi != null) pi.SetValue(c, true, null);
            }
            catch { }
        }

        /// <summary>
        /// 整个窗口层次一次性合成（WS_EX_COMPOSITED）：切换前台/弹窗打开时子控件逐块重绘造成的白块闪烁、镂空，
        /// 都是「父容器连背景带子控件分开画」导致的撕裂；交给系统双缓冲后不再逐块闪。
        /// </summary>
        public static void Composite(Form f)
        {
            if (f == null) return;
            try
            {
                IntPtr h = f.Handle;                  // 确保句柄已创建
                int ex = (int)(IntPtr.Size == 8 ? GetExLong64(h, -20) : GetExLong32(h, -20));
                if ((ex & 0x02000000) != 0) return;
                IntPtr nv = new IntPtr(ex | 0x02000000);
                if (IntPtr.Size == 8) SetExLong64(h, -20, nv); else SetExLong32(h, -20, nv);
                SetWindowPos(h, IntPtr.Zero, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0004 | 0x0010);
            }
            catch { }
        }

        [DllImport("ntdll.dll")] static extern int RtlGetVersion(ref RTL_OSVERSIONINFOEX lpVersionInformation);
        [StructLayout(LayoutKind.Sequential)]
        struct RTL_OSVERSIONINFOEX
        {
            public int dwOSVersionInfoSize;
            public int dwMajorVersion;
            public int dwMinorVersion;
            public int dwBuildNumber;
            public int dwPlatformId;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szCSDVersion;
            public ushort wServicePackMajor;
            public ushort wServicePackMinor;
            public ushort wSuiteMask;
            public byte wProductType;
            public byte wReserved;
        }
        // 不依赖应用清单：csc 默认清单会让 Environment.OSVersion 谎报为 6.2.9200，RtlGetVersion 取真实内部版本
        static int RealBuild()
        {
            try
            {
                RTL_OSVERSIONINFOEX o = new RTL_OSVERSIONINFOEX();
                o.dwOSVersionInfoSize = Marshal.SizeOf(typeof(RTL_OSVERSIONINFOEX));
                if (RtlGetVersion(ref o) == 0) return o.dwBuildNumber;
            }
            catch { }
            try { return int.Parse((string)Microsoft.Win32.Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion", "CurrentBuild", "0")); } catch { }
            return Environment.OSVersion.Version.Build;
        }

        public static bool IsWin11 { get { return RealBuild() >= 22000; } }

        public static void ApplyWinFrame(Form f, int radius)
        {
            if (IsWin11)
            {
                int v = DWMWCP_ROUND;
                if (DwmSetWindowAttribute(f.Handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref v, 4) != 0)
                {
                    int v2 = DWMWCP_ROUNDSMALL;
                    DwmSetWindowAttribute(f.Handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref v2, 4);
                }
            }
            else
            {
                using (GraphicsPath p = Rounded(new Rectangle(0, 0, f.Width - 1, f.Height - 1), radius))
                    f.Region = new Region(p);
            }
        }

        public static void DrawFrameBorder(Graphics g, int w, int h, int radius, Color c)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath p = Rounded(new Rectangle(0, 0, w - 1, h - 1), radius))
            using (Pen pen = new Pen(c)) g.DrawPath(pen, p);
        }

        public static ShadowHost AttachShadow(Form f, int margin, int radius)
        {
            ShadowHost sh = new ShadowHost(f, margin, IsWin11 ? 8 : radius);
            sh.Size = new Size(f.Width + margin * 2, f.Height + margin * 2);
            sh.Attach();
            return sh;
        }

        static void FadeInTick(object sender, EventArgs e)
        {
            System.Windows.Forms.Timer t = (System.Windows.Forms.Timer)sender;
            FadeState fs = (FadeState)t.Tag;
            fs.i++;
            fs.f.Opacity = (double)fs.i / fs.steps;
            if (fs.i >= fs.steps)
            {
                t.Stop(); t.Dispose();
                fs.f.Opacity = 1;
            }
        }
        class FadeState { public Form f; public int steps; public int i; }
        public static void FadeInOpacity(Form f, int ms = 300)
        {
            if (f == null) return;
            f.Opacity = 0;
            System.Windows.Forms.Timer t = new System.Windows.Forms.Timer();
            FadeState fs = new FadeState();
            fs.f = f;
            fs.steps = Math.Max(1, ms / 15);
            t.Tag = fs;
            t.Interval = 15;
            t.Tick += FadeInTick;
            t.Start();
        }

        // ---------- 系统原生窗口动画：弹出/最小化/恢复全部交给 Windows，不自绘 ----------
        [DllImport("user32.dll")] static extern bool AnimateWindow(IntPtr hWnd, int dwTime, int dwFlags);
        [DllImport("user32.dll", EntryPoint = "ShowWindow")] static extern bool ShowWindowNative(IntPtr h, int cmd);
        const int AW_CENTER = 0x0010, AW_ACTIVATE = 0x00020000;

        /// <summary>启动弹出：必须在窗口仍隐藏时调用；Windows 播放"中心展开"动画（同步阻塞 ms 毫秒）。</summary>
        public static bool NativePop(IntPtr hWnd, int ms)
        {
            try { return AnimateWindow(hWnd, ms, AW_CENTER | AW_ACTIVATE); }
            catch { return false; }
        }
        /// <summary>最小化：调用系统 SW_MINIMIZE，由 Windows 播放收进任务栏动画，并真正进入 iconic 态（任务栏图标可恢复）。</summary>
        public static void NativeMinimize(Form f)
        {
            if (f == null || !f.IsHandleCreated) return;
            try { ShowWindowNative(f.Handle, 6); }
            catch { try { f.WindowState = FormWindowState.Minimized; } catch { } }   // 兜底，避免静默失败
        }
        /// <summary>最大化：交给 Windows（SW_MAXIMIZE），带系统原生过渡动画，并真正进入 Maximized 态。</summary>
        public static void NativeMaximize(Form f)
        {
            if (f == null || !f.IsHandleCreated) return;
            try { ShowWindowNative(f.Handle, 3); }
            catch { try { f.WindowState = FormWindowState.Maximized; } catch { } }
        }
        /// <summary>还原：交给 Windows（SW_RESTORE）。</summary>
        public static void NativeRestoreUp(Form f)
        {
            if (f == null || !f.IsHandleCreated) return;
            try { ShowWindowNative(f.Handle, 9); }
            catch { try { f.WindowState = FormWindowState.Normal; } catch { } }
        }
        [DllImport("user32.dll")] static extern bool FlashWindowEx(ref FLASHWINFO pwfi);        [StructLayout(LayoutKind.Sequential)]
        struct FLASHWINFO { public uint cbSize; public IntPtr hwnd; public uint dwFlags; public uint uCount; public uint dwTimeout; }

        public static void FlashWindow(Form f)
        {
            if (!f.IsHandleCreated) return;
            FLASHWINFO fi = new FLASHWINFO();
            fi.cbSize = (uint)Marshal.SizeOf(typeof(FLASHWINFO));
            fi.hwnd = f.Handle;
            fi.dwFlags = 3;      // FLASHW_ALL
            fi.uCount = 5;
            fi.dwTimeout = 0;
            FlashWindowEx(ref fi);
        }

        class FlashState { public System.Windows.Forms.Timer t; public int n; public bool on; }

        public static void FlashBorder(Form f)
        {
            if (f.Tag is FlashState) return;
            FlashState st = new FlashState();
            f.Tag = st;
            st.t = new System.Windows.Forms.Timer();
            st.t.Interval = 240;
            st.t.Tick += (s, e) =>
            {
                st.n++;
                st.on = !st.on;
                f.Invalidate();
                if (st.n >= 8) { st.t.Stop(); st.t.Dispose(); f.Tag = null; f.Invalidate(); }
            };
            st.t.Start();
        }

        public static void DrawFlashBorder(Graphics g, Form f, int radius, Color c)
        {
            if (f.Tag is FlashState && ((FlashState)f.Tag).on)
                DrawFrameBorder(g, f.Width, f.Height, radius, c);
        }

        public static void BounceWindow(Form f)
        {
            if (f.Tag is string && (string)f.Tag == "bouncing") return;
            f.Tag = "bouncing";
            Point orig = f.Location;
            System.Windows.Forms.Timer t = new System.Windows.Forms.Timer();
            t.Interval = 26;
            int n = 0;
            t.Tick += (s, e) =>
            {
                n++;
                if (n <= 14)
                {
                    int dx = (n % 2 == 1) ? 7 : -7;
                    int dy = (n % 3 == 1) ? 3 : -3;
                    f.Location = new Point(orig.X + dx, orig.Y + dy);
                }
                else
                {
                    f.Location = orig;
                    t.Stop();
                    t.Dispose();
                    f.Tag = null;
                }
            };
            t.Start();
        }

        static Form curDlg;

        // 非模态打开弹窗：主窗体保持可拖动、可点击（点击后主程序+弹窗一起置前），
        // 但内容区用透明遮罩锁住，无法修改任何内容、无法触发任何操作。
        // 弹窗始终居中于主窗体，拖动主窗体时实时跟随重居中；同一时刻只允许一个弹窗（杜绝残留堆叠）。
        public static void OpenDialog(Form owner, Form dialog, Action onClosed = null)
        {
            OpenDialog(owner, dialog, onClosed, false);
        }

        // centerScreen = true：弹窗居中于所在显示器（主窗体最大化时也不会被挤到角落），拖动主窗体不再跟随重居中
        public static void OpenDialog(Form owner, Form dialog, Action onClosed, bool centerScreen)
        {
            OpenDialog(owner, dialog, onClosed, centerScreen, false);
        }

        // keepOthers = true：这个弹窗不和别的弹窗互斥。
        // 「全屏编辑提示词」就是这种：从它里面点胶囊看大图，大图关掉以后它自己必须还开着。
        public static void OpenDialog(Form owner, Form dialog, Action onClosed, bool centerScreen, bool keepOthers)
        {
            OpenDialog(owner, dialog, onClosed, centerScreen, keepOthers, true);
        }

        // lockOwner=false：只作信息展示的弹窗（失败原因/图像丢失说明）。不盖锁层、不抢焦点，
        // 主程序照常可点可用；否则用户点主界面只会点到半透明锁层上，看起来就是"卡死"。
        public static void OpenDialog(Form owner, Form dialog, Action onClosed, bool centerScreen, bool keepOthers, bool lockOwner)
        {
            if (owner == null || owner.IsDisposed)
            {
                dialog.Show();
                return;
            }
            // 若上一个弹窗仍开着（异常残留/点击外部未真正关闭），先强制关掉，避免堆叠出“点好几次确定才关完”。
            if (!keepOthers && curDlg != null && !curDlg.IsDisposed && curDlg != dialog)
            {
                try { curDlg.Close(); } catch { }
                try { curDlg.Dispose(); } catch { }
            }
            if (!keepOthers) curDlg = dialog;
            dialog.StartPosition = FormStartPosition.Manual; // 完全由 CenterOnOwner 决定位置，杜绝跑偏

            EventHandler h = null;
            h = (s, e) =>
            {
                try
                {
                    if (dialog.IsDisposed) return;
                    dialog.BringToFront();
                    if (Form.ActiveForm != dialog) dialog.Activate();
                }
                catch { }
            };
            if (lockOwner) owner.Activated += h;

            EventHandler locH = null;
            locH = (s, e) =>
            {
                try
                {
                    if (dialog.IsDisposed) return;
                    CenterOnOwner(dialog, owner, centerScreen);
                }
                catch { }
            };
            owner.LocationChanged += locH;
            owner.SizeChanged += locH;

            dialog.FormClosed += (s, e) =>
            {
                if (curDlg == dialog) curDlg = null;
                owner.Activated -= h;
                owner.LocationChanged -= locH;
                owner.SizeChanged -= locH;
                UnlockMain(owner);
                // 关闭弹窗后把主窗重新拉回前面：否则关闭一个置前的子窗，激活会掉给背后的其它程序，
                // 表现为「关掉大图/关掉删除确认后，主程序跑到最下层」。不置顶，只是拿回普通窗口里最前面的位置。
                try
                {
                    if (!owner.IsDisposed && owner.IsHandleCreated && owner.Visible &&
                        owner.WindowState != FormWindowState.Minimized)
                    {
                        owner.Activate();
                        SetWindowPos(owner.Handle, IntPtr.Zero, 0, 0, 0, 0, 0x0002 | 0x0001); // SWP_NOMOVE | SWP_NOSIZE
                    }
                }
                catch { }
                if (onClosed != null) { try { onClosed(); } catch { } }
                dialog.Dispose();
            };

            if (lockOwner)
            {
                LockMain(owner, dialog);
                dialog.Show(owner);
            }
            else
            {
                // 独立显示：不带 owner，才不会永远压在主程序上面、也不会把主程序挡成"点不动"
                dialog.Show();
            }
            CenterOnOwner(dialog, owner, centerScreen);
            try
            {
                if (!dialog.IsDisposed) { dialog.BringToFront(); dialog.Activate(); }
            }
            catch { }
        }

        static void CenterOnOwner(Form dialog, Form owner, bool centerScreen)
        {
            if (dialog == null || owner == null || owner.IsDisposed) return;
            int x, y;
            if (centerScreen)
            {
                Rectangle wa = Screen.FromHandle(owner.IsHandleCreated ? owner.Handle : IntPtr.Zero).WorkingArea;
                x = wa.Left + (wa.Width - dialog.Width) / 2;
                y = wa.Top + (wa.Height - dialog.Height) / 2;
            }
            else
            {
                x = owner.Left + (owner.Width - dialog.Width) / 2;
                y = owner.Top + (owner.Height - dialog.Height) / 2;
            }
            dialog.Location = new Point(x, y);
        }

        // 半透明锁层：一个独立的半透明分层窗口盖在主窗体客户区上，由 DWM 统一合成。
        // 旧做法是「DrawToBitmap 抓整窗快照再画到遮罩面板上」，快照会抓到未画完的控件（白块）、
        // 弹窗失焦重激活时反复重画画布，正是"闪烁/镂空"的来源，这里彻底去掉快照。
        class LockOverlay : Form
        {
            public LockOverlay()
            {
                FormBorderStyle = FormBorderStyle.None;
                ShowInTaskbar = false;
                StartPosition = FormStartPosition.Manual;
                BackColor = Color.White;
                // 不要盖成 55% 白，那看起来就是整窗卡死；只留一层很淡的底，点击时靠弹窗闪边提醒
                Opacity = 0.22;
                DoubleBuffered = true;
                Cursor = Cursors.Default;
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            }
            protected override bool ShowWithoutActivation { get { return true; } }
            protected override void WndProc(ref Message m)
            {
                // 点遮罩绝不激活主窗，主窗上的按钮、菜单、滚动条一概不响应
                if (m.Msg == 0x0021) { m.Result = (IntPtr)3; return; }   // WM_MOUSEACTIVATE / MA_NOACTIVATE
                base.WndProc(ref m);
            }
            protected override CreateParams CreateParams
            {
                get
                {
                    CreateParams cp = base.CreateParams;
                    cp.ExStyle |= 0x08000000 | 0x00000080;    // WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW
                    return cp;
                }
            }
        }

        static Form lockMask;
        static EventHandler lockResizeH;
        static EventHandler lockMoveH;

        static void SyncLock(Form owner)
        {
            if (lockMask == null || owner == null || owner.IsDisposed) return;
            if (owner.WindowState == FormWindowState.Minimized) { lockMask.Hide(); return; }
            Point p = owner.PointToScreen(new Point(0, Program.TitleH));
            lockMask.Bounds = new Rectangle(p.X, p.Y, owner.ClientSize.Width, owner.ClientSize.Height - Program.TitleH);
            if (!lockMask.Visible) lockMask.Show();
            lockMask.BringToFront();      // 永远压在弹窗下面（弹窗在 OpenDialog 里随后 Show 并置前）
        }

        static void LockMain(Form owner, Form dialog)
        {
            UnlockMain(owner);
            LockOverlay lo = new LockOverlay();
            lockMask = lo;
            lo.Owner = owner;
            lo.Tag = dialog;
            // 点在锁层上：主程序不做任何动作，只把前面的弹窗提到最前并闪一下边框
            lo.MouseDown += (s, e) =>
            {
                try
                {
                    Form d = lo.Tag as Form;
                    if (d != null && !d.IsDisposed)
                    {
                        d.BringToFront();
                        d.Activate();
                        FlashBorder(d);
                    }
                }
                catch { }
            };
            SyncLock(owner);
            lockResizeH = (s, e) => { try { SyncLock(owner); } catch { } };
            lockMoveH = (s, e) => { try { SyncLock(owner); } catch { } };
            owner.Resize += lockResizeH;
            owner.Move += lockMoveH;
        }

        static void UnlockMain(Form owner)
        {
            if (owner != null)
            {
                if (lockResizeH != null) { owner.Resize -= lockResizeH; lockResizeH = null; }
                if (lockMoveH != null) { owner.Move -= lockMoveH; lockMoveH = null; }
            }
            if (lockMask != null)
            {
                try { lockMask.Hide(); } catch { }
                lockMask.Dispose();
                lockMask = null;
            }
        }
    }

    public class ShadowHost : Form
    {
        Form target;
        int margin, baseRadius;
        IntPtr dib = IntPtr.Zero, hdcMem = IntPtr.Zero, oldObj = IntPtr.Zero;
        public bool Animating;   // 动画期间为 true：只跟随位置、不重建阴影位图，避免卡顿

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
        static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
        static extern IntPtr GetWindowLongPtr32(IntPtr hWnd, int nIndex);
        static IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex)
        { return IntPtr.Size == 8 ? GetWindowLongPtr64(hWnd, nIndex) : GetWindowLongPtr32(hWnd, nIndex); }
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
        static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
        [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
        static extern IntPtr SetWindowLongPtr32(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
        static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong)
        { return IntPtr.Size == 8 ? SetWindowLongPtr64(hWnd, nIndex, dwNewLong) : SetWindowLongPtr32(hWnd, nIndex, dwNewLong); }
        [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hWnd);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
        [DllImport("user32.dll", EntryPoint = "ShowWindow")] static extern bool ShowWindowEx(IntPtr h, int cmd);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
        [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr hDC);
        [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr hDC);
        [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr hDC, IntPtr hObject);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr hObject);
        [DllImport("gdi32.dll")] static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFO pbmi, uint usage, out IntPtr ppvBits, IntPtr hSection, uint offset);
        [DllImport("msvcrt.dll", EntryPoint = "memset", CallingConvention = CallingConvention.Cdecl)]
        static extern IntPtr Memset(IntPtr dest, int c, IntPtr count);
        [DllImport("user32.dll")] static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize, IntPtr hdcSrc, ref POINT pptSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [StructLayout(LayoutKind.Sequential)] struct POINT { public int x; public int y; }
        [StructLayout(LayoutKind.Sequential)] struct SIZE { public int cx; public int cy; }
        [StructLayout(LayoutKind.Sequential)] struct BLENDFUNCTION { public byte BlendOp; public byte BlendFlags; public byte SourceConstantAlpha; public byte AlphaFormat; }
        [StructLayout(LayoutKind.Sequential)]
        struct BITMAPINFOHEADER { public uint biSize; public int biWidth; public int biHeight; public ushort biPlanes; public ushort biBitCount; public uint biCompression; public uint biSizeImage; public int biXPelsPerMeter; public int biYPelsPerMeter; public uint biClrUsed; public uint biClrImportant; }
        [StructLayout(LayoutKind.Sequential)]
        struct BITMAPINFO { public BITMAPINFOHEADER bmiHeader; }

        const int GWL_EXSTYLE = -20;
        const int GWL_STYLE = -16;
        const int WS_EX_LAYERED = 0x00080000;
        const int WS_EX_NOACTIVATE = 0x08000000;   // 阴影窗口绝不抢前台，否则任务栏图标要点两次才最小化
        const int WS_EX_TOOLWINDOW = 0x00000080;
        const int ULW_ALPHA = 2;
        const int AC_SRC_ALPHA = 1;

        public ShadowHost(Form target, int margin, int baseRadius)
        {
            this.target = target;
            this.margin = margin;
            this.baseRadius = baseRadius;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            BackColor = Color.Black;
            CreateControl();
            StripSystemFrame();
            int ex = unchecked((int)GetWindowLongPtr(Handle, GWL_EXSTYLE).ToInt64());
            SetWindowLongPtr(Handle, GWL_EXSTYLE, new IntPtr(ex | WS_EX_LAYERED | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW));
        }

        /// <summary>
        /// 阴影层是分层窗口，内容全靠 UpdateLayeredWindow 画。只要样式里还留着
        /// CAPTION/SYSMENU/MIN/MAX 这几位，Windows 就会在它的非客户区补画一条
        /// 系统标题栏——那条浅蓝横条和系统 □✕ 就是这么冒出来的（它是主窗外围 40px 的阴影窗，
        /// 不是主窗，所以查主窗样式永远是干净的）。这里把这几位全部擦掉。
        /// </summary>
        void StripSystemFrame()
        {
            if (!IsHandleCreated) return;
            int st = unchecked((int)GetWindowLongPtr(Handle, GWL_STYLE).ToInt64());
            st &= ~(0x00C00000 | 0x000F0000);      // CAPTION | SYSMENU/THICKFRAME/MIN/MAX
            SetWindowLongPtr(Handle, GWL_STYLE, new IntPtr(st));
            SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0, 0x0020 | 0x0001 | 0x0002 | 0x0010);
            // FRAMECHANGED|NOMOVE|NOSIZE|NOACTIVATE
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            StripSystemFrame();
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;   // 创建即不激活，前台始终留给主窗口
                cp.Style &= ~0x00C00000;                             // 不要标题栏
                cp.Style &= ~0x000F0000;                             // 不要系统菜单/边框/最小化/最大化按钮位
                cp.Style |= unchecked((int)0x80000000);              // WS_POPUP：无父、无系统装饰
                return cp;
            }
        }

        /// <summary>显示阴影且绝不抢前台：用原生 SW_SHOWNOACTIVATE，WinForms 的 Show() 会主动激活窗口导致任务栏图标要点两次。</summary>
        void ShowNoActivate()
        {
            if (!IsHandleCreated) return;
            ShowWindowEx(Handle, 4);          // 4 = SW_SHOWNOACTIVATE
        }
        bool IsShown { get { return IsHandleCreated && IsWindowVisible(Handle); } }

        public void Attach()
        {
            target.Shown += (s, e) => { Sync(); Render(); ShowNoActivate(); SetBehind(); };
            target.Activated += (s, e) => { if (IsShown) SetBehind(); };
            target.LocationChanged += (s, e) => Sync();
            target.SizeChanged += (s, e) =>
            {
                if (target.WindowState == FormWindowState.Minimized) { Hide(); return; }
                Size = new Size(target.Width + margin * 2, target.Height + margin * 2);
                Sync();
                if (Animating) return;      // 动画期间只跟随尺寸/位置，不重建阴影位图（避免卡顿）
                Render();                 // 先渲染好透明阴影位图再显示，避免恢复瞬间显示黑块
                if (!IsShown) ShowNoActivate();
                SetBehind();              // 立即置于主窗体后方
            };
            target.FormClosed += (s, e) => Close();
            target.VisibleChanged += (s, e) => { if (!target.Visible) Hide(); };
        }

        public void ReRender()
        {
            if (target == null || target.IsDisposed || target.WindowState == FormWindowState.Minimized) return;
            Size = new Size(target.Width + margin * 2, target.Height + margin * 2);
            Sync();
            Render();
            if (!IsShown) ShowNoActivate();
            SetBehind();
        }

        void SetBehind()
        {
            if (!IsHandleCreated || !target.IsHandleCreated) return;
            SetWindowPos(Handle, target.Handle, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
        }

        void Sync()
        {
            if (!IsHandleCreated) return;
            Location = new Point(target.Left - margin, target.Top - margin);
        }

        void Render()
        {
            ReleaseDib();
            int w = Width, h = Height;
            if (w < 4 || h < 4) return;
            BITMAPINFO bmi = new BITMAPINFO();
            bmi.bmiHeader.biSize = 40;
            bmi.bmiHeader.biWidth = w;
            bmi.bmiHeader.biHeight = -h;
            bmi.bmiHeader.biPlanes = 1;
            bmi.bmiHeader.biBitCount = 32;
            bmi.bmiHeader.biCompression = 0;
            IntPtr bits;
            dib = CreateDIBSection(IntPtr.Zero, ref bmi, 0, out bits, IntPtr.Zero, 0);
            if (dib == IntPtr.Zero) return;
            hdcMem = CreateCompatibleDC(IntPtr.Zero);
            oldObj = SelectObject(hdcMem, dib);
            Memset(bits, 0, new IntPtr((long)w * h * 4));
            using (Graphics g = Graphics.FromHdc(hdcMem))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                int layers = 21;
                int holeR = Ui.IsWin11 ? 6 : Math.Max(2, baseRadius - 2);
                for (int i = 0; i < layers; i++)
                {
                    double t = (double)i / (layers - 1);
                    int e = (int)(margin * t);
                    Rectangle rc = new Rectangle(margin - e, margin - e, w - 2 * margin + 2 * e, h - 2 * margin + 2 * e);
                    if (rc.Width < 2 || rc.Height < 2) continue;
                    using (GraphicsPath p = Ui.Rounded(rc, holeR + e))
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(1, 52, 56, 70)))
                        g.FillPath(b, p);
                }
                g.CompositingMode = CompositingMode.SourceCopy;
                using (GraphicsPath p = Ui.Rounded(new Rectangle(margin, margin, w - 2 * margin, h - 2 * margin), holeR))
                using (SolidBrush b = new SolidBrush(Color.FromArgb(0, 0, 0, 0)))
                    g.FillPath(b, p);
            }
            Apply();
        }

        void Apply()
        {
            if (dib == IntPtr.Zero || hdcMem == IntPtr.Zero) return;
            IntPtr hdcScreen = GetDC(IntPtr.Zero);
            POINT ptSrc = new POINT(); ptSrc.x = 0; ptSrc.y = 0;
            POINT ptDst = new POINT(); ptDst.x = Location.X; ptDst.y = Location.Y;
            SIZE sz = new SIZE(); sz.cx = Width; sz.cy = Height;
            BLENDFUNCTION bf = new BLENDFUNCTION();
            bf.BlendOp = 0; bf.BlendFlags = 0; bf.SourceConstantAlpha = 255; bf.AlphaFormat = AC_SRC_ALPHA;
            UpdateLayeredWindow(Handle, hdcScreen, ref ptDst, ref sz, hdcMem, ref ptSrc, 0, ref bf, ULW_ALPHA);
            ReleaseDC(IntPtr.Zero, hdcScreen);
        }

        void ReleaseDib()
        {
            if (dib != IntPtr.Zero)
            {
                if (hdcMem != IntPtr.Zero)
                {
                    if (oldObj != IntPtr.Zero) SelectObject(hdcMem, oldObj);
                    DeleteDC(hdcMem);
                    hdcMem = IntPtr.Zero;
                    oldObj = IntPtr.Zero;
                }
                DeleteObject(dib);
                dib = IntPtr.Zero;
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0084) { m.Result = new IntPtr(-1); return; }   // 整层鼠标穿透
            if (m.Msg == 0x0083 && m.WParam != IntPtr.Zero) { m.Result = IntPtr.Zero; return; }
            if (m.Msg == 0x0085) { m.Result = IntPtr.Zero; return; }
            base.WndProc(ref m);
        }

        protected override void Dispose(bool disposing)
        {
            ReleaseDib();
            base.Dispose(disposing);
        }
    }

    public class GradientButton : Control
    {
        public Color C1 = Color.FromArgb(108, 140, 245);
        public Color C2 = Color.FromArgb(150, 118, 255);
        public int Radius = 12;
        public bool Flat = false;
        bool hover, pressed;

        public GradientButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
            Font = new Font("微软雅黑", 10F, FontStyle.Bold);
            ForeColor = Color.White;
        }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { pressed = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            Rectangle rc = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = Ui.Rounded(rc, Radius))
            {
                if (!Enabled)
                {
                    using (SolidBrush br = new SolidBrush(Color.FromArgb(241, 243, 247))) g.FillPath(br, path);
                    TextRenderer.DrawText(g, Text, Font, rc, Color.FromArgb(160, 168, 184), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                    return;
                }
                if (Flat)
                {
                    Color bg = hover ? Color.FromArgb(231, 237, 251) : Color.FromArgb(241, 244, 250);
                    using (SolidBrush br = new SolidBrush(bg)) g.FillPath(br, path);
                    using (Pen pen = new Pen(Color.FromArgb(226, 231, 242))) g.DrawPath(pen, path);
                    TextRenderer.DrawText(g, Text, Font, rc, Color.FromArgb(59, 66, 87), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                }
                else
                {
                    Color c1 = C1, c2 = C2;
                    if (hover) { c1 = Ui.Shift(C1, 0.08f); c2 = Ui.Shift(C2, 0.08f); }
                    if (pressed) { c1 = Ui.Shift(C1, -0.1f); c2 = Ui.Shift(C2, -0.1f); }
                    using (LinearGradientBrush br = new LinearGradientBrush(rc, c1, c2, 35f)) g.FillPath(br, path);
                    using (Pen pen = new Pen(Color.FromArgb(36, 255, 255, 255))) g.DrawPath(pen, path);
                    TextRenderer.DrawText(g, Text, Font, rc, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                }
            }
        }
    }

    public class FieldBox : Panel
    {
        public TextBox Inner;
        Color fill = Color.FromArgb(246, 248, 252);
        Color border = Color.FromArgb(226, 231, 242);
        Color focusBorder = Color.FromArgb(108, 140, 245);

        public FieldBox()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Color.White;
            Inner = new TextBox();
            Inner.BorderStyle = BorderStyle.None;
            Inner.Font = new Font("微软雅黑", 10F);
            Inner.BackColor = fill;
            Inner.ForeColor = Color.FromArgb(31, 41, 55);
            Controls.Add(Inner);
            Inner.Enter += (s, e) => Invalidate();
            Inner.Leave += (s, e) => Invalidate();
        }
        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            int ih = Math.Max(20, Inner.Font.Height + 4);
            Inner.Location = new Point(12, (Height - ih) / 2);
            Inner.Width = Math.Max(10, Width - 26);
            Inner.Height = ih;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle rc = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = Ui.Rounded(rc, 10))
            {
                using (SolidBrush br = new SolidBrush(fill)) g.FillPath(br, path);
                using (Pen pen = new Pen(Inner.Focused ? focusBorder : border)) g.DrawPath(pen, path);
            }
        }
        public string TextValue { get { return Inner.Text.Trim(); } set { Inner.Text = value ?? ""; } }
        bool roHooked;
        public void SetReadOnly(bool ro)
        {
            Inner.ReadOnly = ro;
            Inner.TabStop = !ro;
            if (ro && !roHooked)
            {
                roHooked = true;
                Inner.Cursor = Cursors.Arrow;
                Inner.MouseDown += (s, e) =>
                {
                    Inner.SelectionStart = Inner.TextLength;
                    Inner.SelectionLength = 0;
                    try { Form f = FindForm(); if (f != null) f.ActiveControl = null; } catch { }
                };
            }
        }
        public Label ErrHint;
        System.Windows.Forms.Timer errTimer;
        public void SetDigitsOnly()
        {
            Inner.KeyPress += (s, e) =>
            {
                if (!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar))
                {
                    e.Handled = true;
                    ShowErrHint();
                }
                else if (ErrHint != null) ErrHint.Visible = false;
            };
            Inner.TextChanged += (s, e) => { if (ErrHint != null) { ErrHint.Visible = false; if (errTimer != null) errTimer.Stop(); } };
            Inner.Leave += (s, e) =>
            {
                if (ErrHint != null) { ErrHint.Visible = false; if (errTimer != null) errTimer.Stop(); }
                string t = Inner.Text;
                if (string.IsNullOrEmpty(t)) return;
                string clean = "";
                foreach (char c in t) if (char.IsDigit(c)) clean += c;
                if (clean != t) Inner.Text = clean;
            };
        }
        void ShowErrHint()
        {
            if (ErrHint == null) return;
            ErrHint.Visible = true;
            if (errTimer == null)
            {
                errTimer = new System.Windows.Forms.Timer();
                errTimer.Interval = 1800;
                errTimer.Tick += (s, e2) => { ErrHint.Visible = false; errTimer.Stop(); };
            }
            errTimer.Stop();
            errTimer.Start();
        }
    }

    public class NavButton : Control
    {
        public int IconKind; // 0 新建项目, 1 批量重命名
        bool active;
        public bool Active { get { return active; } set { active = value; Invalidate(); } }
        bool hover;

        public NavButton(string text, int iconKind)
        {
            Text = text;
            IconKind = iconKind;
            Font = new Font("微软雅黑", 10.5F, FontStyle.Bold);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
        }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            Rectangle rc = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = Ui.Rounded(rc, 12))
            {
                Color iconColor, textColor;
                if (Active)
                {
                    using (LinearGradientBrush br = new LinearGradientBrush(rc, Color.FromArgb(108, 140, 245), Color.FromArgb(150, 118, 255), 35f))
                        g.FillPath(br, path);
                    iconColor = Color.White; textColor = Color.White;
                }
                else
                {
                    using (SolidBrush br = new SolidBrush(hover ? Color.FromArgb(240, 243, 249) : Color.Transparent))
                        g.FillPath(br, path);
                    iconColor = Color.FromArgb(122, 132, 156);
                    textColor = hover ? Color.FromArgb(52, 58, 78) : Color.FromArgb(96, 106, 132);
                }
                DrawIcon(g, iconColor);
                TextRenderer.DrawText(g, Text, Font, new Rectangle(52, 0, Width - 56, Height), textColor, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            }
        }
        void DrawIcon(Graphics g, Color c)
        {
            GraphicsState st = g.Save();
            g.TranslateTransform(15, (Height - 24) / 2f);
            using (Pen pen = new Pen(c, 1.8f))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                pen.LineJoin = LineJoin.Round;
                if (IconKind == 0)
                {
                    using (GraphicsPath fp = new GraphicsPath())
                    {
                        fp.AddLine(2, 6, 8, 6);
                        fp.AddLine(10, 8, 21, 8);
                        fp.AddLine(21, 20, 2, 20);
                        fp.CloseFigure();
                        g.DrawPath(pen, fp);
                    }
                    g.DrawLine(pen, 17, 12, 17, 17);
                    g.DrawLine(pen, 14.5f, 14.5f, 19.5f, 14.5f);
                }
                else if (IconKind == 2)
                {
                    // 生成图像：相框 + 山 + 太阳
                    g.DrawRectangle(pen, 2, 5, 19, 14);
                    g.DrawLine(pen, 5, 16, 10, 10);
                    g.DrawLine(pen, 10, 10, 15, 16);
                    g.DrawEllipse(pen, 14, 8, 4, 4);
                }
                else
                {
                    // 批量重命名：直接用「Aa」字形表示改文字名（原来画的那支笔跟重命名没关系，已换掉）
                    using (Font f = new Font("微软雅黑", 10.5F, FontStyle.Bold))
                    {
                        // 按紧排版量字形盒，把墨迹中心摆到 22×22 图标槽正中 (11,11)，避免 TextRenderer 居中把字裁到左边
                        StringFormat sf = StringFormat.GenericTypographic;
                        SizeF sz = g.MeasureString("Aa", f, new PointF(0, 0), sf);
                        System.Drawing.Text.TextRenderingHint hint = g.TextRenderingHint;
                        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                        using (SolidBrush br = new SolidBrush(c))
                            g.DrawString("Aa", f, br, 13f - sz.Width / 2f, 13f - sz.Height / 2f, sf);
                        g.TextRenderingHint = hint;
                    }
                }
            }
            g.Restore(st);
        }
    }

    public class TitleButton : Control
    {
        public char Kind; // 'm' 最小化, 'x' 关闭, 'f' 放大/还原
        public bool MaxState;
        bool hover;
        public TitleButton(char kind)
        {
            Kind = kind;
            Size = new Size(44, 44);
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
        }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle rc = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = Ui.Rounded(rc, 10))
            {
                if (hover)
                {
                    Color bg = (Kind == 'x') ? Color.FromArgb(253, 235, 235) : Color.FromArgb(241, 244, 250);
                    using (SolidBrush br = new SolidBrush(bg)) g.FillPath(br, path);
                }
                using (Pen pen = new Pen(hover && Kind == 'x' ? Color.FromArgb(229, 72, 77) : Color.FromArgb(90, 100, 124), 1.7f))
                {
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;
                    if (Kind == 'm') g.DrawLine(pen, 15, 22, 29, 22);
                    else if (Kind == 'f')
                    {
                        if (!MaxState)
                        {
                            g.DrawRectangle(pen, 15, 15, 14, 13);
                        }
                        else
                        {
                            // 还原图标：前面一个方窗，右上外侧再单独画一个「角」（与方窗之间留空隙、不连起来）
                            g.DrawRectangle(pen, 14, 17, 12, 11);
                            g.DrawLine(pen, 21, 13, 29, 13);
                            g.DrawLine(pen, 29, 13, 29, 20);
                        }
                    }
                    else
                    {
                        g.DrawLine(pen, 15, 15, 29, 29);
                        g.DrawLine(pen, 29, 15, 15, 29);
                    }
                }
            }
        }
    }

    public class NumberStylePicker : Control
    {
        public string Value = "1";
        public event EventHandler ValueChanged;
        PickerPopup popup;

        public NumberStylePicker()
        {
            Font = new Font("微软雅黑", 10F);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            Rectangle rc = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = Ui.Rounded(rc, 10))
            {
                using (SolidBrush br = new SolidBrush(Color.FromArgb(246, 248, 252))) g.FillPath(br, path);
                using (Pen pen = new Pen(Color.FromArgb(226, 231, 242))) g.DrawPath(pen, path);
            }
            TextRenderer.DrawText(g, Program.StyleName(Value), Font, new Rectangle(14, 0, Width - 34, Height), Color.FromArgb(31, 41, 55), TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            using (Pen pen = new Pen(Color.FromArgb(120, 130, 152), 1.8f))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                g.DrawLine(pen, Width - 22, Height / 2 - 3, Width - 15, Height / 2 + 1);
                g.DrawLine(pen, Width - 15, Height / 2 + 1, Width - 8, Height / 2 - 3);
            }
        }
        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            if (popup != null && popup.Visible) { popup.Close(); popup = null; return; }
            popup = new PickerPopup(this);
            popup.Show(this.FindForm());
        }
        public void NotifyChanged()
        {
            if (ValueChanged != null) ValueChanged(this, EventArgs.Empty);
        }
    }

    class PickerPopup : Form
    {
        public PickerPopup(NumberStylePicker owner)
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            BackColor = Color.White;
            int ih = 40;
            int w = Math.Max(170, owner.Width);
            Size = new Size(w + 10, 3 * ih + 14);
            string[] vals = { "1", "01", "一" };
            for (int i = 0; i < 3; i++)
            {
                PopupItem it = new PopupItem(vals[i], Program.StyleName(vals[i]), owner.Value == vals[i]);
                it.Location = new Point(5, 5 + i * ih);
                it.Size = new Size(Width - 10, ih - 6);
                string v = vals[i];
                it.Picked += (s, ev) => { owner.Value = v; owner.Invalidate(); owner.NotifyChanged(); this.Close(); };
                Controls.Add(it);
            }
            Point pt = owner.PointToScreen(new Point(0, owner.Height + 4));
            Location = pt;
            Deactivate += (s, e) => Close();
            Paint += (s, e) =>
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                Rectangle outer = new Rectangle(0, 0, Width - 1, Height - 1);
                Rectangle inner = new Rectangle(2, 2, Width - 5, Height - 5);
                using (GraphicsPath po = Ui.Rounded(outer, 6))
                using (LinearGradientBrush br = new LinearGradientBrush(outer, Color.FromArgb(172, 166, 250), Color.FromArgb(138, 152, 248), 90f))
                    g.FillPath(br, po);
                using (GraphicsPath pi = Ui.Rounded(inner, 4))
                using (SolidBrush wh = new SolidBrush(Color.White))
                    g.FillPath(wh, pi);
            };
        }
    }

    class PopupItem : Control
    {
        public bool Selected;
        public event EventHandler Picked;
        bool hover;
        public PopupItem(string val, string label, bool selected)
        {
            Value = val;
            Text = label;
            Selected = selected;
            Font = new Font("微软雅黑", 10F);
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
        }
        public string Value;
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); if (Picked != null) Picked(this, EventArgs.Empty); }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            Rectangle rc = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = Ui.Rounded(rc, 9))
            {
                if (Selected || hover)
                    using (SolidBrush br = new SolidBrush(hover ? Color.FromArgb(231, 237, 251) : Color.FromArgb(240, 244, 252))) g.FillPath(br, path);
                if (Selected)
                {
                    using (Pen pen = new Pen(Color.FromArgb(108, 140, 245), 2f))
                    {
                        pen.StartCap = LineCap.Round;
                        pen.EndCap = LineCap.Round;
                        g.DrawLine(pen, 14, Height / 2, 18, Height / 2 + 4);
                        g.DrawLine(pen, 18, Height / 2 + 4, 26, Height / 2 - 4);
                    }
                }
            }
            TextRenderer.DrawText(g, Text, Font, new Rectangle(Selected ? 36 : 16, 0, Width - 40, Height), Color.FromArgb(31, 41, 55), TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }

    // 在线更新：从公网 JSON 清单读取版本信息，下载新版单文件 exe，校验 SHA256 后
    // 由一个临时批处理在程序退出后覆盖自身并重启。只替换程序本体，history / config / 项目目录不动。
    public class UpdateInfo
    {
        public string version;
        public string notes;
        public string url;
        public List<string> urls;
        public string sha256;
        public bool mandatory;
    }

    public static class UpdateService
    {
        // 更新清单固定走这里，不再让用户在「配置 API」里手填；换仓库只改这一行。
        public const string ManifestUrl = "https://gitee.com/AISeriesProductionTool/ai-series-update/raw/master/update.json";
        static volatile bool checking;

        public static string CurrentVersion()
        {
            try
            {
                Version v = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
                if (v != null && v.Major > 0) return v.Major + "." + v.Minor + "." + v.Build + "." + v.Revision;
            }
            catch { }
            return "0.0.0.0";
        }

        public static void Check(bool silent)
        {
            // 更新地址内置，不再读 Program.Config.update.manifest，也不让用户填
            string url = ManifestUrl;
            if (checking) return;
            checking = true;
            if (!silent) Toast.Show("正在检查更新…", null);
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                string err = null;
                UpdateInfo info = null;
                byte[] raw = HttpGet(url, out err);
                if (raw != null)
                {
                    try
                    {
                        string json = Encoding.UTF8.GetString(raw).Trim('\uFEFF', ' ', '\r', '\n', '\t');
                        info = Program.Json.Deserialize<UpdateInfo>(json);
                    }
                    catch (Exception ex) { err = ex.Message; }
                }
                OnUi(delegate
                {
                    checking = false;
                    if (info == null)
                    {
                        if (!silent) DesignDialog.ShowError("检查更新失败：" + (string.IsNullOrEmpty(err) ? "更新清单格式不正确" : err) + "\n\n请确认这个地址能在浏览器里直接打开，并返回 JSON 文本。", "软件更新");
                        return;
                    }
                    if (!IsNewer(info.version, CurrentVersion()))
                    {
                        if (!silent) DesignDialog.ShowInfo("当前已是最新版本 v" + CurrentVersion() + "。", "软件更新");
                        return;
                    }
                    string notes = string.IsNullOrEmpty(info.notes) ? "（这次更新没有写更新说明）" : info.notes.Trim();
                    string text = "发现新版本 v" + info.version + "（当前 v" + CurrentVersion() + "）。\n\n更新内容：\n" + notes +
                        "\n\n更新只替换程序本体，历史图片、config.json 和项目文件夹都不会动。是否现在下载并更新？";
                    DesignDialog.ShowConfirm(text, "软件更新", delegate { DownloadAndApply(info); });
                });
            });
        }

        static void DownloadAndApply(UpdateInfo info)
        {
            Toast.Show("正在下载新版本 v" + info.version + "…", null);
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                string err = null;
                string file = DownloadOne(info, out err);
                OnUi(delegate
                {
                    if (string.IsNullOrEmpty(file))
                    {
                        DesignDialog.ShowError("更新下载失败：" + (string.IsNullOrEmpty(err) ? "没有可用的下载地址" : err) + "\n\n可以稍后再试，或到发布页手动下载安装包覆盖安装。", "软件更新");
                        return;
                    }
                    Toast.Show("更新已下载，正在重启完成更新…", null);
                    System.Windows.Forms.Timer t = new System.Windows.Forms.Timer { Interval = 700 };
                    t.Tick += delegate
                    {
                        t.Stop(); t.Dispose();
                        ApplyUpdate(file);
                    };
                    t.Start();
                });
            });
        }

        static string DownloadOne(UpdateInfo info, out string err)
        {
            err = null;
            List<string> all = new List<string>();
            if (!string.IsNullOrEmpty(info.url)) all.Add(info.url.Trim());
            if (info.urls != null)
                foreach (string u in info.urls)
                    if (!string.IsNullOrEmpty(u)) all.Add(u.Trim());
            if (all.Count == 0) { err = "清单里没有 url"; return null; }

            string dir = Path.Combine(Path.GetTempPath(), "AiSeriesUpdate");
            try { Directory.CreateDirectory(dir); } catch { }
            string file = Path.Combine(dir, "AI-Series-Production-Tool_" + Safe(info.version) + "_" + DateTime.Now.Ticks + ".exe");
            foreach (string u in all)
            {
                if (!u.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !u.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) continue;
                byte[] data = HttpGet(u, out err);
                if (data == null) continue;
                if (data.Length < 50000) { err = "下载到的文件太小（" + data.Length + " 字节），不像完整程序"; continue; }
                if (data[0] != (byte)'M' || data[1] != (byte)'Z') { err = "下载到的不是 Windows 程序文件"; continue; }
                try { File.WriteAllBytes(file, data); } catch (Exception ex) { err = "写入临时文件失败：" + ex.Message; continue; }
                if (!string.IsNullOrEmpty(info.sha256))
                {
                    string got = Sha256(file);
                    if (!string.Equals(got, info.sha256.Trim(), StringComparison.OrdinalIgnoreCase))
                    {
                        err = "SHA256 校验不一致，已拒绝运行（文件可能没下载完整或被改动）";
                        try { File.Delete(file); } catch { }
                        continue;
                    }
                }
                err = null;
                return file;
            }
            return null;
        }

        static void ApplyUpdate(string newExe)
        {
            string target = Application.ExecutablePath;
            string dir = Path.GetDirectoryName(target);
            try
            {
                if (CanWriteDir(dir)) StartBatch(newExe, target);
                else StartElevated(newExe, target);
            }
            catch (Exception ex)
            {
                DesignDialog.ShowError("启动更新失败：" + ex.Message + "\n\n可以关闭程序后，手动用新下载的程序替换原文件。", "软件更新");
            }
        }

        static bool CanWriteDir(string dir)
        {
            try
            {
                string p = Path.Combine(dir, ".update_write_test_" + DateTime.Now.Ticks);
                using (FileStream fs = new FileStream(p, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { fs.WriteByte(0); }
                File.Delete(p);
                return true;
            }
            catch { return false; }
        }

        static void StartBatch(string newExe, string target)
        {
            string dir = Path.Combine(Path.GetTempPath(), "AiSeriesUpdate");
            Directory.CreateDirectory(dir);
            string bat = Path.Combine(dir, "apply_" + DateTime.Now.Ticks + ".cmd");
            StringBuilder sb = new StringBuilder();
            sb.Append("@echo off\r\n");
            sb.Append("set \"NEW=").Append(newExe).Append("\"\r\n");
            sb.Append("set \"TGT=").Append(target).Append("\"\r\n");
            sb.Append("set /a TRY=0\r\n");
            sb.Append(":retry\r\n");
            sb.Append("copy /Y \"%NEW%\" \"%TGT%\" >nul 2>&1\r\n");
            sb.Append("if not errorlevel 1 goto ok\r\n");
            sb.Append("set /a TRY+=1\r\n");
            sb.Append("if %TRY% GEQ 90 goto fail\r\n");
            sb.Append("ping -n 2 127.0.0.1 >nul\r\n");
            sb.Append("goto retry\r\n");
            sb.Append(":ok\r\n");
            sb.Append("start \"\" \"%TGT%\"\r\n");
            sb.Append("del \"%NEW%\" >nul 2>&1\r\n");
            sb.Append("del \"%~f0\" >nul 2>&1\r\n");
            sb.Append("exit /b 0\r\n");
            sb.Append(":fail\r\n");
            sb.Append("exit /b 1\r\n");
            // cmd 在中文 Windows 上按 ANSI 读批处理，用 Default 才能正确识别中文安装路径
            File.WriteAllText(bat, sb.ToString(), Encoding.Default);

            ProcessStartInfo psi = new ProcessStartInfo("cmd.exe", "/c \"" + bat + "\"");
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.WindowStyle = ProcessWindowStyle.Hidden;
            Process.Start(psi);
            Application.Exit();
        }

        static void StartElevated(string newExe, string target)
        {
            // 安装目录在 Program Files 等只读位置时，才需要管理员权限；普通用户目录不会弹 UAC。
            string dir = Path.Combine(Path.GetTempPath(), "AiSeriesUpdate");
            Directory.CreateDirectory(dir);
            string ps1 = Path.Combine(dir, "apply_" + DateTime.Now.Ticks + ".ps1");
            StringBuilder sb = new StringBuilder();
            sb.Append("Start-Sleep -Seconds 2\r\n");
            sb.Append("for ($i=0; $i -lt 90; $i++) { try { Copy-Item -LiteralPath '").Append(Ps(newExe)).Append("' -Destination '").Append(Ps(target)).Append("' -Force -ErrorAction Stop; break } catch { Start-Sleep -Seconds 1 } }\r\n");
            sb.Append("Start-Process -FilePath '").Append(Ps(target)).Append("'\r\n");
            sb.Append("Remove-Item -LiteralPath $MyInvocation.MyCommand.Path -Force -ErrorAction SilentlyContinue\r\n");
            File.WriteAllText(ps1, sb.ToString(), new UTF8Encoding(true));

            ProcessStartInfo psi = new ProcessStartInfo("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -File \"" + ps1 + "\"");
            psi.UseShellExecute = true;
            psi.Verb = "runas";
            psi.WindowStyle = ProcessWindowStyle.Hidden;
            try
            {
                Process.Start(psi);
                Application.Exit();
            }
            catch (Exception ex)
            {
                DesignDialog.ShowError("这个安装目录需要管理员权限才能覆盖。\n已取消授权，更新没有执行。\n\n" + ex.Message, "软件更新");
            }
        }

        static string Ps(string s) { return (s ?? "").Replace("'", "''"); }

        static string Safe(string s)
        {
            if (string.IsNullOrEmpty(s)) return "new";
            StringBuilder sb = new StringBuilder();
            foreach (char c in s)
                sb.Append(char.IsLetterOrDigit(c) || c == '.' || c == '-' || c == '_' ? c : '_');
            return sb.ToString();
        }

        static string Sha256(string file)
        {
            using (FileStream fs = File.OpenRead(file))
            using (SHA256 sha = SHA256.Create())
            {
                byte[] h = sha.ComputeHash(fs);
                return BitConverter.ToString(h).Replace("-", "").ToLowerInvariant();
            }
        }

        static byte[] HttpGet(string url, out string err)
        {
            err = null;
            for (int attempt = 0; attempt < 2; attempt++)
            {
                try
                {
                    HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
                    req.Method = "GET";
                    req.Timeout = 15000;
                    req.ReadWriteTimeout = 30000;
                    req.AllowAutoRedirect = true;
                    req.UserAgent = "AiSeriesTool-Updater/" + CurrentVersion();
                    if (attempt == 1) req.Proxy = WebRequest.DefaultWebProxy;
                    using (WebResponse resp = req.GetResponse())
                    using (Stream s = resp.GetResponseStream())
                    using (MemoryStream ms = new MemoryStream())
                    {
                        s.CopyTo(ms);
                        return ms.ToArray();
                    }
                }
                catch (Exception ex)
                {
                    err = ex.Message;
                    try { System.Threading.Thread.Sleep(600); } catch { }
                }
            }
            return null;
        }

        static bool IsNewer(string remote, string local)
        {
            int[] a = Parts(remote), b = Parts(local);
            for (int i = 0; i < 4; i++)
            {
                if (a[i] > b[i]) return true;
                if (a[i] < b[i]) return false;
            }
            return false;
        }

        static int[] Parts(string s)
        {
            int[] r = new int[4];
            if (string.IsNullOrEmpty(s)) return r;
            string[] p = s.Trim().Split('.');
            for (int i = 0; i < p.Length && i < 4; i++)
            {
                int n;
                if (int.TryParse(p[i], out n)) r[i] = n;
            }
            return r;
        }

        static void OnUi(Action a)
        {
            Form f = null;
            try
            {
                foreach (Form x in Application.OpenForms)
                    if (x is MainForm) { f = x; break; }
            }
            catch { }
            if (f != null && f.IsHandleCreated)
            {
                try { f.BeginInvoke(a); return; } catch { }
            }
            try { a(); } catch { }
        }
    }
}
