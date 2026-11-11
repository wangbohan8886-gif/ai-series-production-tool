using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace AiAnimeTool
{
    public enum DiaIcon { Info, Warn, Error, Question }

    public class DesignDialog : Form
    {
        public static void ShowInfo(string text, string title = "提示") { Show(text, title, DiaIcon.Info, false); }
        public static void ShowWarn(string text, string title = "提示") { Show(text, title, DiaIcon.Warn, false); }
        public static void ShowError(string text, string title = "错误") { Show(text, title, DiaIcon.Error, false); }
        public static void ShowConfirm(string text, string title, Action onOk) { Show(text, title, DiaIcon.Question, true, onOk); }

        static void Show(string text, string title, DiaIcon icon, bool yesNo, Action onOk = null)
        {
            DesignDialog d = new DesignDialog(text, title, icon, yesNo);
            Form owner = null;
            try
            {
                foreach (Form f in Application.OpenForms)
                    if (f is MainForm) { owner = f; break; }
                if (owner == null && Application.OpenForms.Count > 0) owner = Application.OpenForms[0];
            }
            catch { }
            // 所有弹窗（提示/报错/确认）都锁住主界面：后方点击不触发任何动作，
            // 只让弹窗闪一下边框提醒；不再用系统模态阻塞整个主窗。
            Ui.OpenDialog(owner, d, () => { if (onOk != null && d.DialogResult == DialogResult.OK) onOk(); },
                false, false, true);
        }

        DesignDialog(string text, string title, DiaIcon icon, bool yesNo)
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual; // 位置由 Ui.OpenDialog 居中控制
            ShowInTaskbar = false;
            BackColor = Color.White;
            Font = new Font("微软雅黑", 10F);

            int W = 470;
            Font textFont = new Font("微软雅黑", 10F);
            Size textSize = TextRenderer.MeasureText(text, textFont, new Size(360, 500), TextFormatFlags.WordBreak);
            int textH = Math.Max(36, Math.Min(320, textSize.Height));
            int H = 56 + textH + 80;
            Size = new Size(W, H);
            Ui.ApplyWinFrame(this, 12);
            Ui.Composite(this);

            Label t = new Label();
            t.Text = title;
            t.Font = new Font("微软雅黑", 13F, FontStyle.Bold);
            t.ForeColor = Color.FromArgb(28, 33, 50);
            t.Location = new Point(24, 18);
            t.Size = new Size(W - 48, 26);
            Controls.Add(t);

            Panel ic = new Panel();
            ic.Location = new Point(24, 56);
            ic.Size = new Size(44, 44);
            ic.Paint += (s, e) => DrawDiaIcon(e.Graphics, icon, 44);
            Controls.Add(ic);

            Label msg = new Label();
            msg.Text = text;
            msg.Font = textFont;
            msg.ForeColor = Color.FromArgb(60, 68, 90);
            msg.Location = new Point(84, 56);
            msg.Size = new Size(W - 108, textH);
            Controls.Add(msg);

            int by = H - 62;
            int bw = 96, bh = 38;
            GradientButton ok = new GradientButton();
            ok.Text = yesNo ? "是" : "确定";
            ok.Location = new Point(W - 24 - bw, by);
            ok.Size = new Size(bw, bh);
            ok.Click += (s, e) => { DialogResult = DialogResult.OK; Close(); };
            Controls.Add(ok);

            if (yesNo)
            {
                GradientButton no = new GradientButton();
                no.Text = "否";
                no.Flat = true;
                no.Font = new Font("微软雅黑", 10F);
                no.Location = new Point(W - 24 - bw - 12 - bw, by);
                no.Size = new Size(bw, bh);
                no.Click += (s, e) => { DialogResult = DialogResult.No; Close(); };
                Controls.Add(no);
            }
            Ui.AttachShadow(this, 28, 12);
            bool closing = false;
            FormClosing += (s, e) => closing = true;
            Deactivate += (s, e) =>
            {
                if (closing) return;
                // 锁层会吃掉主界面点击并让这里闪边；如果焦点确实被主窗拿走，也补一次闪烁
                if (Owner != null && (Form.ActiveForm == Owner || Owner.ContainsFocus)) { Ui.FlashBorder(this); return; }
                if (Form.ActiveForm == null) Ui.FlashBorder(this);
            };
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Ui.DrawFlashBorder(e.Graphics, this, Ui.IsWin11 ? 4 : 12, Color.FromArgb(205, 108, 140, 245));
        }

        static void DrawDiaIcon(Graphics g, DiaIcon icon, int size)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            Rectangle rc = new Rectangle(0, 0, size - 1, size - 1);
            Color c = icon == DiaIcon.Error ? Color.FromArgb(229, 72, 77) :
                      icon == DiaIcon.Warn ? Color.FromArgb(245, 166, 35) :
                      Color.FromArgb(108, 140, 245);
            using (GraphicsPath p = Ui.Rounded(rc, size / 2))
            using (SolidBrush b = new SolidBrush(c))
                g.FillPath(b, p);
            string glyph = icon == DiaIcon.Info ? "i" : icon == DiaIcon.Warn ? "!" : icon == DiaIcon.Error ? "×" : "?";
            TextRenderer.DrawText(g, glyph, new Font("微软雅黑", size / 2 + 2, FontStyle.Bold), rc, Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }

    public class PreviewDialog : Form
    {
        public PreviewDialog(string summary, List<string> oldNames, List<string> newNames)
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual; // 位置由 Ui.OpenDialog 居中控制
            ShowInTaskbar = false;
            BackColor = Color.White;
            Font = new Font("微软雅黑", 10F);
            int W = 680, H = 560;
            Size = new Size(W, H);
            Ui.ApplyWinFrame(this, 14);

            Label t = new Label();
            t.Text = "重命名预览";
            t.Font = new Font("微软雅黑", 15F, FontStyle.Bold);
            t.ForeColor = Color.FromArgb(28, 33, 50);
            t.Location = new Point(28, 22);
            t.Size = new Size(W - 56, 30);
            Controls.Add(t);

            Label sub = new Label();
            sub.Text = summary;
            sub.Font = new Font("微软雅黑", 9.5F);
            sub.ForeColor = Color.FromArgb(140, 148, 168);
            sub.Location = new Point(30, 56);
            sub.Size = new Size(W - 60, 20);
            Controls.Add(sub);

            Panel listHost = new Panel();
            listHost.Location = new Point(24, 92);
            listHost.Size = new Size(W - 48, H - 92 - 74);
            listHost.AutoScroll = true;
            listHost.BackColor = Color.FromArgb(246, 248, 252);
            Controls.Add(listHost);

            int rowW = listHost.ClientSize.Width - 24;
            if (oldNames.Count == 0)
            {
                Label empty = new Label();
                empty.Text = "没有可预览的文件";
                empty.Font = new Font("微软雅黑", 11F);
                empty.ForeColor = Color.FromArgb(150, 158, 180);
                empty.Location = new Point(0, 24);
                empty.Size = new Size(listHost.ClientSize.Width, 30);
                empty.TextAlign = ContentAlignment.MiddleCenter;
                listHost.Controls.Add(empty);
            }
            else
            {
                int rowH = 44, gap = 8, y = 12;
                for (int i = 0; i < oldNames.Count; i++)
                {
                    PreviewRow row = new PreviewRow(i + 1, oldNames[i], newNames[i]);
                    row.Location = new Point(12, y);
                    row.Size = new Size(rowW, rowH);
                    listHost.Controls.Add(row);
                    y += rowH + gap;
                }
            }

            Label cnt = new Label();
            cnt.Text = "共 " + oldNames.Count + " 个文件";
            cnt.Font = new Font("微软雅黑", 9.5F);
            cnt.ForeColor = Color.FromArgb(140, 148, 168);
            cnt.Location = new Point(28, H - 58);
            cnt.Size = new Size(200, 24);
            Controls.Add(cnt);

            GradientButton close = new GradientButton();
            close.Text = "关闭";
            close.Location = new Point(W - 24 - 96, H - 62);
            close.Size = new Size(96, 38);
            close.Click += (s, e) => Close();
            Controls.Add(close);
            Ui.AttachShadow(this, 28, 14);
            bool closing = false;
            FormClosing += (s, e) => closing = true;
            Deactivate += (s, e) =>
            {
                if (closing) return;
                // 非模态：点到主程序（owner）是合法操作，会被带回最上层，不提醒；
                // 只有激活切到本程序之外（ActiveForm 为 null）才闪框提醒。
                if (Owner != null && (Form.ActiveForm == Owner || Owner.ContainsFocus)) return;
                if (Form.ActiveForm == null) Ui.FlashBorder(this);
            };
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Ui.DrawFlashBorder(e.Graphics, this, Ui.IsWin11 ? 4 : 14, Color.FromArgb(205, 108, 140, 245));
        }
    }

    public class PreviewRow : Panel
    {
        Panel badge;
        Label oldL, arrow, newL;

        public PreviewRow(int idx, string oldName, string newName)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Color.Transparent;

            badge = new Panel();
            badge.Size = new Size(34, 26);
            badge.Paint += (s, e) =>
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                Rectangle rc = new Rectangle(0, 0, 33, 25);
                using (GraphicsPath p = Ui.Rounded(rc, 8))
                using (LinearGradientBrush b = new LinearGradientBrush(rc, Color.FromArgb(108, 140, 245), Color.FromArgb(150, 118, 255), 35f))
                    g.FillPath(b, p);
                TextRenderer.DrawText(g, idx.ToString(), new Font("微软雅黑", 9.5F, FontStyle.Bold), rc, Color.White,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            };
            Controls.Add(badge);

            oldL = new Label();
            oldL.Text = oldName;
            oldL.Font = new Font("微软雅黑", 9.5F);
            oldL.ForeColor = Color.FromArgb(140, 148, 168);
            oldL.TextAlign = ContentAlignment.MiddleLeft;
            oldL.AutoEllipsis = true;
            Controls.Add(oldL);

            arrow = new Label();
            arrow.Text = "→";
            arrow.Font = new Font("微软雅黑", 12F, FontStyle.Bold);
            arrow.ForeColor = Color.FromArgb(108, 140, 245);
            arrow.TextAlign = ContentAlignment.MiddleCenter;
            Controls.Add(arrow);

            newL = new Label();
            newL.Text = newName;
            newL.Font = new Font("微软雅黑", 9.5F, FontStyle.Bold);
            newL.ForeColor = Color.FromArgb(31, 41, 55);
            newL.TextAlign = ContentAlignment.MiddleLeft;
            newL.AutoEllipsis = true;
            Controls.Add(newL);

            LayoutChildren();
        }

        void LayoutChildren()
        {
            badge.Location = new Point(12, (Height - badge.Height) / 2);
            oldL.Location = new Point(58, 0);
            oldL.Size = new Size(Math.Max(60, Width - 58 - 150), Height);
            arrow.Location = new Point(Width - 140, 0);
            arrow.Size = new Size(28, Height);
            newL.Location = new Point(Width - 104, 0);
            newL.Size = new Size(96, Height);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutChildren();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle rc = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath p = Ui.Rounded(rc, 10))
            {
                using (SolidBrush b = new SolidBrush(Color.White)) g.FillPath(b, p);
                using (Pen pen = new Pen(Color.FromArgb(232, 237, 247))) g.DrawPath(pen, p);
            }
        }
    }

    // 配置 API：接口地址 / API Key / 模型名，保存到 config.json 的 image 段
    public class ApiConfigDialog : Form
    {
        FieldBox fUrl, fKey, fProxy;

        public ApiConfigDialog()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            BackColor = Color.White;
            Font = new Font("微软雅黑", 10F);
            AutoScaleMode = AutoScaleMode.None;      // 绝对坐标布局，禁止按 DPI 二次缩放（否则底部按钮被裁）
            int W = 620, H = 404;
            Size = new Size(W, H);
            Ui.ApplyWinFrame(this, 14);

            Label t = new Label();
            t.Text = "配置 API";
            t.Font = new Font("微软雅黑", 15F, FontStyle.Bold);
            t.ForeColor = Color.FromArgb(28, 33, 50);
            t.Location = new Point(40, 22);
            t.Size = new Size(W - 80, 30);
            Controls.Add(t);

            Label sub = new Label();
            sub.Text = "填写接口地址与 API Key；模型由接口返回的清单自动识别，在生成页里选";
            sub.Font = new Font("微软雅黑", 9.5F);
            sub.ForeColor = Color.FromArgb(140, 148, 168);
            sub.Location = new Point(40, 56);
            sub.Size = new Size(W - 80, 20);
            Controls.Add(sub);

            CfgImage c = Program.Config.image;
            if (c == null) { c = new CfgImage(); Program.Config.image = c; }

            int y = 104;
            Controls.Add(LabelAt("API 地址", 40, y + 10, 100));
            fUrl = Field(146, y, 432, 40);
            fUrl.TextValue = string.IsNullOrEmpty(c.baseUrl) ? "https://api.openai.com/v1" : c.baseUrl;
            Controls.Add(fUrl);

            y += 58;
            Controls.Add(LabelAt("API Key", 40, y + 10, 100));
            fKey = Field(146, y, 432, 40);
            fKey.TextValue = c.apiKey ?? "";
            Controls.Add(fKey);

            // 模型不再在这里填：地址+Key 保存后由「生成图像」页向 /models 问一遍
            // 接口实际支持哪些模型，只把可用的列进下拉框，选哪个就原样调哪个。
            y += 58;
            Controls.Add(LabelAt("代理", 40, y + 10, 100));
            fProxy = Field(146, y, 432, 40);
            fProxy.TextValue = c.proxy ?? "";
            Controls.Add(fProxy);

            // 更新地址已固定在程序内（UpdateService.ManifestUrl），这里不再让用户填写。
            Label tip = new Label();
            tip.Text = "OpenAI 兼容填 https://api.openai.com/v1（国内中转填它给的地址，可直连不用梯子）；\r\nGoogle Gemini 填 https://generativelanguage.googleapis.com/v1beta";
            tip.Font = new Font("微软雅黑", 8.5F);
            tip.ForeColor = Color.FromArgb(150, 158, 180);
            tip.Location = new Point(40, 282);
            tip.Size = new Size(W - 80, 46);
            Controls.Add(tip);

            GradientButton ok = new GradientButton();
            ok.Text = "保存";
            ok.Location = new Point(W - 40 - 108, H - 62);
            ok.Size = new Size(108, 40);
            ok.Click += (s, e) =>
            {
                Program.Config.image.baseUrl = fUrl.TextValue;
                Program.Config.image.apiKey = fKey.TextValue;
                Program.Config.image.proxy = fProxy.TextValue;
                if (string.IsNullOrEmpty(Program.Config.image.baseUrl)) Program.Config.image.baseUrl = "https://api.openai.com/v1";
                Program.SaveConfig();
                DialogResult = DialogResult.OK;
                Close();
            };
            Controls.Add(ok);

            GradientButton cancel = new GradientButton();
            cancel.Text = "取消";
            cancel.Flat = true;
            cancel.Font = new Font("微软雅黑", 10F);
            cancel.Location = new Point(W - 40 - 108 - 12 - 96, H - 62);
            cancel.Size = new Size(96, 40);
            cancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(cancel);

            Ui.AttachShadow(this, 28, 14);
            bool closing = false;
            FormClosing += (s, e) => closing = true;
            Deactivate += (s, e) =>
            {
                if (closing) return;
                // 锁层点击会让这里闪边；焦点真被主窗拿走时也补一次提醒
                if (Owner != null && (Form.ActiveForm == Owner || Owner.ContainsFocus)) { Ui.FlashBorder(this); return; }
                if (Form.ActiveForm == null) Ui.FlashBorder(this);
            };
        }

        FieldBox Field(int x, int y, int w, int h)
        {
            FieldBox f = new FieldBox();
            f.Location = new Point(x, y);
            f.Size = new Size(w, h);
            return f;
        }

        Label LabelAt(string text, int x, int y, int w, int h = 22)
        {
            Label l = new Label();
            l.Text = text;
            l.Font = new Font("微软雅黑", 10F);
            l.ForeColor = Color.FromArgb(60, 68, 90);
            l.Location = new Point(x, y);
            l.Size = new Size(w, h);
            l.TextAlign = ContentAlignment.MiddleLeft;
            return l;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Ui.DrawFlashBorder(e.Graphics, this, Ui.IsWin11 ? 4 : 12, Color.FromArgb(205, 108, 140, 245));
        }
    }

    // 放大查看生成的图片：右侧显示图片信息与提示词（可一键复制），右上角「下载 / 关闭」
    // 大图窗口右上角的"最小化"：方形圆角、只画一道减号，悬停淡紫
    public class MinButton : Control
    {
        bool hover, down;
        public MinButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            Cursor = Cursors.Hand;
            BackColor = Color.White;
        }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { down = true; Invalidate(); } base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle rc = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath p = Ui.Rounded(rc, 9))
            {
                Color fill = down ? Color.FromArgb(226, 232, 250)
                           : hover ? Color.FromArgb(238, 241, 253) : Color.FromArgb(248, 250, 255);
                using (SolidBrush b = new SolidBrush(fill)) g.FillPath(b, p);
                using (Pen pen = new Pen(hover ? Color.FromArgb(176, 194, 244) : Color.FromArgb(214, 222, 240)))
                    g.DrawPath(pen, p);
            }
            Color c = down ? Color.FromArgb(74, 96, 190) : Color.FromArgb(96, 118, 190);
            using (Pen pen = new Pen(c, 1.9f))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                int cy = Height / 2, half = Math.Max(5, Width / 7);
                g.DrawLine(pen, Width / 2 - half, cy, Width / 2 + half, cy);
            }
        }
    }

    public class ImageViewDialog : Form
    {
        [DllImport("user32.dll")] static extern bool ReleaseCapture();
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr wp, IntPtr lp);

        // 参考图 / 文件预览：只看图，不显示右侧图片信息栏
        public static void Show(string path) { Show(path, null, null, false); }
        // 生成结果图：右侧带图片信息与提示词
        public static void Show(string path, string prompt) { Show(path, prompt, null, true); }

        public static void Show(string path, string prompt, List<string> refs)
        {
            Show(path, prompt, refs, true);
        }

        public static void Show(string path, string prompt, List<string> refs, bool withInfo)
        {
            // 大图窗口是完全独立的顶层窗口：不挂主程序做 owner、不锁主程序、不参与"同时只允许一个弹窗"的单例。
            // 这样主程序永远是自己的正常窗口（不会被大图的白色残影盖住），大图也能多开、各自最小化。
            ImageViewDialog d = new ImageViewDialog(path, prompt, refs, withInfo);
            Rectangle wa;
            try { wa = Screen.FromPoint(Cursor.Position).WorkingArea; }
            catch { wa = new Rectangle(0, 0, 1600, 900); }
            d.StartPosition = FormStartPosition.Manual;
            d.Location = new Point(wa.X + (wa.Width - d.Width) / 2, wa.Y + (wa.Height - d.Height) / 2);
            d.Show();
        }

        string path, promptText, modelText, qualityText;
        List<string> refs = new List<string>();
        bool withInfo = true;
        Bitmap bmp;
        Rectangle imgRect;
        Panel infoCard;

        public ImageViewDialog(string path) : this(path, null, null, false) { }

        public ImageViewDialog(string path, string prompt) : this(path, prompt, null, true) { }

        public ImageViewDialog(string path, string prompt, List<string> refsIn) : this(path, prompt, refsIn, true) { }

        public ImageViewDialog(string path, string prompt, List<string> refsIn, bool showInfo)
        {
            this.path = path;
            this.withInfo = showInfo;
            if (refsIn != null) foreach (string s in refsIn) if (!string.IsNullOrEmpty(s)) refs.Add(s);
            promptText = LoadPrompt(path, prompt);
            modelText = LoadModelName(path);
            qualityText = LoadQualityName(path);
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            // 独立窗口：要在任务栏有一格，最小化到后台后才找得回来
            ShowInTaskbar = true;
            BackColor = Color.White;
            Font = new Font("微软雅黑", 10F);
            AutoScaleMode = AutoScaleMode.None;
            KeyPreview = true;

            try
            {
                using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read))
                using (Image im = Image.FromStream(fs))
                    bmp = new Bitmap(im);
            }
            catch { bmp = null; }

            int iw = bmp != null ? bmp.Width : 500;
            int ih = bmp != null ? bmp.Height : 300;
            Rectangle wa;
            try { wa = Screen.FromHandle(IntPtr.Zero).WorkingArea; } catch { wa = new Rectangle(0, 0, 1600, 900); }
            const int pad = 24;
            int panelW = withInfo ? 336 : 0, gap = withInfo ? 22 : 0;
            int maxW = (int)(wa.Width * 0.9), maxH = (int)(wa.Height * 0.86);
            int boxW = Math.Max(240, maxW - panelW - gap - pad * 2);
            int boxH = Math.Max(200, maxH - 60 - 24);
            float k = Math.Min(1f, Math.Min((float)boxW / iw, (float)boxH / ih));
            int iwd = Math.Max(1, (int)(iw * k)), ihd = Math.Max(1, (int)(ih * k));
            int W = pad * 2 + iwd + gap + panelW;
            if (W < 380) W = 380;      // 只看图时也要放得下文件名和下载/关闭按钮
            int H = Math.Max(460, ihd + 60 + 24);
            Size = new Size(W, H);
            imgRect = new Rectangle(withInfo ? pad : Math.Max(pad, (W - iwd) / 2), 60 + (H - 84 - ihd) / 2, iwd, ihd);
            Ui.ApplyWinFrame(this, 14);
            Ui.Composite(this);

            // 右上角按钮排布：[下载]（只看图时再加一个 [关闭]）
            GradientButton down = new GradientButton();
            down.Text = "下载";
            down.Font = new Font("微软雅黑", 9.5F, FontStyle.Bold);
            down.Size = new Size(88, 34);
            down.Location = new Point(W - 24 - 88, 14);
            down.Click += (s, e) => DownloadDirect();
            Controls.Add(down);

            int leftEnd = down.Left;
            if (!withInfo)
            {
                // 只看图时没有右侧信息栏，关闭按钮补在右上角（信息栏模式下方那个「关闭」在卡片里）
                GradientButton closeTop = new GradientButton();
                closeTop.Text = "关闭";
                closeTop.Flat = true;
                closeTop.Font = new Font("微软雅黑", 9.5F);
                closeTop.Size = new Size(76, 34);
                closeTop.Location = new Point(down.Left - 8 - 76, 14);
                closeTop.Click += (s, e) => Close();
                Controls.Add(closeTop);
                leftEnd = closeTop.Left;
            }

            Label name = new Label();
            name.Text = Path.GetFileName(path);
            name.Font = new Font("微软雅黑", 9F);
            name.ForeColor = Color.FromArgb(150, 158, 180);
            name.Location = new Point(24, 18);
            name.Size = new Size(leftEnd - 24 - 12, 26);
            name.TextAlign = ContentAlignment.MiddleLeft;
            name.AutoEllipsis = true;
            Controls.Add(name);

            if (withInfo) BuildInfoPanel(new Rectangle(pad + iwd + gap, 60, panelW, H - 84));

            // 窗口可自由拖动：按住图片/空白处 = 抓住标题栏移动（不抢按钮和右键菜单）
            MouseDown += (s, e) => DragWindow(e);

            // 大图右键：复制图像 / 使用参考图（跟缩略图保持一套）；菜单是分层窗口画的圆角，没有锯齿
            MouseUp += (s, e) =>
            {
                if (e.Button != MouseButtons.Right) return;
                if (imgRect.Contains(e.Location) || !withInfo)
                    RoundMenu.Show(
                        new string[] { "复制图像", "使用参考图" },
                        Cursor.Position,
                        new Action[]
                        {
                            () => CopyImage(),
                            () => { if (ImagePage.Current != null) ImagePage.Current.AddRefFile(path); }
                        });
            };

            Ui.AttachShadow(this, 28, 16);
            bool closing = false;
            FormClosing += (s, e) => { closing = true; if (bmp != null) { bmp.Dispose(); bmp = null; } };
            Deactivate += (s, e) =>
            {
                if (closing) return;
                if (Owner != null && (Form.ActiveForm == Owner || Owner.ContainsFocus)) return;
                if (Form.ActiveForm == null) Ui.FlashBorder(this);
            };
            KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) Close(); };
        }

        static string LoadModelName(string path)
        {
            try
            {
                string p = Path.Combine(Path.GetDirectoryName(path), Path.GetFileNameWithoutExtension(path) + ".model.txt");
                if (File.Exists(p)) return File.ReadAllText(p, Encoding.UTF8).Trim();
            }
            catch { }
            return "";
        }

        // 质量档位（1K/2K/4K）存在同名 .quality.txt 里
        static string LoadQualityName(string path)
        {
            try
            {
                string p = Path.Combine(Path.GetDirectoryName(path), Path.GetFileNameWithoutExtension(path) + ".quality.txt");
                if (File.Exists(p)) return File.ReadAllText(p, Encoding.UTF8).Trim();
            }
            catch { }
            return "";
        }

        // 提示词优先用记录里带的，没有就读同名 .txt（历史记录都存了）
        static string LoadPrompt(string path, string prompt)
        {
            if (!string.IsNullOrEmpty(prompt)) return StripSys(prompt.Trim());
            try
            {
                string txt = Path.Combine(Path.GetDirectoryName(path), Path.GetFileNameWithoutExtension(path) + ".txt");
                if (File.Exists(txt)) return StripSys(File.ReadAllText(txt, Encoding.UTF8).Trim());
            }
            catch { }
            return "";
        }

        // 程序自动加的"参考图编号对应"那段是发给模型用的后台说明，界面上只显示用户自己敲的提示词
        public static string StripSys(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            const string mark = "【参考图编号对应】";
            int k = s.IndexOf(mark, StringComparison.Ordinal);
            if (k == 0)
            {
                int br = s.IndexOf("\n\n", StringComparison.Ordinal);
                if (br > 0) s = s.Substring(br + 2);
            }
            return s.Trim();
        }

        static string SizeText(long bytes)
        {
            if (bytes < 1024) return bytes + " B";
            if (bytes < 1024 * 1024) return (bytes / 1024.0).ToString("0.0") + " KB";
            return (bytes / 1024.0 / 1024.0).ToString("0.00") + " MB";
        }

        void BuildInfoPanel(Rectangle rc)
        {
            RoundCard card = new RoundCard();
            card.Fill = Color.FromArgb(250, 251, 254);
            card.BorderColor = Color.FromArgb(236, 239, 245);
            card.Radius = 12;
            card.Bounds = rc;
            Controls.Add(card);
            infoCard = card;

            int x = 18;
            int w = rc.Width - x * 2;

            Label t = new Label();
            t.Text = "图片信息";
            t.Font = new Font("微软雅黑", 10.5F, FontStyle.Bold);
            t.ForeColor = Color.FromArgb(28, 33, 50);
            t.Location = new Point(x, 14);
            t.Size = new Size(w, 26);
            card.Controls.Add(t);

            long bytes = 0;
            try { bytes = new FileInfo(path).Length; } catch { }
            string format = bmp != null ? (bmp.RawFormat.Guid == System.Drawing.Imaging.ImageFormat.Jpeg.Guid ? "JPEG" : "PNG") : "未知";
            DateTime time = DateTime.MinValue;
            try { time = File.GetCreationTime(path); } catch { }

            string[,] rows = new string[6, 2];
            rows[0, 0] = "尺寸"; rows[0, 1] = bmp != null ? bmp.Width + " × " + bmp.Height + " px" : "—";
            rows[1, 0] = "大小"; rows[1, 1] = bytes > 0 ? SizeText(bytes) : "—";
            rows[2, 0] = "格式"; rows[2, 1] = format;
            rows[3, 0] = "模型"; rows[3, 1] = modelText.Length > 0 ? modelText : "未知（这条记录没保存模型名）";
            // 不需要选质量的模型（界面上根本没有质量下拉）就写「自适应」，
            // 不要写成一串"未知/没保存"，那会让人以为记录坏了。
            string showQ = qualityText;
            if (showQ.Length == 0) showQ = "自适应";
            else
            {
                bool locked;
                if (modelText.Length > 0 && !QualityPicker.AppliesTo(modelText, out locked)) showQ = "自适应";
            }
            rows[4, 0] = "质量"; rows[4, 1] = showQ;
            rows[5, 0] = "时间"; rows[5, 1] = time == DateTime.MinValue ? "—" : time.ToString("yyyy-MM-dd HH:mm");
            for (int i = 0; i < 6; i++)
            {
                Label lk = new Label();
                lk.Text = rows[i, 0];
                lk.Font = new Font("微软雅黑", 9F);
                lk.ForeColor = Color.FromArgb(150, 158, 180);
                lk.Location = new Point(x, 46 + i * 24);
                lk.Size = new Size(46, 22);
                card.Controls.Add(lk);

                Label lv = new Label();
                lv.Text = rows[i, 1];
                lv.Font = new Font("微软雅黑", 9.5F);
                lv.ForeColor = Color.FromArgb(60, 68, 90);
                lv.Location = new Point(x + 50, 46 + i * 24);
                lv.Size = new Size(w - 50, 22);
                lv.TextAlign = ContentAlignment.MiddleLeft;
                lv.AutoEllipsis = true;
                card.Controls.Add(lv);
            }

            Label sep = new Label();
            sep.Text = "";
            sep.BackColor = Color.FromArgb(236, 239, 245);
            sep.Location = new Point(x, 196);
            sep.Size = new Size(w, 1);
            card.Controls.Add(sep);

            int y = 204;
            // 参考图：生成这张图时真正喂给模型的那几张，可看大图 / 复制 / 下载
            if (refs.Count > 0)
            {
                Label lr = new Label();
                lr.Text = "参考图（" + refs.Count + " 张 · 点击看大图）";
                lr.Font = new Font("微软雅黑", 9F);
                lr.ForeColor = Color.FromArgb(150, 158, 180);
                lr.Location = new Point(x, y);
                lr.Size = new Size(w, 20);
                card.Controls.Add(lr);

                int tw = 82, gp = 8;
                int cols = Math.Max(1, (w + gp) / (tw + gp));
                int lines = (refs.Count + cols - 1) / cols;
                if (lines > 2) lines = 2;
                for (int i = 0; i < refs.Count && i < cols * lines; i++)
                {
                    RefThumb rt = new RefThumb(refs[i], tw);
                    rt.Location = new Point(x + (i % cols) * (tw + gp), y + 24 + (i / cols) * (tw + gp));
                    card.Controls.Add(rt);
                }
                y += 24 + lines * (tw + gp) + 2;
            }

            Label lp = new Label();
            lp.Text = promptText.Length > 0 ? "提示词" : "提示词（这条记录没有保存提示词）";
            lp.Font = new Font("微软雅黑", 9F);
            lp.ForeColor = Color.FromArgb(150, 158, 180);
            lp.Location = new Point(x, y);
            lp.Size = new Size(w, 20);
            card.Controls.Add(lp);

            int editY = y + 24;
            int btnH = 34;
            int editH = Math.Max(80, rc.Height - editY - btnH - 30);
            PromptEdit view = new PromptEdit();
            view.BackColor = Color.White;
            view.Bounds = new Rectangle(x, editY, w, editH);
            view.Editor.ReadOnly = true;
            view.Editor.Font = new Font("微软雅黑", 9.5F);
            view.Editor.ForeColor = Color.FromArgb(60, 68, 90);
            view.Editor.Text = promptText;
            view.Editor.TabStop = false;
            card.Controls.Add(view);

            int by = rc.Height - 12 - btnH;
            int bwUse = 104, bwCopy = 104, bwClose = 68;
            int closeX = rc.Width - 12 - bwClose;
            int copyX = closeX - 8 - bwCopy;
            int useX = copyX - 8 - bwUse;

            // 「使用提示词」：把这张图的提示词直接填回生成图像页的输入框
            GradientButton use = new GradientButton();
            use.Text = "使用提示词";
            use.Font = new Font("微软雅黑", 9.5F);
            use.Size = new Size(bwUse, btnH);
            use.Location = new Point(useX, by);
            use.Enabled = promptText.Length > 0;
            use.Click += (s, e) =>
            {
                if (promptText.Length == 0) return;
                try { if (ImagePage.UsePromptHook != null) ImagePage.UsePromptHook(promptText); }
                catch { }
                Close();
            };
            card.Controls.Add(use);

            GradientButton copy = new GradientButton();
            copy.Text = promptText.Length > 0 ? "复制提示词" : "复制失败原因";
            copy.Font = new Font("微软雅黑", 9.5F);
            copy.Size = new Size(bwCopy, btnH);
            copy.Location = new Point(copyX, by);
            copy.Click += (s, e) =>
            {
                if (promptText.Length > 0) CopyPrompt();
                else CopyImage();
                copy.Text = "已复制";
                copy.Invalidate();
                System.Windows.Forms.Timer tm = new System.Windows.Forms.Timer { Interval = 1300 };
                tm.Tick += (a, b) =>
                {
                    tm.Stop(); tm.Dispose();
                    if (!copy.IsDisposed) { copy.Text = promptText.Length > 0 ? "复制提示词" : "复制失败原因"; copy.Invalidate(); }
                };
                tm.Start();
            };
            card.Controls.Add(copy);

            // 关闭按钮放在右下角，紧挨「复制提示词」右侧
            GradientButton close = new GradientButton();
            close.Text = "关闭";
            close.Flat = true;
            close.Font = new Font("微软雅黑", 9.5F);
            close.Size = new Size(bwClose, btnH);
            close.Location = new Point(closeX, by);
            close.Click += (s, e) => Close();
            card.Controls.Add(close);
        }

        // 按住图片/空白处拖窗口（等同抓标题栏）；点图片本身不弹菜单，右键仍是图片菜单
        void DragWindow(MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            try
            {
                ReleaseCapture();
                SendMessage(Handle, 0xA1, (IntPtr)2, IntPtr.Zero);   // WM_NCLBUTTONDOWN / HTCAPTION
            }
            catch { }
        }

        // 图片区也允许拖动：Form 自己画的图没有子控件挡着，直接走 Form 的 MouseDown


        void CopyPrompt()
        {
            if (promptText.Length == 0) return;
            try { Clipboard.SetText(promptText); }
            catch { }
        }

        public static void CopyImage(string filePath)
        {
            try
            {
                using (FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                using (Image im = Image.FromStream(fs))
                    Clipboard.SetImage(new Bitmap(im));
            }
            catch { }
        }

        void CopyImage()
        {
            if (bmp != null) { try { Clipboard.SetImage(new Bitmap(bmp)); } catch { } }
            else CopyImage(path);
        }

        // 顶部「下载」：直接落到系统「下载」文件夹，成功后主窗口上方弹出提示（右侧可打开该文件夹）
        void DownloadDirect()
        {
            try { DownloadToDefault(path); }
            catch (Exception ex) { DesignDialog.ShowError("下载失败：" + ex.Message); }
        }

        void SaveAs()
        {
            using (SaveFileDialog sd = new SaveFileDialog())
            {
                sd.Title = "保存图片";
                sd.FileName = Path.GetFileName(path);
                sd.Filter = "PNG 图片|*.png|JPEG 图片|*.jpg|所有文件|*.*";
                sd.InitialDirectory = Path.GetDirectoryName(path);
                if (sd.ShowDialog(this) == DialogResult.OK)
                {
                    try
                    {
                        File.Copy(path, sd.FileName, true);
                        Toast.Show("已保存 · " + Path.GetFileName(sd.FileName), Path.GetDirectoryName(sd.FileName));
                    }
                    catch (Exception ex) { DesignDialog.ShowError("保存失败：" + ex.Message); }
                }
            }
        }

        public static void RevealInExplorer(string filePath)
        {
            try { System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + filePath + "\""); }
            catch { }
        }

        // 一键下载到系统「下载」文件夹（同名自动加序号），返回落地路径
        public static string DownloadToDefault(string src)
        {
            if (string.IsNullOrEmpty(src) || !File.Exists(src)) throw new Exception("文件不存在");
            string dir = "";
            try { dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"); } catch { }
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
            {
                try { dir = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory); } catch { }
            }
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) throw new Exception("找不到「下载」文件夹");
            string name = Path.GetFileName(src);
            string bn = Path.GetFileNameWithoutExtension(name);
            string ext = Path.GetExtension(name);
            string dst = Path.Combine(dir, name);
            int i = 1;
            while (File.Exists(dst))
            {
                dst = Path.Combine(dir, bn + " (" + i + ")" + ext);
                i++;
            }
            File.Copy(src, dst, false);
            try { Toast.Show("下载成功 · " + Path.GetFileName(dst), dir); } catch { }
            return dst;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            using (GraphicsPath p = Ui.Rounded(new Rectangle(imgRect.X - 1, imgRect.Y - 1, imgRect.Width + 1, imgRect.Height + 1), 10))
            using (SolidBrush b = new SolidBrush(Color.FromArgb(246, 248, 252)))
                g.FillPath(b, p);
            if (bmp != null)
                g.DrawImage(bmp, FitRect(bmp.Size, imgRect));
            else
                TextRenderer.DrawText(g, "图片读取失败（文件可能已被移动或删除）", new Font("微软雅黑", 10F), imgRect,
                    Color.FromArgb(170, 177, 195), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            Ui.DrawFlashBorder(g, this, Ui.IsWin11 ? 4 : 12, Color.FromArgb(205, 108, 140, 245));
        }

        static Rectangle FitRect(Size src, Rectangle box)
        {
            float s = Math.Min((float)box.Width / src.Width, (float)box.Height / src.Height);
            if (s > 1f) s = 1f;
            int w = Math.Max(1, (int)(src.Width * s)), h = Math.Max(1, (int)(src.Height * s));
            return new Rectangle(box.X + (box.Width - w) / 2, box.Y + (box.Height - h) / 2, w, h);
        }
    }

    // 查看大图右侧的参考图小方块：点击看大图，右键复制 / 下载 / 另存
    public class RefThumb : Control
    {
        string file;
        Bitmap bmp;
        bool hover;

        public RefThumb(string path, int size)
        {
            file = path;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Size = new Size(size, size);
            BackColor = Color.FromArgb(240, 243, 250);
            Cursor = Cursors.Hand;
            try
            {
                using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read))
                using (Image im = Image.FromStream(fs)) bmp = new Bitmap(im);
            }
            catch { bmp = null; }

            // 参考图缩略图的右键：什么都不做，不弹菜单也不打开大图（左键才是看大图）
        }

        void SaveAs()
        {
            using (SaveFileDialog sd = new SaveFileDialog())
            {
                sd.Title = "保存参考图";
                sd.FileName = Path.GetFileName(file);
                sd.Filter = "PNG 图片|*.png|JPEG 图片|*.jpg|所有文件|*.*";
                if (sd.ShowDialog(FindForm()) == DialogResult.OK)
                {
                    try { File.Copy(file, sd.FileName, true); }
                    catch (Exception ex) { DesignDialog.ShowError("保存失败：" + ex.Message); }
                }
            }
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnClick(EventArgs e) { base.OnClick(e); }

        // 详情信息里的参考图：左键看大图，右键给「复制图像 / 使用参考图」
        // （提示词框上方那条参考图区是另一套自绘控件，那边右键无反应，互不影响）
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Right)
            {
                RoundMenu.Show(
                    new string[] { "复制图像", "使用参考图" },
                    Cursor.Position,
                    new Action[]
                    {
                        delegate { ImageViewDialog.CopyImage(file); },
                        delegate { if (ImagePage.Current != null) ImagePage.Current.AddRefFile(file); }
                    });
                return;
            }
            if (e.Button == MouseButtons.Left) ImageViewDialog.Show(file);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            Rectangle rc = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath p = Ui.Rounded(rc, 8))
            {
                if (bmp != null)
                {
                    g.SetClip(p);
                    float s = Math.Max((float)Width / bmp.Width, (float)Height / bmp.Height);
                    int w = (int)(bmp.Width * s) + 1, h = (int)(bmp.Height * s) + 1;
                    g.DrawImage(bmp, (Width - w) / 2, (Height - h) / 2, w, h);
                    g.ResetClip();
                }
                else
                {
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(240, 243, 250))) g.FillPath(b, p);
                    TextRenderer.DrawText(g, "参考图\n已丢失", new Font("微软雅黑", 8.5F), rc,
                        Color.FromArgb(168, 176, 196), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                }
                using (Pen pen = new Pen(hover ? Color.FromArgb(112, 140, 245) : Color.FromArgb(224, 230, 242), hover ? 2f : 1f))
                    g.DrawPath(pen, p);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && bmp != null) { bmp.Dispose(); bmp = null; }
            base.Dispose(disposing);
        }
    }

    // 把提示词框放大：同一个输入框的"大号视图"，文字/胶囊与下面那个框双向实时同步，
    // 窗口可以拖边拖角缩放，里面的输入区跟着变大；右下角「完成」关掉，内容已经同步回去了。
    public class PromptZoomDialog : Form
    {
        PromptEdit src;          // 主界面里那个小输入框
        PromptEdit big;          // 这个放大版
        RefPreview refRow;       // 放大框里也要能看见参考图和序号
        GradientButton done;
        Form ownerForm;
        Timer beat;
        bool sync, closing;
        string beatText; int beatCount = -1;
        Font titleFont = new Font("微软雅黑", 15F, FontStyle.Bold);
        Font subFont = new Font("微软雅黑", 9F);

        public static void Show(PromptEdit source)
        {
            Form owner = null;
            try
            {
                foreach (Form f in Application.OpenForms)
                    if (f is MainForm) { owner = f; break; }
            }
            catch { }
            PromptZoomDialog d = new PromptZoomDialog(source);
            // keepOthers=true：放大框常驻，从它里面点胶囊看大图、看失败原因，关掉子窗口后放大框不能跟着消失
            if (owner != null) Ui.OpenDialog(owner, d, null, false, true);
            else d.Show();
        }

        public PromptZoomDialog(PromptEdit source)
        {
            src = source;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            BackColor = Color.White;
            Font = new Font("微软雅黑", 10F);
            AutoScaleMode = AutoScaleMode.None;
            DoubleBuffered = true;
            KeyPreview = true;
            Text = "全屏编辑提示词";
            // 尺寸不自己定：跟着主窗体走（主窗多大，这个框就多大），
            // 打开时先按主窗算一次，之后由 SyncToOwner 持续跟随（不是拖边框缩放）
            Size = new Size(980, 680);
            MinimumSize = new Size(560, 360);
            Ui.ApplyWinFrame(this, 16);

            big = new PromptEdit();
            big.Editor.Font = new Font("微软雅黑", 13F);
            big.Editor.BackColor = Color.White;
            big.Editor.ForeColor = Color.FromArgb(31, 41, 55);
            big.BackColor = Color.White;
            // 胶囊与外面完全一致：图号→路径的映射直接共用同一张表，@ 弹窗、点胶囊看大图、粘图都接过去
            big.ChipMap = src.ChipMap;
            big.ChipSource = src.ChipSource;
            big.ChipClicked = src.ChipClicked;
            big.PasteImage = src.PasteImage;
            Controls.Add(big);

            refRow = new RefPreview();
            refRow.Source = src.ChipSource;
            Controls.Add(refRow);

            done = new GradientButton();
            done.Text = "完成";
            done.Font = new Font("微软雅黑", 10F, FontStyle.Bold);
            done.Size = new Size(112, 40);
            // 按下就收起：「完成」是最后一步，不需要等抬起；也避免某些情况下第一次点击被吃掉
            done.MouseDown += (s, e) => { if (e.Button == MouseButtons.Left) DoDone(); };
            done.Click += (s, e) => DoDone();
            Controls.Add(done);

            // 打开那一刻把内容搬进来（之后双向同步）
            big.Editor.Text = src.Editor.Text;
            big.Editor.SelectionStart = Math.Min(src.Editor.SelectionStart, big.Editor.TextLength);
            src.Editor.TextChanged += OnSrcChanged;
            big.Editor.TextChanged += OnBigChanged;

            LayoutInner();
            Shown += (s, e) =>
            {
                // 弹窗必须真的拿到焦点：否则用户第一次点「完成」会被系统当成"激活窗口"而吃掉
                try { Activate(); } catch { }
                try { big.Editor.Focus(); } catch { }
                SyncToOwner();
                // 每 400ms 对一次：外面参考图增删、外面文字被改、换行重排，
                // 大框里的缩略图与胶囊都能跟着变（不靠事件穿透，最稳）
                beat = new Timer { Interval = 400 };
                beat.Tick += (s2, e2) =>
                {
                    if (IsDisposed) { beat.Stop(); return; }
                    // 只有内容真变了才重排/重画：每 400ms 无条件 LayoutChips + Invalidate
                    // 就是放大框"一直闪"的根源
                    string t = "";
                    int cnt = -1;
                    try { t = big.Editor.Text; } catch { }
                    try { if (refRow.Source != null) { List<string> l = refRow.Source(); cnt = l == null ? 0 : l.Count; } } catch { }
                    if (t == beatText && cnt == beatCount) return;
                    beatText = t; beatCount = cnt;
                    try { refRow.Invalidate(); } catch { }
                    try { if (!sync) big.LayoutChips(); } catch { }
                };
                beat.Start();
            };
            FormClosing += (s, e) =>
            {
                try { if (beat != null) beat.Stop(); } catch { }
                try { if (ownerForm != null) { ownerForm.Resize -= OnOwnerChanged; ownerForm.Move -= OnOwnerChanged; } } catch { }
                try { src.Editor.TextChanged -= OnSrcChanged; } catch { }
                try { big.Editor.TextChanged -= OnBigChanged; } catch { }
                PullBack();
            };
            Deactivate += (s, e) =>
            {
                if (Form.ActiveForm == null) Ui.FlashBorder(this);
            };
        }

        void OnSrcChanged(object s, EventArgs e)
        {
            if (sync) return;
            sync = true;
            try
            {
                bool focused = big.Editor.Focused;
                int caret = src.Editor.SelectionStart;
                if (big.Editor.Text != src.Editor.Text) big.Editor.Text = src.Editor.Text;
                if (!focused) big.Editor.SelectionStart = Math.Min(caret, big.Editor.TextLength);
                big.LayoutChips();
            }
            catch { }
            finally { sync = false; }
        }

        void OnBigChanged(object s, EventArgs e)
        {
            if (sync) return;
            sync = true;
            try
            {
                int caret = big.Editor.SelectionStart;
                if (src.Editor.Text != big.Editor.Text) src.Editor.Text = big.Editor.Text;
                if (!src.Editor.Focused) src.Editor.SelectionStart = Math.Min(caret, src.Editor.TextLength);
                src.LayoutChips();
            }
            catch { }
            finally { sync = false; }
        }

        // 关闭前最后同步一次，保证外面的框就是放大版里看到的
        void DoDone()
        {
            if (closing) return;
            closing = true;
            PullBack();
            try { Close(); } catch { }
        }

        void PullBack()
        {
            try
            {
                if (big == null || big.IsDisposed || src == null || src.IsDisposed) return;
                sync = true;
                if (src.Editor.Text != big.Editor.Text) src.Editor.Text = big.Editor.Text;
                src.LayoutChips();
            }
            catch { }
            finally { sync = false; }
        }

        void OnOwnerChanged(object s, EventArgs e) { SyncToOwner(); }

        // 「放大」不是把弹窗单独拉大：它是主窗体的一部分，主窗变大它就变大、主窗挪走它就跟着挪，
        // 永远居中在主窗内容区里（比主窗略小一圈，留出主窗自己的边）。
        void SyncToOwner()
        {
            try
            {
                if (ownerForm == null)
                {
                    foreach (Form f in Application.OpenForms)
                        if (f is MainForm) { ownerForm = f; break; }
                    if (ownerForm == null) return;
                    ownerForm.Resize += OnOwnerChanged;
                    ownerForm.Move += OnOwnerChanged;
                }
                Rectangle r = ownerForm.Bounds;
                int w = Math.Max(560, r.Width - 96);
                int h = Math.Max(360, r.Height - 96);
                int x = r.X + (r.Width - w) / 2;
                int y = r.Y + (r.Height - h) / 2;
                // 主窗体最小化/不在屏上时不要乱跳
                if (ownerForm.WindowState == FormWindowState.Minimized) return;
                SetBounds(x, y, w, h);
                LayoutInner();
            }
            catch { }
        }

        void LayoutInner()
        {
            int w = ClientSize.Width, h = ClientSize.Height;
            if (big == null) return;
            int refH = 64;
            if (refRow != null) refRow.Bounds = new Rectangle(22, 76, Math.Max(80, w - 44), refH);
            int top = 76 + (refRow != null ? refH + 10 : 0);
            big.Bounds = new Rectangle(22, top, Math.Max(120, w - 44), Math.Max(80, h - top - 20 - 56));
            if (done != null) done.Location = new Point(w - 22 - done.Width, h - 20 - done.Height);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutInner();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            TextRenderer.DrawText(g, "全屏编辑提示词", titleFont,
                new Rectangle(22, 14, 420, 30), Color.FromArgb(28, 33, 50),
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            TextRenderer.DrawText(g, "和下面的输入框完全同步 · 跟着主窗口一起放大缩小 · 点「完成」收起", subFont,
                new Rectangle(22, 44, ClientSize.Width - 44, 18), Color.FromArgb(150, 158, 180),
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            // 输入区外圈画一道淡边框，明确"这就是放大的那个框"
            if (big != null)
            {
                int rad = 12;
                Rectangle rc = new Rectangle(big.Left - 6, big.Top - 6, big.Width + 12, big.Height + 12);
                GraphicsPath p = new GraphicsPath();
                p.AddArc(rc.X, rc.Y, rad * 2, rad * 2, 180, 90);
                p.AddArc(rc.Right - rad * 2, rc.Y, rad * 2, rad * 2, 270, 90);
                p.AddArc(rc.Right - rad * 2, rc.Bottom - rad * 2, rad * 2, rad * 2, 0, 90);
                p.AddArc(rc.X, rc.Bottom - rad * 2, rad * 2, rad * 2, 90, 90);
                p.CloseFigure();
                using (SolidBrush b = new SolidBrush(Color.White)) g.FillPath(b, p);
                using (Pen pen = new Pen(Color.FromArgb(226, 231, 242))) g.DrawPath(pen, p);
                p.Dispose();
            }
            Ui.DrawFlashBorder(g, this, Ui.IsWin11 ? 4 : 16, Color.FromArgb(205, 108, 140, 245));
        }

        // 这个窗口不再自己拖边框缩放：尺寸完全由主窗体决定（见 SyncToOwner）
    }

    // 查看单条生成失败的真实原因（非模态，内容可滚动）
    public class ErrorDetailDialog : Form
    {
        public static void Show(GenRecord rec)
        {
            Form owner = null;
            try
            {
                foreach (Form f in Application.OpenForms)
                    if (f is MainForm) { owner = f; break; }
            }
            catch { }
            ErrorDetailDialog d = new ErrorDetailDialog(rec);
            if (owner != null) Ui.OpenDialog(owner, d, null, false, true, true);
            else d.Show();
        }

        public ErrorDetailDialog(GenRecord rec)
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            BackColor = Color.White;
            Font = new Font("微软雅黑", 10F);
            AutoScaleMode = AutoScaleMode.None;
            int W = 760, H = 900;
            Size = new Size(W, H);
            Ui.ApplyWinFrame(this, 14);

            Label t = new Label();
            t.Text = "生成失败原因";
            t.Font = new Font("微软雅黑", 15F, FontStyle.Bold);
            t.ForeColor = Color.FromArgb(28, 33, 50);
            t.Location = new Point(32, 20);
            t.Size = new Size(W - 64, 30);
            Controls.Add(t);

            string when = rec != null ? rec.Time.ToString("yyyy-MM-dd HH:mm:ss") : "";
            Label sub = new Label();
            sub.Text = when + "   ·   上方是通俗原因，下方是接口原始返回";
            sub.Font = new Font("微软雅黑", 9F);
            sub.ForeColor = Color.FromArgb(150, 158, 180);
            sub.Location = new Point(34, 56);
            sub.Size = new Size(W - 68, 20);
            Controls.Add(sub);

            // 通俗易懂的失败原因（置顶单独一块）
            FailReason.Info info = FailReason.Parse(rec == null ? "" : rec.Reason);
            int cardX = 34, cardY = 84, cardW = W - 68, cardH = 146;

            Label title = new Label();
            title.Text = info.Title;
            title.Font = new Font("微软雅黑", 13F, FontStyle.Bold);
            title.ForeColor = Color.FromArgb(205, 82, 82);
            title.BackColor = Color.FromArgb(250, 240, 241);
            title.Location = new Point(cardX + 50, cardY + 18);
            title.Size = new Size(cardW - 70, 26);
            Controls.Add(title);

            Label advice = new Label();
            advice.Text = info.Advice;
            advice.Font = new Font("微软雅黑", 9.5F);
            advice.ForeColor = Color.FromArgb(120, 100, 104);
            advice.BackColor = Color.FromArgb(250, 240, 241);
            advice.Location = new Point(cardX + 50, cardY + 50);
            advice.Size = new Size(cardW - 70, 46);
            Controls.Add(advice);

            Label fuzzy = new Label();
            string basis = string.IsNullOrEmpty(info.Basis) ? "" : "依据：" + info.Basis + "。";
            fuzzy.Text = basis + (info.Weak
                ? "这条原因无法从接口返回中确切识别，仅供参考，请以下方原始返回为准。"
                : "若与实际情况不符，请以下方原始返回为准。");
            fuzzy.Font = new Font("微软雅黑", 8.5F);
            fuzzy.ForeColor = Color.FromArgb(168, 128, 132);
            fuzzy.BackColor = Color.FromArgb(250, 240, 241);
            fuzzy.Location = new Point(cardX + 50, cardY + 96);
            fuzzy.Size = new Size(cardW - 70, 40);
            Controls.Add(fuzzy);

            Label lPrompt = new Label();
            lPrompt.Text = "提示词";
            lPrompt.Font = new Font("微软雅黑", 9F);
            lPrompt.ForeColor = Color.FromArgb(140, 148, 168);
            lPrompt.Location = new Point(cardX + 6, 0);
            lPrompt.Size = new Size(120, 20);

            PromptEdit promptView = new PromptEdit();
            promptView.Size = new Size(cardW, 124);
            promptView.BackColor = Color.FromArgb(249, 250, 253);
            promptView.Editor.BackColor = Color.FromArgb(249, 250, 253);
            promptView.Editor.ForeColor = Color.FromArgb(60, 68, 90);
            promptView.Editor.Font = new Font("微软雅黑", 9.5F);
            promptView.Editor.ReadOnly = true;
            promptView.Editor.Text = (rec == null || string.IsNullOrEmpty(rec.Prompt)) ? "（没有记录到提示词）" : ImageViewDialog.StripSys(rec.Prompt);

            // 参考图放在提示词上方：和查看大图的详情信息一样，复制、使用参考图、
            // 点开看大图（不带详情信息）都在 RefThumb 里。
            List<string> refList = new List<string>();
            if (rec != null && rec.Refs != null)
                foreach (string f in rec.Refs)
                    if (!string.IsNullOrEmpty(f) && File.Exists(f)) refList.Add(f);

            Label lRefs = new Label();
            lRefs.Text = refList.Count > 0 ? ("参考图（" + refList.Count + " 张）") : "参考图";
            lRefs.Font = new Font("微软雅黑", 9F);
            lRefs.ForeColor = Color.FromArgb(140, 148, 168);
            lRefs.Location = new Point(cardX + 6, 244);
            lRefs.Size = new Size(240, 20);

            Panel refPanel = new Panel();
            refPanel.BackColor = Color.White;
            refPanel.Location = new Point(cardX, 268);
            refPanel.Size = new Size(cardW, 80);
            for (int i = 0; i < refList.Count; i++)
            {
                RefThumb th = new RefThumb(refList[i], 68);
                th.Location = new Point(2 + i * 76, 4);
                refPanel.Controls.Add(th);
            }

            int promptY = refList.Count > 0 ? 384 : 268;
            lPrompt.Location = new Point(cardX + 6, promptY - 24);
            promptView.Location = new Point(cardX, promptY);
            Controls.Add(promptView);
            Controls.Add(lPrompt);
            if (refList.Count > 0)
            {
                Controls.Add(refPanel);
                Controls.Add(lRefs);
            }

            int reasonY = refList.Count > 0 ? 544 : 428;
            Label lReason = new Label();
            lReason.Text = "错误信息（接口原始返回）";
            lReason.Font = new Font("微软雅黑", 9F);
            lReason.ForeColor = Color.FromArgb(140, 148, 168);
            lReason.Location = new Point(cardX + 6, reasonY - 24);
            lReason.Size = new Size(260, 20);
            Controls.Add(lReason);

            PromptEdit reasonView = new PromptEdit();
            reasonView.Location = new Point(cardX, reasonY);
            reasonView.Size = new Size(cardW, H - reasonY - 70);
            reasonView.BackColor = Color.FromArgb(249, 250, 253);
            reasonView.Editor.BackColor = Color.FromArgb(249, 250, 253);
            reasonView.Editor.ForeColor = Color.FromArgb(60, 68, 90);
            reasonView.Editor.Font = new Font("微软雅黑", 9.5F);
            reasonView.Editor.ReadOnly = true;
            reasonView.Editor.Text = (rec == null || string.IsNullOrEmpty(rec.Reason)) ? "（没有记录到原因）" : rec.Reason;
            Controls.Add(reasonView);

            TextBox txt = reasonView.Editor;

            // 「重新生成」：用这条记录当时实际发出去的提示词、参考图、模型/质量/尺寸原样再发一次
            GradientButton regen = new GradientButton();
            regen.Text = "重新生成";
            regen.Location = new Point(cardX, H - 62);
            regen.Size = new Size(120, 40);
            regen.Enabled = rec != null && rec.Failed;
            regen.Click += (s, e) =>
            {
                if (rec == null) return;
                Close();
                try { if (ImagePage.Current != null) ImagePage.Current.Regenerate(rec); }
                catch { }
            };
            Controls.Add(regen);

            GradientButton ok = new GradientButton();
            ok.Text = "关闭";
            ok.Location = new Point(W - 40 - 108, H - 62);
            ok.Size = new Size(108, 40);
            ok.Click += (s, e) => Close();
            Controls.Add(ok);

            // 「使用提示词」：把这条失败记录的提示词直接填回生成图像页的输入框
            GradientButton useP = new GradientButton();
            useP.Text = "使用提示词";
            useP.Flat = true;
            useP.Font = new Font("微软雅黑", 10F);
            int useX = W - 40 - 108 - 12 - 108 - 12 - 108 - 12 - 108;
            useP.Location = new Point(useX, H - 62);
            useP.Size = new Size(108, 40);
            bool hasPrompt = rec != null && !string.IsNullOrEmpty(rec.Prompt);
            // 只回填用户自己写的提示词，不带程序自动加的那段编号说明
            string userPrompt = hasPrompt ? ImageViewDialog.StripSys(rec.Prompt) : "";
            useP.Enabled = hasPrompt;
            useP.Click += (s, e) =>
            {
                if (!hasPrompt) return;
                try { if (ImagePage.UsePromptHook != null) ImagePage.UsePromptHook(userPrompt); }
                catch { }
                Close();
            };
            Controls.Add(useP);

            // 「复制提示词」：只把提示词本身复制走，方便拿去别处改
            GradientButton copyPrompt = new GradientButton();
            copyPrompt.Text = "复制提示词";
            copyPrompt.Flat = true;
            copyPrompt.Font = new Font("微软雅黑", 10F);
            copyPrompt.Location = new Point(useX + 108 + 12, H - 62);
            copyPrompt.Size = new Size(108, 40);
            copyPrompt.Enabled = hasPrompt;
            copyPrompt.Click += (s, e) =>
            {
                if (!hasPrompt) return;
                try { Clipboard.SetText(userPrompt); copyPrompt.Text = "已复制"; copyPrompt.Invalidate(); }
                catch { }
                System.Windows.Forms.Timer tm = new System.Windows.Forms.Timer { Interval = 1300 };
                tm.Tick += (a, b) =>
                {
                    tm.Stop(); tm.Dispose();
                    if (!copyPrompt.IsDisposed) { copyPrompt.Text = "复制提示词"; copyPrompt.Invalidate(); }
                };
                tm.Start();
            };
            Controls.Add(copyPrompt);

            GradientButton copy = new GradientButton();
            copy.Text = "复制原因";
            copy.Flat = true;
            copy.Font = new Font("微软雅黑", 10F);
            copy.Location = new Point(W - 40 - 108 - 12 - 108, H - 62);
            copy.Size = new Size(108, 40);
            copy.Click += (s, e) =>
            {
                try { Clipboard.SetText(string.IsNullOrEmpty(rec.Reason) ? info.Title : info.Title + "\r\n" + info.Advice + "\r\n\r\n" + rec.Reason); DesignDialog.ShowInfo("失败原因已复制到剪贴板。"); }
                catch { }
            };
            Controls.Add(copy);

            Ui.AttachShadow(this, 28, 14);
            bool closing = false;
            FormClosing += (s, e) => closing = true;
            Deactivate += (s, e) =>
            {
                if (closing) return;
                // 锁层点击会让这里闪边；焦点真被主窗拿走时也补一次提醒
                if (Owner != null && (Form.ActiveForm == Owner || Owner.ContainsFocus)) { Ui.FlashBorder(this); return; }
                if (Form.ActiveForm == null) Ui.FlashBorder(this);
            };
            Shown += (s, e) => { try { txt.Select(0, 0); this.ActiveControl = null; } catch { } };

            this.card = new Rectangle(cardX, cardY, cardW, cardH);
        }

        Rectangle card;

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            if (card.Width > 0)
            {
                int rad = 12;
                GraphicsPath p = new GraphicsPath();
                p.AddArc(card.X, card.Y, rad * 2, rad * 2, 180, 90);
                p.AddArc(card.Right - rad * 2, card.Y, rad * 2, rad * 2, 270, 90);
                p.AddArc(card.Right - rad * 2, card.Bottom - rad * 2, rad * 2, rad * 2, 0, 90);
                p.AddArc(card.X, card.Bottom - rad * 2, rad * 2, rad * 2, 90, 90);
                p.CloseFigure();
                using (SolidBrush b = new SolidBrush(Color.FromArgb(250, 240, 241))) g.FillPath(b, p);
                using (Pen pen = new Pen(Color.FromArgb(240, 200, 200))) g.DrawPath(pen, p);
                p.Dispose();

                // 红色圆形感叹号图标
                Rectangle ic = new Rectangle(card.X + 18, card.Y + 20, 22, 22);
                using (SolidBrush b = new SolidBrush(Color.FromArgb(226, 116, 116)))
                    g.FillEllipse(b, ic);
                TextRenderer.DrawText(g, "!", new Font("微软雅黑", 11F, FontStyle.Bold), ic,
                    Color.FromArgb(255, 255, 255),
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
            Ui.DrawFlashBorder(g, this, Ui.IsWin11 ? 4 : 12, Color.FromArgb(205, 108, 140, 245));
        }
    }

    // ===== 轻提示：下载成功后弹出，支持多条同时堆叠 =====
    public static class Toast
    {
        public static Form Owner;
        static List<ToastForm> stack = new List<ToastForm>();
        const int MaxCards = 5;       // 最多叠 5 张，多的把最旧的收掉
        const int OffsetY = 54;       // 每张向下错开一点
        public const int LifeMs = 5000;   // 每张单独计时 5 秒
        static Form hookedOwner;
        static EventHandler ownerMoveH, ownerResizeH, ownerVisibleH;

        static void EnsureOwnerHook()
        {
            Form o = Owner;
            if (o == null || o.IsDisposed || o == hookedOwner) return;
            UnhookOwner();
            hookedOwner = o;
            ownerMoveH = (s, e) => Reflow();
            ownerResizeH = (s, e) => Reflow();
            ownerVisibleH = (s, e) => Reflow();
            o.Move += ownerMoveH;
            o.Resize += ownerResizeH;
            o.VisibleChanged += ownerVisibleH;
        }

        static void UnhookOwner()
        {
            Form o = hookedOwner;
            if (o != null && !o.IsDisposed)
            {
                if (ownerMoveH != null) o.Move -= ownerMoveH;
                if (ownerResizeH != null) o.Resize -= ownerResizeH;
                if (ownerVisibleH != null) o.VisibleChanged -= ownerVisibleH;
            }
            hookedOwner = null;
            ownerMoveH = null;
            ownerResizeH = null;
            ownerVisibleH = null;
        }

        public static void Show(string text, string folder)
        {
            try
            {
                EnsureOwnerHook();
                while (stack.Count >= MaxCards)
                {
                    ToastForm old = stack[0];
                    stack.RemoveAt(0);
                    try { old.DismissSilently(); } catch { }
                }
                ToastForm t = new ToastForm(text, folder);
                t.Dismissed += (s, e) =>
                {
                    stack.Remove(t);
                    Reflow();
                };
                stack.Add(t);
                t.Show();
                Reflow();
            }
            catch { }
        }

        static void Reflow()
        {
            Form o = Owner;
            if (o == null || o.IsDisposed || !o.Visible || o.WindowState == FormWindowState.Minimized)
            {
                // 主窗不在桌面上时，提示不能孤零零留在桌面；收起来，恢复后再显示
                for (int i = 0; i < stack.Count; i++)
                {
                    ToastForm t = stack[i];
                    if (t != null && !t.IsDisposed && t.Visible) t.Hide();
                }
                return;
            }

            Rectangle r = o.WindowState == FormWindowState.Maximized ? Screen.FromControl(o).WorkingArea : o.Bounds;
            int baseY = r.Y + 64;
            for (int i = 0; i < stack.Count; i++)
            {
                ToastForm t = stack[i];
                if (t == null || t.IsDisposed) continue;
                t.Location = new Point(r.X + (r.Width - t.Width) / 2, baseY + i * OffsetY);
                if (!t.Visible) t.Show();
            }
        }
    }

    public class ToastForm : Form
    {
        string msg, folder;
        Rectangle iconRect;
        bool hoverIcon;
        Timer tm;
        int life;
        Font f;
        bool silent;                       // 被"超出 5 张"强行收掉时不再通知外层去重排

        public event EventHandler Dismissed;

        public void DismissSilently()
        {
            silent = true;
            Close();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);
            if (tm != null) { tm.Stop(); tm.Dispose(); tm = null; }
            if (!silent && Dismissed != null) { try { Dismissed(this, EventArgs.Empty); } catch { } }
        }

        public ToastForm(string text, string folderPath)
        {
            msg = text; folder = folderPath;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;               // 压在查看器/其它弹窗之上，保证下载提示始终可见
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.FromArgb(252, 253, 255);
            DoubleBuffered = true;
            f = new Font("微软雅黑", 9.5F);
            int tw = TextRenderer.MeasureText(msg, f, new Size(1000, 40), TextFormatFlags.NoPadding).Width;
            bool hasFolder = !string.IsNullOrEmpty(folder);
            Size = new Size(Math.Min(900, tw + (hasFolder ? 22 + 30 + 30 + 16 : 24)), 48);
            iconRect = hasFolder ? new Rectangle(Width - 16 - 30, (Height - 30) / 2, 30, 30) : Rectangle.Empty;
            Cursor = Cursors.Default;
            tm = new Timer { Interval = 100 };
            tm.Tick += (s, e) =>
            {
                life += 100;
                if (life >= Toast.LifeMs) { tm.Stop(); Close(); }   // 每张自己计时 5 秒，到点自己消失
            };
            tm.Start();
            Ui.AttachShadow(this, 18, 10);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Ui.ApplyWinFrame(this, 12);      // 圆角裁掉四角的白边，让四周真正透明
            Ui.Composite(this);
        }

        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x08000000;   // WS_EX_NOACTIVATE：弹出时不抢焦点
                return cp;
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            bool h = iconRect.Contains(e.Location);
            if (h != hoverIcon) { hoverIcon = h; Cursor = h ? Cursors.Hand : Cursors.Default; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (hoverIcon) { hoverIcon = false; Invalidate(); }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (iconRect.Contains(e.Location)) OpenFolder();
            else Close();       // 点提示框空白处也直接收掉，不留一个拿不掉的白色方块
        }

        void OpenFolder()
        {
            // 先停掉计时器把自己关掉，再去开资源管理器：
            // 之前是先 Process.Start 再 Close，资源管理器抢到前台后这里收不到 MouseLeave，
            // hoverIcon 一直为 true 把倒计时清零，提示框就永远置顶卡在屏幕上了
            hoverIcon = false;
            if (tm != null) { tm.Stop(); tm.Dispose(); tm = null; }
            Close();
            try
            {
                if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder)) System.Diagnostics.Process.Start("explorer.exe", "\"" + folder + "\"");
            }
            catch { }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle rc = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath p = Ui.Rounded(rc, 12))
            {
                using (SolidBrush b = new SolidBrush(Color.FromArgb(252, 253, 255))) g.FillPath(b, p);
                using (Pen pen = new Pen(Color.FromArgb(223, 229, 243))) g.DrawPath(pen, p);
            }
            TextRenderer.DrawText(g, msg, f, new Rectangle(16, 0, Width - 16 - (iconRect.Width > 0 ? 30 + 16 + 10 : 16), Height), Color.FromArgb(56, 64, 86),
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

            // 右侧文件夹按钮：只有带目录的下载提示才画；更新进度这类纯文字提示不画无用图标
            if (iconRect.Width > 0)
            {
                if (hoverIcon)
                {
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(238, 241, 252)))
                        g.FillEllipse(b, iconRect);
                }
                Color ic = hoverIcon ? Color.FromArgb(108, 124, 214) : Color.FromArgb(120, 130, 154);
                using (Pen pen = new Pen(ic, 1.6f))
                {
                    pen.StartCap = LineCap.Round; pen.EndCap = LineCap.Round;
                    Rectangle fr = new Rectangle(iconRect.X + 6, iconRect.Y + 9, 18, 13);
                    using (GraphicsPath p = Ui.Rounded(fr, 3))
                        g.DrawPath(pen, p);
                    g.DrawLine(pen, fr.X, fr.Y + 3, fr.Right, fr.Y + 3);
                }
            }
            base.OnPaint(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { if (tm != null) { tm.Stop(); tm.Dispose(); tm = null; } if (f != null) { f.Dispose(); f = null; } }
            base.Dispose(disposing);
        }
    }
}
