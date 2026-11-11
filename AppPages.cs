using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace AiAnimeTool
{
    public class PageBase : Panel
    {
        // 每个控件在“基准尺寸”下的位置与大小，以及它如何跟随窗口变化：
        // stretchW = 宽度随窗口加宽（输入框、列表），stretchH = 高度随窗口加高（列表），
        // anchorRight = 贴着内容块右边（浏览按钮、右侧提示），窗口加宽时跟着往右走。
        class LayInfo
        {
            public Rectangle rect;
            public bool stretchW, stretchH, anchorRight, downH;
            public bool noBound;    // 不参与"内容整体居中"的包围盒（纯装饰性的提示文字，拉歪居中）
        }
        Dictionary<Control, LayInfo> _lay = new Dictionary<Control, LayInfo>();
        protected bool autoCenter;      // 该页是否按内容整体居中（新建项目 / 批量重命名 = true，图像页不走这里）

        public void RecenterIfAuto()
        {
            if (autoCenter) ApplyLay();
        }

        /// <summary>登记当前所有控件为基准位置（构造末尾调用一次）。</summary>
        protected void TrackAll()
        {
            foreach (Control c in Controls)
                if (!_lay.ContainsKey(c))
                    _lay[c] = new LayInfo { rect = new Rectangle(c.Left, c.Top, c.Width, c.Height) };
        }
        protected void StretchWidth(params Control[] cs) { foreach (Control c in cs) if (_lay.ContainsKey(c)) _lay[c].stretchW = true; }
        protected void StretchHeight(params Control[] cs) { foreach (Control c in cs) if (_lay.ContainsKey(c)) _lay[c].stretchH = true; }
        protected void AnchorRight(params Control[] cs) { foreach (Control c in cs) if (_lay.ContainsKey(c)) _lay[c].anchorRight = true; }
        /// <summary>这些控件不参与居中包围盒的计算（只是装饰性提示，算进去会让整块内容偏左/偏右）。</summary>
        protected void NotInBounds(params Control[] cs) { foreach (Control c in cs) if (_lay.ContainsKey(c)) _lay[c].noBound = true; }
        /// <summary>基准 Y 位于 belowY 以下的控件，窗口变高时整体下移，给变高的列表让位。</summary>
        protected void ShiftDownBelow(int belowY, Control except)
        {
            foreach (KeyValuePair<Control, LayInfo> kv in _lay)
                if (kv.Key != except && kv.Value.rect.Y >= belowY) kv.Value.downH = true;
        }

        /// <summary>按当前窗口尺寸重排：内容块整体居中，输入框/列表跟着窗口加宽加高，字号与按钮尺寸恒定。</summary>
        protected void ApplyLay()
        {
            if (_lay.Count == 0) return;
            int baseW = Program.WinW - Program.SideW - 12;
            int baseH = Program.WinH - Program.TitleH - 12;
            int extraW = Width - baseW;
            int extraH = Math.Max(0, Height - baseH);

            int left = int.MaxValue, top = int.MaxValue, right = int.MinValue, bottom = int.MinValue;
            foreach (KeyValuePair<Control, LayInfo> kv in _lay)
            {
                LayInfo li = kv.Value;
                int w = li.rect.Width + (li.stretchW ? extraW : 0);
                if (li.stretchW && w < 80) w = 80;      // 只有被拉伸的输入框/列表才保底，标签保持原宽度，否则会压住相邻的输入框
                int h = li.rect.Height + (li.stretchH ? extraH : 0);
                // 包围盒必须按"实际会摆到的位置"量：贴右的控件在窄窗口里会随 extraW 左移，
                // 用基准 x 量的话窗口比基准窄时整块内容会被算偏，左右空隙就不等宽了
                int x = li.rect.X + (li.anchorRight ? extraW : 0);
                int y0 = li.rect.Y + (li.downH ? extraH : 0);
                if (!li.noBound)
                {
                    if (x < left) left = x;
                    if (y0 < top) top = y0;
                    if (x + w > right) right = x + w;
                    if (y0 + h > bottom) bottom = y0 + h;
                }
            }
            if (left == int.MaxValue) return;
            int bw = right - left, bh = bottom - top;
            int dx = (Width - bw) / 2 - left;
            int dy = (Height - bh) / 2 - top;
            // 内容不许被推出页面
            if (dx < -left) dx = -left;
            if (dx > Width - right) dx = Width - right;
            if (dy < -top) dy = -top;
            if (dy > Height - bottom) dy = Height - bottom;

            SuspendLayout();
            try
            {
                foreach (KeyValuePair<Control, LayInfo> kv in _lay)
                {
                    LayInfo li = kv.Value;
                    Control c = kv.Key;
                    int w = li.rect.Width + (li.stretchW ? extraW : 0);
                    if (li.stretchW && w < 80) w = 80;
                    int h = li.rect.Height + (li.stretchH ? extraH : 0);
                    int x = li.rect.X + (li.anchorRight ? extraW : 0) + dx;
                    int y = li.rect.Y + (li.downH ? extraH : 0) + dy;
                    if (c.Bounds.Width != w || c.Bounds.Height != h) c.Size = new Size(w, h);
                    if (c.Left != x || c.Top != y) c.Location = new Point(x, y);
                }
            }
            finally { ResumeLayout(true); }
            Invalidate(true);
        }

        public PageBase()
        {
            Size = new Size(Program.WinW - Program.SideW, Program.WinH - Program.TitleH);
            BackColor = Color.White;
            Visible = false;
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            MouseDown += (s, e) => { Form f = FindForm(); if (f != null) f.ActiveControl = null; };
        }

        protected Label Lbl(string text, int x, int y, int w, int h = 24, Color? color = null, float size = 10F, FontStyle style = FontStyle.Regular)
        {
            Label l = new Label();
            l.Text = text;
            l.Font = new Font("微软雅黑", size, style);
            l.ForeColor = color ?? Color.FromArgb(60, 68, 90);
            l.Location = new Point(x, y);
            l.Size = new Size(w, h);
            l.TextAlign = ContentAlignment.MiddleLeft;
            l.MouseDown += (s, e) => { Form f = FindForm(); if (f != null) f.ActiveControl = null; };
            return l;
        }
        protected FieldBox Field(int x, int y, int w, int h = 38)
        {
            FieldBox f = new FieldBox();
            f.Location = new Point(x, y);
            f.Size = new Size(w, h);
            return f;
        }
        protected GradientButton Btn(string text, int x, int y, int w, int h = 38)
        {
            GradientButton b = new GradientButton();
            b.Text = text;
            b.Flat = true;
            b.Font = new Font("微软雅黑", 10F);
            b.Location = new Point(x, y);
            b.Size = new Size(w, h);
            return b;
        }
        protected GradientButton PrimaryBtn(string text, int x, int y, int w, int h)
        {
            GradientButton b = new GradientButton();
            b.Text = text;
            b.Location = new Point(x, y);
            b.Size = new Size(w, h);
            return b;
        }
        protected string BrowseFolder()
        {
            using (FolderBrowserDialog dlg = new FolderBrowserDialog())
            {
                dlg.Description = "选择文件夹";
                if (dlg.ShowDialog() == DialogResult.OK) return dlg.SelectedPath;
            }
            return null;
        }
        protected void SelectInExplorer(string path)
        {
            try { System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + path + "\""); } catch { }
        }

        // 不再等比缩放页面内容：字号、缩略图、按钮尺寸恒定，
        // 只由各页面在 OnResize 里把“容器”撑大，容纳更多内容。
        public void FitTo(float kx, float ky)
        {
        }
    }

    public class ProjectPage : PageBase
    {
        FieldBox fLocation, fName, fScript, fStart, fEnd;
        NumberStylePicker picker;
        GradientButton btnBrowseCtl, btnScriptCtl;
        Control trashCtl, hintScriptCtl;
        string scriptPath;

        public ProjectPage()
        {
            autoCenter = true;
            int x = 96, y = 46;

            Label heading = Lbl("新建项目", x, y, 260, 34, Color.FromArgb(28, 33, 50), 20F, FontStyle.Bold);
            Controls.Add(heading);
            Controls.Add(Lbl("一键创建项目文件夹与全部子结构", x + 2, y + 36, 460, 20, Color.FromArgb(140, 148, 168), 9F));

            y = 116;
            Controls.Add(Lbl("创建位置", x, y + 8, 110));
            fLocation = Field(x + 110, y, 480);
            fLocation.SetReadOnly(true);
            Controls.Add(fLocation);
            GradientButton btnBrowse = Btn("浏览", x + 600, y, 100);
            btnBrowseCtl = btnBrowse;
            btnBrowse.Click += (s, e) =>
            {
                string p = BrowseFolder();
                if (p != null) { fLocation.TextValue = p; Program.Config.project.location = p; Program.SaveConfig(); }
            };
            Controls.Add(btnBrowse);

            y += 58;
            Controls.Add(Lbl("项目名称", x, y + 8, 110));
            fName = Field(x + 110, y, 480);
            Controls.Add(fName);

            y += 58;
            Controls.Add(Lbl("剧本", x, y + 8, 110));
            fScript = Field(x + 110, y, 480);
            fScript.SetReadOnly(true);
            fScript.AllowDrop = true;
            Panel trash = new Panel();
            trashCtl = trash;
            trash.Location = new Point(x + 708, y + 6);
            trash.Size = new Size(26, 26);
            trash.Cursor = Cursors.Hand;
            trash.Visible = false;
            bool trashHover = false;
            trash.Paint += (s, e) =>
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                Color c = trashHover ? Color.FromArgb(229, 72, 77) : Color.FromArgb(150, 158, 180);
                using (Pen p = new Pen(c, 1.6f))
                {
                    g.DrawLine(p, 8, 9, 18, 9);
                    g.DrawLine(p, 10, 9, 11, 6);
                    g.DrawLine(p, 16, 9, 15, 6);
                    g.DrawLine(p, 9, 7, 17, 7);
                    g.DrawRectangle(p, 8, 11, 10, 12);
                    g.DrawLine(p, 11, 13, 11, 21);
                    g.DrawLine(p, 14, 13, 14, 21);
                }
            };
            trash.MouseEnter += (s, e) => { trashHover = true; trash.Invalidate(); };
            trash.MouseLeave += (s, e) => { trashHover = false; trash.Invalidate(); };
            trash.Click += (s, e) => { scriptPath = null; fScript.TextValue = ""; trash.Visible = false; };
            fScript.DragEnter += (s, e) => { if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy; };
            fScript.DragDrop += (s, e) =>
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files != null && files.Length > 0 && File.Exists(files[0]) && files[0].ToLower().EndsWith(".docx"))
                { scriptPath = files[0]; fScript.TextValue = Path.GetFileName(files[0]); trash.Visible = true; }
                else DesignDialog.ShowWarn("请拖入 .docx 格式的剧本文件！");
            };
            Controls.Add(fScript);
            GradientButton btnScript = Btn("选择", x + 600, y, 100);
            btnScriptCtl = btnScript;
            btnScript.Click += (s, e) =>
            {
                using (OpenFileDialog dlg = new OpenFileDialog())
                {
                    dlg.Filter = "Word文档(*.docx)|*.docx";
                    if (dlg.ShowDialog() == DialogResult.OK) { scriptPath = dlg.FileName; fScript.TextValue = Path.GetFileName(dlg.FileName); trash.Visible = true; }
                }
            };
            Controls.Add(btnScript);
            Controls.Add(trash);
            hintScriptCtl = Lbl("支持拖入 .docx 文件", x + 748, y + 8, 200, 22, Color.FromArgb(140, 148, 168), 9F);
            Controls.Add(hintScriptCtl);

            y += 58;
            Controls.Add(Lbl("集数", x, y + 8, 110));
            Controls.Add(Lbl("第", x + 118, y + 8, 20));
            fStart = Field(x + 138, y, 76);
            fStart.Inner.TextAlign = HorizontalAlignment.Center;
            fStart.SetDigitsOnly();
            Controls.Add(fStart);
            Controls.Add(Lbl("集 至 第", x + 222, y + 8, 64));
            fEnd = Field(x + 290, y, 76);
            fEnd.Inner.TextAlign = HorizontalAlignment.Center;
            fEnd.SetDigitsOnly();
            Controls.Add(fEnd);
            Controls.Add(Lbl("集", x + 374, y + 8, 20));
            Label errDigits = Lbl("! 只能输入纯数字", x + 138, y + 40, 260, 18, Color.FromArgb(220, 68, 68), 8.5F);
            errDigits.Visible = false;
            Controls.Add(errDigits);
            fStart.ErrHint = errDigits;
            fEnd.ErrHint = errDigits;
            Controls.Add(Lbl("数字样式", x + 420, y + 8, 90));
            picker = new NumberStylePicker();
            picker.Location = new Point(x + 510, y);
            picker.Size = new Size(170, 38);
            picker.Value = Program.Config.project.numStyle;
            picker.ValueChanged += (s, e) => { Program.Config.project.numStyle = picker.Value; Program.SaveConfig(); };
            Controls.Add(picker);

            y += 64;
            Controls.Add(Lbl("将在「02-成品」→「05-原片素材」内创建序列文件夹，每集一个：第1集、第2集……", x, y, 700, 22, Color.FromArgb(140, 148, 168), 9F));

            y += 74;
            GradientButton btnCreate = PrimaryBtn("新建项目", x, y, 300, 56);
            btnCreate.Click += (s, e) => CreateProject();
            Controls.Add(btnCreate);
            Controls.Add(Lbl("创建完成后将自动打开并选中新文件夹", x + 316, y + 18, 300, 22, Color.FromArgb(140, 148, 168), 9F));

            fLocation.TextValue = Program.Config.project.location;
            TrackAll();
            StretchWidth(fLocation, fName, fScript);
            AnchorRight(btnBrowseCtl, btnScriptCtl, trashCtl, hintScriptCtl);
            NotInBounds(hintScriptCtl, trashCtl);      // 跟在「选择」右边的小提示、删除小图标都是装饰，算进包围盒会把整块内容拉偏
            ApplyLay();           // 首帧就按当前窗口尺寸居中裁好，不靠 Resize 事件
        }

        void CreateProject()
        {
            string loc = fLocation.TextValue;
            string name = fName.TextValue;
            int start, end;
            if (string.IsNullOrEmpty(loc)) { DesignDialog.ShowWarn("请选择创建位置！"); return; }
            if (!Directory.Exists(loc)) { DesignDialog.ShowWarn("创建位置不存在！"); return; }
            if (string.IsNullOrEmpty(name)) { DesignDialog.ShowWarn("请输入项目名称！"); return; }
            if (!int.TryParse(fStart.Inner.Text, out start) || !int.TryParse(fEnd.Inner.Text, out end) || start < 1 || end < start)
            { DesignDialog.ShowWarn("集数只能输入纯数字，请填写有效的集数范围！"); return; }

            string root = Path.Combine(loc, name);
            if (Directory.Exists(root)) { DesignDialog.ShowWarn("该文件夹已存在！"); return; }
            try
            {
                Directory.CreateDirectory(root);
                string[] main = { "01-剧本", "02-成品", "03-人物资产", "04-场景资产", "05-道具资产", "06-音频资产", "07-其他资产" };
                foreach (string s in main) Directory.CreateDirectory(Path.Combine(root, s));

                string chengpin = Path.Combine(root, "02-成品");
                string[] sub = { "01-成片第集——第集", "02-无字幕成片", "03-字幕文件", "04-高光剪辑", "05-原片素材", "06-AI创作过程截图", "07-工程文件截图" };
                foreach (string s in sub) Directory.CreateDirectory(Path.Combine(chengpin, s));

                string yuanpian = Path.Combine(chengpin, "05-原片素材");
                string style = picker.Value;
                for (int i = start; i <= end; i++)
                    Directory.CreateDirectory(Path.Combine(yuanpian, "第" + Program.FormatNum(i, style) + "集"));

                if (!string.IsNullOrEmpty(scriptPath) && File.Exists(scriptPath))
                {
                    string dest = Path.Combine(Path.Combine(root, "01-剧本"), Path.GetFileName(scriptPath));
                    File.Copy(scriptPath, dest, true);
                }

                Program.Config.project.location = loc;
                Program.SaveConfig();
                SelectInExplorer(root);
            }
            catch (Exception ex) { DesignDialog.ShowError("创建失败：" + ex.Message); }
        }
        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            ApplyLay();
        }
    }

    public class RenamePage : PageBase
    {
        FieldBox fFolder, fStart, fPrefix, fSuffix;
        NumberStylePicker picker;
        FileCheckList lstFiles;
        ComboBox cboType;
        GradientButton btnUndo, btnBrowseCtl;
        string currentFolder;
        static readonly string[] videoExts = { ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".flv", ".rmvb", ".rm", ".ts", ".m4v", ".webm", ".mpg", ".mpeg", ".3gp", ".f4v", ".vob" };

        public RenamePage()
        {
            autoCenter = true;
            int x = 96, y = 46;
            Controls.Add(Lbl("批量重命名", x, y, 260, 34, Color.FromArgb(28, 33, 50), 20F, FontStyle.Bold));
            Controls.Add(Lbl("按文件创建时间（精确到秒）排序，勾选后批量重命名", x + 2, y + 36, 460, 20, Color.FromArgb(140, 148, 168), 9F));

            y = 116;
            Controls.Add(Lbl("选择文件夹", x, y + 8, 110));
            fFolder = Field(x + 110, y, 480);
            fFolder.SetReadOnly(true); // 只读不可点、不可改、不可选中文字
            Controls.Add(fFolder);
            GradientButton btnBrowse = Btn("浏览", x + 600, y, 100);
            btnBrowseCtl = btnBrowse;
            btnBrowse.Click += (s, e) =>
            {
                string p = BrowseFolder();
                if (p != null) { fFolder.TextValue = p; Program.Config.rename.folder = p; Program.SaveConfig(); RefreshFiles(); }
            };
            Controls.Add(btnBrowse);

            y += 58;
            cboType = new ComboBox();
            cboType.Items.AddRange(new object[] { "视频文件", "全部文件" });
            cboType.SelectedIndex = 0;
            cboType.DropDownStyle = ComboBoxStyle.DropDownList;
            cboType.FlatStyle = FlatStyle.Flat;
            cboType.Font = new Font("微软雅黑", 10F);
            cboType.BackColor = Color.FromArgb(246, 248, 252);
            cboType.Location = new Point(x, y);
            cboType.Size = new Size(130, 30);
            cboType.SelectedIndexChanged += (s, e) => RefreshFiles();
            Controls.Add(cboType);

            GradientButton bRefresh = Btn("刷新", x + 150, y, 80);
            bRefresh.Click += (s, e) => RefreshFiles();
            Controls.Add(bRefresh);
            GradientButton bAll = Btn("全选", x + 240, y, 80);
            bAll.Click += (s, e) => { for (int i = 0; i < lstFiles.Items.Count; i++) lstFiles.SetItemChecked(i, true); };
            Controls.Add(bAll);
            GradientButton bNone = Btn("全不选", x + 330, y, 80);
            bNone.Click += (s, e) => { for (int i = 0; i < lstFiles.Items.Count; i++) lstFiles.SetItemChecked(i, false); };
            Controls.Add(bNone);
            GradientButton bInv = Btn("反选", x + 420, y, 80);
            bInv.Click += (s, e) => { for (int i = 0; i < lstFiles.Items.Count; i++) lstFiles.SetItemChecked(i, !lstFiles.GetItemChecked(i)); };
            Controls.Add(bInv);

            y += 48;
            lstFiles = new FileCheckList();
            lstFiles.Location = new Point(x, y);
            lstFiles.Size = new Size(700, 300);
            lstFiles.CheckOnClick = true;
            Controls.Add(lstFiles);

            y += 330;
            Controls.Add(Lbl("起始集数", x, y + 8, 80));
            fStart = Field(x + 80, y, 76);
            fStart.Inner.TextAlign = HorizontalAlignment.Center;
            fStart.SetDigitsOnly();
            Controls.Add(fStart);
            Label errStart = Lbl("! 只能输入纯数字", x + 80, y + 40, 260, 18, Color.FromArgb(220, 68, 68), 8.5F);
            errStart.Visible = false;
            Controls.Add(errStart);
            fStart.ErrHint = errStart;
            Controls.Add(Lbl("前缀", x + 172, y + 8, 46));
            fPrefix = Field(x + 218, y, 90);
            fPrefix.Inner.TextAlign = HorizontalAlignment.Center;
            fPrefix.TextValue = "第";
            Controls.Add(fPrefix);
            Controls.Add(Lbl("后缀", x + 324, y + 8, 46));
            fSuffix = Field(x + 370, y, 90);
            fSuffix.Inner.TextAlign = HorizontalAlignment.Center;
            fSuffix.TextValue = "集";
            Controls.Add(fSuffix);
            Controls.Add(Lbl("数字样式", x + 476, y + 8, 90));
            picker = new NumberStylePicker();
            picker.Location = new Point(x + 566, y);
            picker.Size = new Size(170, 38);
            picker.Value = Program.Config.rename.numStyle;
            picker.ValueChanged += (s, e) => { Program.Config.rename.numStyle = picker.Value; Program.SaveConfig(); };
            Controls.Add(picker);

            y += 62;
            y += 52;
            GradientButton bPreview = Btn("预览", x, y, 110, 46);
            bPreview.Click += (s, e) => Preview();
            Controls.Add(bPreview);
            GradientButton bRename = PrimaryBtn("开始重命名", x + 124, y, 140, 46);
            bRename.Click += (s, e) => DoRename();
            Controls.Add(bRename);
            btnUndo = Btn("撤销重命名", x + 278, y, 140, 46);
            btnUndo.Click += (s, e) => UndoRename();
            Controls.Add(btnUndo);

            fFolder.TextValue = Program.Config.rename.folder;
            btnUndo.Enabled = (Program.Config.rename.undo != null && Program.Config.rename.undo.Count > 0);
            RefreshFiles();
            TrackAll();
            StretchWidth(fFolder);
            StretchHeight(lstFiles);
            ShiftDownBelow(lstFiles.Bottom, lstFiles);   // 列表变高后，起始集数/按钮等整体下移
            AnchorRight(btnBrowseCtl);
            ApplyLay();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            ApplyLay();
        }

        List<string> GetChecked()
        {
            List<string> list = new List<string>();
            foreach (object o in lstFiles.CheckedItems) list.Add(o.ToString());
            return list;
        }

        void Preview()
        {
            var items = GetChecked();
            if (items.Count == 0) { DesignDialog.ShowWarn("请先勾选要重命名的文件！"); return; }
            int start;
            if (!int.TryParse(fStart.Inner.Text, out start)) { DesignDialog.ShowWarn("起始集数只能输入纯数字，请填写有效的起始集数！"); return; }
            List<string> oldN = new List<string>();
            List<string> newN = new List<string>();
            for (int i = 0; i < items.Count; i++)
            {
                int num = start + i;
                oldN.Add(Path.GetFileName(items[i]));
                newN.Add(fPrefix.TextValue + Program.FormatNum(num, picker.Value) + fSuffix.TextValue + Path.GetExtension(items[i]));
            }
            string summary = "按创建时间（精确到秒）排序  ·  起始集数 " + start + "  ·  数字样式 " + Program.StyleName(picker.Value);
            PreviewDialog d = new PreviewDialog(summary, oldN, newN);
            Form owner = this.FindForm();
            Ui.OpenDialog(owner, d);
        }

        void DoRename()
        {
            var items = GetChecked();
            if (items.Count == 0) { DesignDialog.ShowWarn("请先勾选要重命名的文件！"); return; }
            int start;
            if (!int.TryParse(fStart.Inner.Text, out start)) { DesignDialog.ShowWarn("起始集数只能输入纯数字，请填写有效的起始集数！"); return; }
            List<RenameItem> renamed = new List<RenameItem>();
            try
            {
                string[] tmp = new string[items.Count];
                for (int i = 0; i < items.Count; i++)
                {
                    string dir = Path.GetDirectoryName(items[i]);
                    tmp[i] = Path.Combine(dir, "__tmp_ren_" + Guid.NewGuid().ToString("N") + Path.GetExtension(items[i]));
                    File.Move(items[i], tmp[i]);
                }
                for (int i = 0; i < items.Count; i++)
                {
                    int num = start + i;
                    string newName = fPrefix.TextValue + Program.FormatNum(num, picker.Value) + fSuffix.TextValue + Path.GetExtension(items[i]);
                    string newPath = Path.Combine(Path.GetDirectoryName(items[i]), newName);
                    File.Move(tmp[i], newPath);
                    RenameItem ri = new RenameItem();
                    ri.oldPath = items[i];
                    ri.newPath = newPath;
                    renamed.Add(ri);
                }
                Program.Config.rename.undo = renamed;
                btnUndo.Enabled = true;
                Program.SaveConfig();
                RefreshFiles();
                // 默认完成不弹任何提示，直接打开重命名所在的文件夹
                if (items.Count > 0)
                {
                    try { System.Diagnostics.Process.Start("explorer.exe", "\"" + Path.GetDirectoryName(items[0]) + "\""); } catch { }
                }
            }
            catch (Exception ex) { DesignDialog.ShowError("重命名失败：" + ex.Message); }
        }

        void UndoRename()
        {
            if (Program.Config.rename.undo == null || Program.Config.rename.undo.Count == 0) return;
            var existing = Program.Config.rename.undo.Where(r => File.Exists(r.newPath)).ToList();
            if (existing.Count == 0)
            {
                DesignDialog.ShowWarn("文件已不存在。");
                Program.Config.rename.undo = new List<RenameItem>();
                btnUndo.Enabled = false;
                Program.SaveConfig();
                return;
            }
            DesignDialog.ShowConfirm("确定撤销上次重命名？将恢复 " + existing.Count + " 个文件名。", "确认", () =>
            {
                try
                {
                    foreach (var r in existing) { if (File.Exists(r.newPath)) File.Move(r.newPath, r.oldPath); }
                    Program.Config.rename.undo = new List<RenameItem>();
                    btnUndo.Enabled = false;
                    Program.SaveConfig();
                    RefreshFiles();
                    DesignDialog.ShowInfo("已撤销。");
                }
                catch (Exception ex) { DesignDialog.ShowError("撤销失败：" + ex.Message); }
            });
        }

        void RefreshFiles()
        {
            lstFiles.Items.Clear();
            string folder = fFolder.TextValue;
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return;
            currentFolder = folder;
            try
            {
                var files = Directory.GetFiles(folder).Select(f => new FileInfo(f))
                    .OrderBy(f => f.CreationTime).ThenBy(f => f.Name).ToList();
                foreach (var fi in files)
                {
                    if (cboType.SelectedIndex == 0 && !videoExts.Contains(fi.Extension.ToLower())) continue;
                    lstFiles.Items.Add(fi.FullName);
                }
            }
            catch { }
        }
    }

    // 浅紫渐变自绘文件列表（替代系统黑框 CheckedListBox）
    public class FileCheckList : Control
    {
        public List<string> Items = new List<string>();
        HashSet<int> checkedIdx = new HashSet<int>();
        public bool CheckOnClick = true;
        public int ItemHeight = 40;
        public int RowGap = 8;
        int scroll;
        bool draggingScroll;
        int dragStartY, dragStartScroll;
        int hoverIdx = -1;

        public FileCheckList()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            DoubleBuffered = true;
            BackColor = Color.White;
            Cursor = Cursors.Hand;
        }

        int Step { get { return ItemHeight + RowGap; } }
        int VisibleCount { get { if (Step <= 0) return 0; return Math.Max(1, (Height - 8) / Step); } }
        bool NeedScroll { get { return Items.Count * Step > Height - 8; } }
        int MaxScroll { get { return Math.Max(0, Items.Count - VisibleCount); } }

        public void Add(string s) { Items.Add(s); Invalidate(); }
        public void Clear() { Items.Clear(); checkedIdx.Clear(); scroll = 0; Invalidate(); }
        public bool GetItemChecked(int i) { return i >= 0 && i < Items.Count && checkedIdx.Contains(i); }
        public void SetItemChecked(int i, bool c)
        {
            if (i < 0 || i >= Items.Count) return;
            if (c) checkedIdx.Add(i); else checkedIdx.Remove(i);
            Invalidate();
        }
        public List<string> CheckedItems
        {
            get { var r = new List<string>(); for (int i = 0; i < Items.Count; i++) if (checkedIdx.Contains(i)) r.Add(Items[i]); return r; }
        }

        int IndexFromY(int y) { if (Step <= 0) return -1; return scroll + (y - 4) / Step; }
        Rectangle ScrollBarRect() { return new Rectangle(Width - 8 - 4, 4, 8, Height - 8); }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            if (Items.Count > 0)
            {
                scroll -= (e.Delta > 0 ? 1 : -1) * 3;
                scroll = Math.Max(0, Math.Min(MaxScroll, scroll));
                Invalidate();
            }
            base.OnMouseWheel(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) { base.OnMouseDown(e); return; }
            if (NeedScroll && ScrollBarRect().Contains(e.Location))
            {
                draggingScroll = true; dragStartY = e.Y; dragStartScroll = scroll;
                return;
            }
            int idx = IndexFromY(e.Y);
            if (CheckOnClick && idx >= 0 && idx < Items.Count)
            {
                if (checkedIdx.Contains(idx)) checkedIdx.Remove(idx); else checkedIdx.Add(idx);
                Invalidate();
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (draggingScroll)
            {
                int range = Height - 8 - ThumbH();
                if (range > 0 && MaxScroll > 0)
                {
                    double ratio = (double)(e.Y - 4 - ThumbH() / 2.0) / range;
                    scroll = (int)Math.Round(ratio * MaxScroll);
                    scroll = Math.Max(0, Math.Min(MaxScroll, scroll));
                    Invalidate();
                }
                base.OnMouseMove(e);
                return;
            }
            int hi = IndexFromY(e.Y);
            if (hi != hoverIdx) { hoverIdx = hi; Invalidate(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e) { hoverIdx = -1; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseUp(MouseEventArgs e) { draggingScroll = false; base.OnMouseUp(e); }
        protected override void OnResize(EventArgs e)
        {
            scroll = Math.Max(0, Math.Min(MaxScroll, scroll));
            base.OnResize(e);
            Invalidate();
        }

        int ThumbH()
        {
            int range = Height - 8;
            if (!NeedScroll) return range;
            int th = range * VisibleCount / Math.Max(1, Items.Count);
            return Math.Max(28, th);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            Rectangle outer = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath po = Ui.Rounded(outer, 12))
            using (LinearGradientBrush bg = new LinearGradientBrush(outer, Color.FromArgb(247, 248, 253), Color.FromArgb(239, 239, 252), 90f))
                g.FillPath(bg, po);

            if (Items.Count == 0)
            {
                TextRenderer.DrawText(g, "暂无文件", new Font("微软雅黑", 11F), outer, Color.FromArgb(160, 168, 190),
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                return;
            }

            int vc = VisibleCount;
            for (int i = 0; i < vc; i++)
            {
                int idx = scroll + i;
                if (idx >= Items.Count) break;
                DrawRow(g, idx, 4 + i * Step);
            }

            if (NeedScroll)
            {
                Rectangle sb = ScrollBarRect();
                using (SolidBrush b = new SolidBrush(Color.FromArgb(36, 108, 140, 245))) g.FillRectangle(b, sb);
                int th = ThumbH();
                int ty = 4 + (MaxScroll > 0 ? (int)((double)scroll / MaxScroll * (Height - 8 - th)) : 0);
                using (SolidBrush b = new SolidBrush(Color.FromArgb(120, 108, 140, 245))) g.FillRectangle(b, new Rectangle(sb.X, ty, sb.Width, th));
            }
        }

        void DrawRow(Graphics g, int idx, int y)
        {
            int lw = NeedScroll ? 22 : 12;
            Rectangle rc = new Rectangle(6, y, Width - lw, ItemHeight);
            bool chk = checkedIdx.Contains(idx);
            bool hover = hoverIdx == idx;
            using (GraphicsPath p = Ui.Rounded(rc, 10))
            {
                if (chk)
                    using (LinearGradientBrush b = new LinearGradientBrush(rc, Color.FromArgb(108, 140, 245), Color.FromArgb(150, 118, 255), 35f))
                        g.FillPath(b, p);
                else
                    using (LinearGradientBrush b = new LinearGradientBrush(rc, hover ? Color.FromArgb(236, 239, 251) : Color.FromArgb(242, 244, 252), Color.FromArgb(244, 242, 252), 90f))
                        g.FillPath(b, p);
                using (Pen pen = new Pen(chk ? Color.FromArgb(128, 255, 255, 255) : Color.FromArgb(231, 236, 247))) g.DrawPath(pen, p);
            }

            int cbSize = 20;
            int cbX = rc.X + 14;
            int cbY = rc.Y + (ItemHeight - cbSize) / 2;
            Rectangle cb = new Rectangle(cbX, cbY, cbSize, cbSize);
            using (GraphicsPath p = Ui.Rounded(cb, 6))
            {
                using (SolidBrush b = new SolidBrush(chk ? Color.White : Color.FromArgb(250, 251, 253))) g.FillPath(b, p);
                if (chk)
                {
                    using (Pen pen = new Pen(Color.FromArgb(108, 140, 245), 2f))
                    {
                        pen.StartCap = LineCap.Round; pen.EndCap = LineCap.Round;
                        g.DrawLine(pen, cb.X + 4, cb.Y + cb.Height / 2, cb.X + cb.Height / 2, cb.Y + cb.Height - 4);
                        g.DrawLine(pen, cb.X + cb.Height / 2, cb.Y + cb.Height - 4, cb.X + cb.Height - 3, cb.Y + 4);
                    }
                }
                else
                {
                    using (Pen pen = new Pen(Color.FromArgb(196, 202, 224))) g.DrawPath(pen, p);
                }
            }

            Color tc = chk ? Color.White : Color.FromArgb(56, 66, 92);
            Rectangle trc = new Rectangle(cbX + cbSize + 10, rc.Y, rc.Right - (cbX + cbSize + 10), ItemHeight);
            TextRenderer.DrawText(g, Path.GetFileName(Items[idx]), new Font("微软雅黑", 10F), trc, tc, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }

    // 圆角浅色卡片：结果预览与输入区共用
    public class RoundCard : Panel
    {
        public Color Fill = Color.FromArgb(246, 248, 252);
        public Color BorderColor = Color.FromArgb(226, 231, 242);
        public int Radius = 14;
        public Image Pic;
        public string EmptyText;

        public RoundCard()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Color.White;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle rc = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = Ui.Rounded(rc, Radius))
            {
                using (SolidBrush b = new SolidBrush(Fill)) g.FillPath(b, path);
                if (Pic != null)
                {
                    g.SetClip(path);
                    int pad = 14;
                    float sw = (float)(Width - pad * 2) / Pic.Width;
                    float sh = (float)(Height - pad * 2) / Pic.Height;
                    float s = Math.Min(sw, sh);
                    int w = (int)(Pic.Width * s), h = (int)(Pic.Height * s);
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.DrawImage(Pic, (Width - w) / 2, (Height - h) / 2, w, h);
                    g.ResetClip();
                }
                else if (!string.IsNullOrEmpty(EmptyText))
                {
                    TextRenderer.DrawText(g, EmptyText, new Font("微软雅黑", 10.5F), rc,
                        Color.FromArgb(178, 186, 205), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                }
                using (Pen pen = new Pen(BorderColor)) g.DrawPath(pen, path);
            }
        }
    }

    // 参考图缩略行：拖入或点击 + 添加，可横向滚动，可点角标删除，数量不限
    public class RefStrip : Control
    {
        public List<string> Paths = new List<string>();
        List<Bitmap> thumbs = new List<Bitmap>();
        public event EventHandler Changed;
        int T = 72, Gap = 10;
        public float K = 1f;
        int hoverDel = -1, scrollX;
        bool hoverAdd;

        public RefStrip()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            BackColor = Color.FromArgb(246, 248, 252);
            AllowDrop = true;
            DragEnter += (s, e) => { if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy; };
            DragDrop += (s, e) =>
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files == null) return;
                int bad = 0;
                foreach (string f in files) if (!AddFile(f)) bad++;
                if (bad > 0) DesignDialog.ShowWarn("只支持 png / jpg / jpeg / bmp 图片，已跳过 " + bad + " 个不支持的文件。");
            };
        }

        public int Count { get { return Paths.Count; } }

        // 按路径移除（胶囊删掉时同步拿走那张参考图）
        public bool RemovePath(string path)
        {
            int i = Paths.IndexOf(path);
            if (i < 0) return false;
            Paths.RemoveAt(i);
            thumbs[i].Dispose();
            thumbs.RemoveAt(i);
            hoverDel = -1;
            ClampScroll();
            Invalidate();
            if (Changed != null) Changed(this, EventArgs.Empty);
            return true;
        }

        public void Clear()
        {
            Paths.Clear();
            foreach (Bitmap b in thumbs) b.Dispose();
            thumbs.Clear();
            scrollX = 0;
            Invalidate();
            if (Changed != null) Changed(this, EventArgs.Empty);
        }

        Bitmap ThumbOf(string path)
        {
            using (Image src = Image.FromFile(path))
            {
                Bitmap th = new Bitmap(T, T);
                using (Graphics g = Graphics.FromImage(th))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.Clear(Color.FromArgb(238, 241, 248));
                    float s = Math.Max((float)T / src.Width, (float)T / src.Height);
                    int w = (int)(src.Width * s) + 1, h = (int)(src.Height * s) + 1;
                    g.DrawImage(src, (T - w) / 2, (T - h) / 2, w, h);
                }
                return th;
            }
        }

        // 窗口放大/缩小时按比例重排参考图区
        public void Rescale(float f)
        {
            int nt = (int)(72 * f);
            if (nt < 44) nt = 44;
            if (nt == T) return;
            T = nt;
            Gap = (int)(10 * f);
            K = T / 72f;
            List<string> ps = new List<string>(Paths);
            foreach (Bitmap b in thumbs) b.Dispose();
            thumbs.Clear();
            Paths.Clear();
            foreach (string p in ps)
            {
                Bitmap t = null;
                try { t = ThumbOf(p); }
                catch { }
                if (t == null) continue;
                thumbs.Add(t);
                Paths.Add(p);
            }
            scrollX = 0;
            Invalidate();
        }

        public bool AddFile(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            string ext = Path.GetExtension(path).ToLower();
            if (ext != ".png" && ext != ".jpg" && ext != ".jpeg" && ext != ".bmp") return false;
            Bitmap th = null;
            try { th = ThumbOf(path); }
            catch { if (th != null) th.Dispose(); return false; }
            thumbs.Add(th);
            Paths.Add(path);
            Invalidate();
            if (Changed != null) Changed(this, EventArgs.Empty);
            return true;
        }

        // 从剪贴板/内存图像添加：先落成临时 png 再走统一流程（参考图必须是磁盘上的文件）
        public bool AddImage(Image img)
        {
            string p = AddImagePath(img);
            return p != null;
        }

        // 同上，但把落盘后的路径回传（粘贴时要顺手插一颗胶囊，得知道图号对应哪张）
        public string AddImagePath(Image img)
        {
            if (img == null) return null;
            try
            {
                string dir = Path.Combine(Path.GetTempPath(), "ai_refs");
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, "ref_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + ".png");
                using (Bitmap b = new Bitmap(img)) b.Save(path, System.Drawing.Imaging.ImageFormat.Png);
                return AddFile(path) ? path : null;
            }
            catch { return null; }
        }

        int ThumbsW { get { return Paths.Count * (T + Gap); } }
        int ViewX { get { return T + Gap; } }
        int ViewW { get { return Math.Max(0, Width - ViewX); } }
        Rectangle AddBox { get { return new Rectangle(0, 0, T, T); } }

        // 拖动排序：按住缩略图左右拖，松手落到新位置；没移动就是“点一下看大图”
        int pressIdx = -1, dropSlot = -1;
        Point pressPt;
        bool moved;

        // 量出文字墨迹的几何中心（相对绘制原点），用于把数字真正对准圆心
        static PointF InkCenter(Graphics g, string s, Font f, int box)
        {
            PointF fallback = new PointF(box / 2f, box / 2f);
            try
            {
                using (Bitmap b = new Bitmap(box, box, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
                using (Graphics gg = Graphics.FromImage(b))
                {
                    gg.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                    gg.Clear(Color.Transparent);
                    using (StringFormat sf = StringFormat.GenericTypographic)
                        gg.DrawString(s, f, Brushes.White, 0, 0, sf);
                    int x0 = int.MaxValue, y0 = int.MaxValue, x1 = -1, y1 = -1;
                    for (int y = 0; y < box; y++)
                        for (int x = 0; x < box; x++)
                        {
                            Color c = b.GetPixel(x, y);
                            if (c.A > 12)
                            {
                                if (x < x0) x0 = x;
                                if (x > x1) x1 = x;
                                if (y < y0) y0 = y;
                                if (y > y1) y1 = y;
                            }
                        }
                    if (x1 < 0) return fallback;
                    return new PointF((x0 + x1 + 1) / 2f, (y0 + y1 + 1) / 2f);
                }
            }
            catch { return fallback; }
        }

        int HitIndex(Point p)
        {
            if (p.X < ViewX || p.Y > T) return -1;
            int vx = p.X - ViewX + scrollX;
            int i = vx / (T + Gap);
            if (i < 0 || i >= Paths.Count) return -1;
            return i;
        }

        int SlotAt(int x)
        {
            int vx = x - ViewX + scrollX;
            int slot = (vx + (T + Gap) / 2) / (T + Gap);
            if (slot < 0) slot = 0;
            if (slot > Paths.Count) slot = Paths.Count;
            return slot;
        }

        Rectangle DelRect(int i, float k)
        {
            int ds = (int)(22 * k);
            Rectangle rc = new Rectangle(ViewX + i * (T + Gap) - scrollX, 0, T, T);
            return new Rectangle(rc.Right - ds - (int)(2 * k), (int)(2 * k), ds, ds);
        }

        // 点删除时把角标热区再放宽一点：角标只有二十来像素，手指/鼠标稍微歪一点就点空
        bool HitDel(int i, int x, int y)
        {
            Rectangle r = DelRect(i, T / 72f);
            r.Inflate(6, 6);
            return r.Contains(x, y);
        }

        void ClampScroll()
        {
            int max = Math.Max(0, ThumbsW - ViewW);
            if (scrollX > max) scrollX = max;
            if (scrollX < 0) scrollX = 0;
        }

        void PickFiles()
        {
            using (OpenFileDialog d = new OpenFileDialog())
            {
                d.Multiselect = true;
                d.Title = "选择参考图";
                d.Filter = "图片文件|*.png;*.jpg;*.jpeg;*.bmp|所有文件|*.*";
                if (d.ShowDialog(FindForm()) != DialogResult.OK) return;
                int bad = 0;
                foreach (string f in d.FileNames) if (!AddFile(f)) bad++;
                if (bad > 0) DesignDialog.ShowWarn("已跳过 " + bad + " 个不支持的图片文件。");
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;

            // 最左侧固定加号框：永远可点击添加
            float k = T / 72f;
            using (GraphicsPath p = Ui.Rounded(AddBox, (int)(12 * k)))
            {
                using (SolidBrush b = new SolidBrush(Color.FromArgb(250, 251, 254))) g.FillPath(b, p);
                using (Pen pen = new Pen(hoverAdd ? Color.FromArgb(108, 140, 245) : Color.FromArgb(200, 208, 228), 1.4f * k))
                {
                    pen.DashStyle = DashStyle.Dash;
                    pen.DashPattern = new float[] { 4f, 3f };
                    g.DrawPath(pen, p);
                }
            }
            using (Pen pen = new Pen(hoverAdd ? Color.FromArgb(108, 140, 245) : Color.FromArgb(150, 158, 180), 1.8f * k))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                int cx = T / 2, cy = T / 2, half = (int)(9 * k);
                g.DrawLine(pen, cx - half, cy, cx + half, cy);
                g.DrawLine(pen, cx, cy - half, cx, cy + half);
            }

            if (Paths.Count == 0)
            {
                TextRenderer.DrawText(g, "点击 + 、拖入图片，或按 Ctrl+V 粘贴剪贴板图片", new Font("微软雅黑", 9.5F * k), new Rectangle(ViewX, (int)(10 * k), ViewW, (int)(26 * k)),
                    Color.FromArgb(150, 158, 180), TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
                TextRenderer.DrawText(g, "支持 png / jpg / jpeg / bmp；可点开看大图、可按住拖动调整顺序（序号即发给模型的顺序）", new Font("微软雅黑", 8.5F * k), new Rectangle(ViewX, (int)(36 * k), ViewW, (int)(24 * k)),
                    Color.FromArgb(190, 197, 214), TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
                return;
            }

            ClampScroll();
            Rectangle view = new Rectangle(ViewX, 0, ViewW, Height);
            g.SetClip(view);
            Font numFont = new Font("微软雅黑", 9F * k, FontStyle.Bold);
            for (int i = 0; i < thumbs.Count; i++)
            {
                Rectangle rc = new Rectangle(ViewX + i * (T + Gap) - scrollX, 0, T, T);
                if (rc.Right < ViewX || rc.X > Width) continue;
                using (GraphicsPath p = Ui.Rounded(rc, (int)(12 * k)))
                {
                    g.SetClip(p, CombineMode.Intersect);
                    g.DrawImage(thumbs[i], rc);
                    g.SetClip(view);
                    using (Pen pen = new Pen(Color.FromArgb(222, 228, 242))) g.DrawPath(pen, p);
                }
                // 删除角标：浅灰半透明底（不抢画面），悬停变红
                Rectangle del = DelRect(i, k);
                using (SolidBrush b = new SolidBrush(hoverDel == i ? Color.FromArgb(238, 229, 72, 77) : Color.FromArgb(196, 238, 241, 248)))
                    g.FillEllipse(b, del);
                using (Pen pen = new Pen(hoverDel == i ? Color.White : Color.FromArgb(120, 128, 148), 1.5f * k))
                {
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;
                    g.DrawLine(pen, del.X + (int)(5 * k), del.Y + (int)(5 * k), del.Right - (int)(5 * k), del.Bottom - (int)(5 * k));
                    g.DrawLine(pen, del.Right - (int)(5 * k), del.Y + (int)(5 * k), del.X + (int)(5 * k), del.Bottom - (int)(5 * k));
                }
                // 左下角序号：浅灰半透明底 + 数字严格居中；只有整块可见时才画，
                // 否则滚动后文字会盖到固定的加号框上（GDI 文字不受 Graphics 裁剪约束）
                int bs = Math.Max(18, (int)(22 * k));
                Rectangle idx = new Rectangle(rc.X + (int)(4 * k), rc.Bottom - bs - (int)(4 * k), bs, bs);
                if (idx.X >= view.X && idx.Right <= view.Right && idx.Y >= 0 && idx.Bottom <= Height)
                {
                    using (GraphicsPath bi = Ui.Rounded(idx, bs / 2))
                    using (SolidBrush bb = new SolidBrush(Color.FromArgb(228, 234, 237, 245)))
                        g.FillPath(bb, bi);
                    // 数字按「墨迹包围盒」居中：任何字体/字号的字形重心都不同，
                    // 先渲染到临时位图量出真实墨迹中心，再把中心对准圆心，
                    // 这样才算真的居中（不是 em 盒居中，也不是 TextRenderer 的近似）
                    string t = (i + 1).ToString();
                    PointF ink = InkCenter(g, t, numFont, bs);
                    g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                    using (SolidBrush tb = new SolidBrush(Color.FromArgb(52, 60, 84)))
                    using (StringFormat sf = StringFormat.GenericTypographic)
                        g.DrawString(t, numFont, tb, idx.X + bs / 2f - ink.X, idx.Y + bs / 2f - ink.Y, sf);
                    g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.SystemDefault;
                }
            }
            // 拖动排序时显示插入位置
            if (moved && dropSlot >= 0)
            {
                int sx = ViewX + dropSlot * (T + Gap) - scrollX - Gap / 2;
                using (Pen pen = new Pen(Color.FromArgb(112, 140, 245), 2.4f * k))
                using (SolidBrush b = new SolidBrush(Color.FromArgb(112, 140, 245)))
                {
                    g.DrawLine(pen, sx, 2, sx, T - 2);
                    g.FillEllipse(b, sx - (int)(2.4f * k), T / 2 - (int)(2.4f * k), (int)(4.8f * k), (int)(4.8f * k));
                }
            }
            numFont.Dispose();
            g.ResetClip();

            // 横向滚动指示
            int max = Math.Max(0, ThumbsW - ViewW);
            if (max > 0)
            {
                int barW = Math.Max(28, ViewW * ViewW / Math.Max(1, ThumbsW));
                int barX = ViewX + (int)((ViewW - barW) * ((float)scrollX / max));
                using (SolidBrush b = new SolidBrush(Color.FromArgb(205, 214, 232)))
                    g.FillRectangle(b, barX, T + (int)(4 * k), barW, (int)(3 * k));
            }
        }

        protected override void OnResize(EventArgs e) { base.OnResize(e); ClampScroll(); }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            int max = Math.Max(0, ThumbsW - ViewW);
            if (max <= 0) return;
            scrollX -= Math.Sign(e.Delta) * 48;
            ClampScroll();
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            float k = T / 72f;
            if (pressIdx >= 0)
            {
                if (!moved && Math.Abs(e.X - pressPt.X) > 4) moved = true;
                if (moved) { dropSlot = SlotAt(e.X); Cursor = Cursors.Hand; Invalidate(); }
                return;
            }
            bool onAdd = AddBox.Contains(e.Location);
            int over = HitIndex(e.Location);
            int idx = (over >= 0 && HitDel(over, e.Location.X, e.Location.Y)) ? over : -1;
            if (idx != hoverDel || onAdd != hoverAdd)
            {
                hoverDel = idx;
                hoverAdd = onAdd;
                Cursor = (idx >= 0 || onAdd || over >= 0) ? Cursors.Hand : Cursors.Arrow;
                Invalidate();
            }
            else if (over >= 0 && Cursor != Cursors.Hand) Cursor = Cursors.Hand;
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (pressIdx >= 0) return;
            if (hoverDel != -1 || hoverAdd) { hoverDel = -1; hoverAdd = false; Cursor = Cursors.Arrow; Invalidate(); }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;   // 右键什么都不做：不打开大图、不弹菜单
            Focus();
            if (AddBox.Contains(e.Location)) { PickFiles(); return; }
            int i = HitIndex(e.Location);
            if (i < 0) return;
            if (HitDel(i, e.Location.X, e.Location.Y))
            {
                Paths.RemoveAt(i);
                thumbs[i].Dispose();
                thumbs.RemoveAt(i);
                hoverDel = -1;
                ClampScroll();
                Invalidate();
                if (Changed != null) Changed(this, EventArgs.Empty);
                return;
            }
            pressIdx = i;
            pressPt = e.Location;
            moved = false;
            dropSlot = -1;
            Capture = true;
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left) return;
            if (pressIdx < 0) return;
            int from = pressIdx, slot = dropSlot;
            bool wasMoved = moved;
            pressIdx = -1; dropSlot = -1; moved = false;
            Capture = false;
            Cursor = Cursors.Arrow;

            if (wasMoved && slot >= 0)
            {
                int to = slot > from ? slot - 1 : slot;   // 自己被移走后后面的前移一位
                if (to >= 0 && to < Paths.Count && to != from)
                {
                    string p = Paths[from];
                    Paths.RemoveAt(from);
                    Paths.Insert(to, p);
                    Bitmap b = thumbs[from];
                    thumbs.RemoveAt(from);
                    thumbs.Insert(to, b);
                    dropSlot = -1;
                    Invalidate();
                    if (Changed != null) Changed(this, EventArgs.Empty);   // 顺序即发给模型的顺序
                    return;
                }
            }
            else if (from >= 0 && from < Paths.Count)
            {
                ImageViewDialog.Show(Paths[from]);   // 未发送的参考图也能点开看大图
            }
            Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { foreach (Bitmap b in thumbs) b.Dispose(); thumbs.Clear(); }
            base.Dispose(disposing);
        }
    }

    // 生成记录：成功 = 本地图片文件；失败 = 同名 .fail 文本（含真实失败原因）
    public class GenRecord
    {
        public string FilePath;
        public bool Failed;
        public string Reason;
        public string Prompt;
        public string Model;                             // 生成这张图时用的模型（查看大图右侧"模型"一行）
        public string Quality;                           // 生成这张图时选的质量档位（1K/2K/4K）
        public string Size;                              // 失败记录也记住当时选的尺寸，重生成时原样再发
        public string FullPrompt;                        // 实际发出去的完整提示词（含程序补的编号对应说明）
        public DateTime Time;
        public List<string> Refs = new List<string>();   // 生成这张图时用到的参考图（查看大图时在右侧展示）
    }

    // 把接口返回的原始错误（英文 JSON / 报错串）归类成通俗易懂的中文原因
    public static class FailReason
    {
        public struct Info
        {
            public string Title;
            public string Advice;
            public bool Weak;      // 判断依据不够确定（只在兜底时置 true）
            public string Basis;   // 判定依据（连接层状态码 / HTTP 状态码 / 接口字段），弹窗里展示出来
        }

        static bool Has(string s, params string[] keys)
        {
            foreach (string k in keys) if (s.Contains(k)) return true;
            return false;
        }

        // 从接口返回里优先取结构化字段，避免被无关文本带偏
        static void PullJson(string raw, out string code, out string type, out string message)
        {
            code = type = message = "";
            try
            {
                JavaScriptSerializer js = new JavaScriptSerializer();
                js.MaxJsonLength = int.MaxValue;
                object o = js.DeserializeObject(raw);
                Dictionary<string, object> root = o as Dictionary<string, object>;
                if (root == null) return;
                object errObj;
                if (root.ContainsKey("error") && root["error"] is Dictionary<string, object>) errObj = root["error"];
                else errObj = root;
                if (root.ContainsKey("error") && root["error"] is string)
                {
                    message = Convert.ToString(root["error"]);
                    if (root.ContainsKey("status")) code = Convert.ToString(root["status"]).ToLowerInvariant();
                    return;
                }
                Dictionary<string, object> e = errObj as Dictionary<string, object>;
                if (e == null) return;
                if (e.ContainsKey("code") && e["code"] != null) code = Convert.ToString(e["code"]).ToLowerInvariant();
                if (e.ContainsKey("type") && e["type"] != null) type = Convert.ToString(e["type"]).ToLowerInvariant();
                if (e.ContainsKey("message") && e["message"] != null) message = Convert.ToString(e["message"]);
            }
            catch { }
        }

        // 只认真正的 HTTP 状态码写法，避免把 base64 之类文本里的数字当状态码
        static bool StatusIs(string s, params int[] codes)
        {
            foreach (int c in codes)
            {
                if (System.Text.RegularExpressions.Regex.IsMatch(s, "\"(status|status_code|http_status)\"\\s*:\\s*" + c + "\\b")) return true;
                if (System.Text.RegularExpressions.Regex.IsMatch(s, "(^|\\D)" + c + "\\s*(unauthorized|forbidden|too many|bad request|internal|bad gateway|gateway timeout|service unavailable)")) return true;
            }
            return false;
        }

        public static Info Parse(string raw)
        {
            Info i = new Info();
            string body = (raw ?? "").Replace("\r", " ").Replace("\n", " ");
            if (body.Trim().Length == 0)
            {
                i.Title = "没有记录到具体原因";
                i.Advice = "重试一次；仍失败请点右上角「配置 API」检查配置是否完整。";
                i.Weak = true;
                i.Basis = "没有保存到任何返回内容";
                return i;
            }

            string code, type, message;
            PullJson(body, out code, out type, out message);
            string s = body.ToLowerInvariant();
            string m = (code + " " + type + " " + message).ToLowerInvariant();

            // 连接层状态码 / HTTP 状态码（网络失败时程序给的真实结果，格式 “[Status] 详情”）
            string netStatus = "";
            System.Text.RegularExpressions.Match nm = System.Text.RegularExpressions.Regex.Match(body, "^\\s*\\[([^\\]]+)\\]");
            if (nm.Success) netStatus = nm.Groups[1].Value.Trim();
            int httpCode = 0;
            if (netStatus.StartsWith("HTTP "))
            { try { httpCode = int.Parse(netStatus.Substring(5)); } catch { } }

            if (netStatus == "ProtocolError" || httpCode > 0) i.Basis = "HTTP 状态码 / 接口错误字段";
            else if (netStatus.Length > 0) i.Basis = "连接层状态码 " + netStatus;
            else i.Basis = "接口返回文本";
            if (message.Length > 0 && message.Length <= 60) i.Basis = i.Basis + "（message：" + message + "）";

            // 1) 配置缺失
            if (Has(s, "未配置", "没有配置", "api key 为空", "missing api key", "no api key", "key is empty"))
            { i.Title = "还没有配置 API Key"; i.Advice = "点右上角「配置 API」，填入接口地址、Key 和模型名后再生成。"; i.Basis = "本地检查"; return i; }

            // 1.5) 连接层状态码：这是 .NET 给出的真实网络结果，比解析文字可靠得多
            if (netStatus == "SecureChannelFailure" || netStatus == "TrustFailure")
            {
                Info ni = new Info();
                ni.Basis = "连接层状态码 " + netStatus;
                if (netStatus == "TrustFailure")
                {
                    ni.Title = "HTTPS 证书不被信任";
                    ni.Advice = "服务器证书过期、自签名，或被公司防火墙/杀毒软件替换。可换网络（手机热点）试一次；仍失败说明是对方证书问题。";
                }
                else
                {
                    ni.Title = "安全连接（SSL/TLS）建立失败";
                    ni.Advice = "程序已强制使用 TLS1.2，仍失败说明握手被拦在半路：多半是系统证书库过期，或代理/VPN 拦截了 HTTPS。先在浏览器打开该地址确认能通，再把接口域名加入代理直连白名单。";
                }
                return ni;
            }
            if (netStatus == "NameResolutionFailure")
            {
                Info ni = new Info();
                ni.Basis = "连接层状态码 NameResolutionFailure";
                ni.Title = "域名解析失败（找不到服务器）";
                ni.Advice = "接口地址的域名在本机解析不出来。检查地址是否写错、是否需要先连上公司内网/VPN。";
                return ni;
            }
            if (netStatus == "ProxyNameResolutionFailure")
            {
                Info ni = new Info();
                ni.Basis = "连接层状态码 ProxyNameResolutionFailure";
                ni.Title = "代理服务器不可用";
                ni.Advice = "系统里配置的代理地址连不上（代理软件没启动 / 端口不对）。关掉代理软件或改成直连后重试。";
                return ni;
            }
            if (netStatus == "ConnectFailure")
            {
                Info ni = new Info();
                ni.Basis = "连接层状态码 ConnectFailure";
                ni.Title = "连不上服务商（连接被拒绝或中断）";
                ni.Advice = "服务器没有接受连接。确认接口地址、端口正确，或该中转服务当前是否可用。";
                return ni;
            }
            if (netStatus == "Timeout")
            {
                Info ni = new Info();
                ni.Basis = "连接层状态码 Timeout";
                ni.Title = "请求超时，没等到结果";
                ni.Advice = "网络太慢或服务端迟迟不返回；参考图多、尺寸大时耗时会明显变长，可减少参考图后重试。";
                return ni;
            }
            if (netStatus == "KeepAliveFailure" || netStatus == "ConnectionClosed" || netStatus == "ConnectionReset")
            {
                Info ni = new Info();
                ni.Basis = "连接层状态码 " + netStatus;
                ni.Title = "连接被对方中途断开";
                ni.Advice = "多半是代理/防火墙把连接掐了，或服务端主动断开。关掉代理重试一次。";
                return ni;
            }

            // 2) 地址不存在（404/405：多半是地址填错或拼了多余的路径）
            if (httpCode == 404 || httpCode == 405
                || Has(s, "404 page not found", "404 not found", "cannot post", "method not allowed", "no such endpoint"))
            {
                i.Title = "接口地址不对（该路径不存在）";
                i.Advice = "核对「配置 API」里的地址：OpenAI 兼容服务一般填到 /v1 为止，程序会自动接 /images/generations；如果服务商给的是完整地址（如 /v1/api/generate），就填完整地址，不要再带别的路径。";
                i.Basis = httpCode > 0 ? "HTTP " + httpCode : "原始返回含 404/405";
                return i;
            }

            // 3) 鉴权（优先看 HTTP 状态码与结构化错误字段）
            if (Has(m, "invalid_api_key", "invalid api key", "incorrect api key", "authentication_error", "unauthorized", "invalid token", "apikey error", "api key error", "invalid apikey", "invalid key", "key error")
                || httpCode == 401
                || StatusIs(s, 401) || Has(s, "invalid_api_key", "incorrect api key", "key 无效", "密钥无效", "invalid authentication", "apikey error"))
            { i.Title = "API Key 无效或不正确"; i.Advice = "重新复制完整的 Key（注意别带空格、别漏字符），确认填在正确的账号下；换 Key 后再点一次生成。"; i.Basis = httpCode > 0 ? "HTTP " + httpCode + " + 接口错误字段" : "接口错误字段"; return i; }

            if (Has(m, "permission", "forbidden", "not_authorized", "access denied")
                || httpCode == 403 || StatusIs(s, 403) || Has(s, "insufficient_permissions", "无权限", "没有权限"))
            { i.Title = "这个 Key 没有权限访问该接口或模型"; i.Advice = "确认账号已开通该模型权限，或换一个可用账号的 Key。"; return i; }

            // 4) 额度
            if (Has(m, "insufficient_quota", "insufficient", "quota", "billing", "payment_required", "credit_balance")
                || httpCode == 402 || StatusIs(s, 402) || Has(s, "quota", "billing", "余额", "额度", "已用完", "欠费"))
            { i.Title = "账户余额或生成额度已用完"; i.Advice = "去服务商后台充值，或换一个有额度的 Key。"; return i; }

            // 5) 限流
            if (Has(m, "rate_limit", "rate limit", "too many requests", "requests per")
                || httpCode == 429 || StatusIs(s, 429) || Has(s, "频繁"))
            { i.Title = "请求太频繁，被服务商限流"; i.Advice = "等 1 分钟再点生成；参考图较多时适当减少。"; return i; }

            // 5) TLS / 证书（没有拿到连接层状态码时，只能按返回文字判断）
            if (Has(s, "ssl", "tls", "安全通道", "certificate", "证书", "信任关系", "trust relationship", "secure channel"))
            {
                i.Title = "安全连接（SSL/TLS）建立失败";
                i.Advice = "程序已强制使用 TLS1.2，说明握手仍被拦在半路：检查系统日期是否准确、系统证书库是否完整，或把该域名加入代理/VPN 的直连白名单。";
                i.Basis = "原始返回含 SSL/TLS 相关字样";
                return i;
            }

            // 6) 代理
            if (Has(s, "proxy", "代理", "407", "需要代理身份验证"))
            { i.Title = "代理服务器拒绝了连接"; i.Advice = "检查系统代理/科学上网工具设置，或先关闭代理再试。"; return i; }

            // 7) DNS
            if (Has(s, "name or service not known", "nodename nor servname", "no such host", "name resolution", "getaddrinfo", "dns", "无法解析", "解析失败", "域名不存在"))
            { i.Title = "域名解析失败（找不到服务器）"; i.Advice = "确认接口地址没写错（不要漏 http:// 或 https://），并检查网络是否能正常访问该域名。"; return i; }

            // 8) 连不上 / 连接被中断
            if (Has(s, "actively refused", "refused", "connection reset", "connection closed", "broken pipe", "无法连接", "连接被", "对方无响应", "unreachable", "no route", "网络不可达"))
            { i.Title = "连不上服务商（连接被拒绝或中断）"; i.Advice = "检查网络与代理；接口地址若填的是中转服务，确认该服务当前可访问。"; return i; }

            if (Has(s, "network", "网络", "internet", "无法访问", "远程服务器返回错误"))
            { i.Title = "网络连接不通"; i.Advice = "检查网络与代理/VPN 设置，确认填写的接口地址在浏览器里能正常打开。"; return i; }

            // 9) 超时
            if (Has(s, "timeout", "timed out", "time out", "超时", "operation cancel"))
            { i.Title = "请求超时，没等到结果"; i.Advice = "检查网络是否稳定；参考图多或尺寸大时耗时会明显变长，可减少参考图。"; return i; }

            // 10) 服务端故障
            if (Has(m, "server_error", "internal_error", "overloaded", "unavailable", "bad_gateway", "service_unavailable")
                || httpCode >= 500 || StatusIs(s, 500, 502, 503, 504) || Has(s, "internal server", "overload", "服务器繁忙", "服务器过载"))
            { i.Title = "服务商服务器过载或临时故障"; i.Advice = "等 1~2 分钟重试，通常很快恢复；连续失败可换个时段。"; return i; }

            // 11) 内容审核
            if (Has(m, "content_policy", "content policy", "moderation", "safety_system", "sensitive")
                || Has(s, "content_policy", "content policy", "违规", "审核", "sensitive", "blocked", "拒绝生成"))
            { i.Title = "生成内容被审核拦截"; i.Advice = "换一种描述方式，避免人物、暴力、血腥等敏感表述后重试。"; return i; }

            // 12) 模型
            if (Has(m, "model not register", "not register", "model_not_register", "模型未注册", "未开通")
                || Has(s, "model not register", "not register"))
            {
                i.Title = "接口不认识这个模型名（模型名没登记）";
                i.Advice = "这是服务商原话 model not register，意思是「这个模型名在我这台服务器上没有登记」。"
                    + "清单里能看到它，只说明 /models 登记过，不等于生图接口认它：很多中转站按端点分别登记模型，"
                    + "带参考图时程序走 /images/edits，不带参考图走 /images/generations，两条路各自有白名单。"
                    + "先看下方「本次实际请求」里的地址和模型名：①把参考图撤掉再生成一次，若不报错，就是这个模型只在不带图的端点上开通；"
                    + "②点模型框右侧箭头重新拉清单后再选；③仍报错就说明该中转站当前没把这条模型挂在这条接口上，换模型或找服务商确认。";
                return i;
            }
            if (Has(m, "model_not_found", "model not found", "no such model", "invalid model", "does not exist")
                || Has(s, "model_not_found", "model not found", "no such model", "invalid model", "模型不存在"))
            { i.Title = "模型名称不存在或不可用"; i.Advice = "点模型框右侧箭头重新拉清单，从清单里选一个真实存在的模型。"; return i; }

            // 13) 参数（尺寸/数量/格式）
            if (Has(m, "invalid_size", "unsupported_size", "invalid_value", "invalid_request_error") || Has(s, "invalid size", "unsupported size", "尺寸", "分辨率", "invalid value"))
            {
                if (Has(s, "size", "尺寸", "分辨率", "resolution", "width", "height"))
                { i.Title = "图片尺寸不被支持"; i.Advice = "换一个该模型支持的尺寸（如 1024x1024）再试。"; return i; }
                i.Title = "请求参数不被接口接受"; i.Advice = "核对「配置 API」里的地址与模型名是否匹配（OpenAI 兼容地址一般以 /v1 结尾）。"; return i;
            }

            // 14) 图片相关
            if (Has(s, "image", "format", "decode", "格式", "图片", "multipart", "base64", "损坏", "corrupt"))
            {
                if (Has(s, "not a valid image", "unsupported image", "decode", "corrupt", "损坏", "解码"))
                { i.Title = "参考图格式不支持或文件已损坏"; i.Advice = "换成 png / jpg 重新添加；损坏的图片无法作为参考。"; return i; }
                if (Has(s, "base64", "multipart"))
                { i.Title = "接口未能正确接收请求内容"; i.Advice = "接口多半不支持带参考图的上传方式，可先不带参考图生成一次试试。"; return i; }
            }

            // 15) 返回不可解析
            if (Has(s, "unexpected character", "json", "解析", "deserialize", "parse", "invalid json", "<html"))
            { i.Title = "接口返回的数据无法识别"; i.Advice = "确认接口地址正确：OpenAI 兼容地址通常以 /v1 结尾，Gemini 用专用地址；也可能是被网关拦截返回了网页。"; return i; }

            // 16) 兜底：只陈述「没能确定」
            i.Title = "生成失败（未能确定具体原因）";
            i.Advice = "下方原始返回是接口给的原文，可按其中的 message 判断；也可以先重试一次，或核对「配置 API」里的地址、Key、模型名。";
            i.Weak = true;
            i.Basis = "返回内容里没有可识别的状态码或错误字段";
            return i;
        }
    }

    // 历史落盘：<exe 目录>\history\，成功写图片，失败写 .fail
    public static class HistoryStore
    {
        public static string Dir
        {
            get
            {
                string baseDir = Path.GetDirectoryName(Application.ExecutablePath);
                if (string.IsNullOrEmpty(baseDir)) baseDir = Environment.CurrentDirectory;
                return Path.Combine(baseDir, "history");
            }
        }

        static string Stamp() { return DateTime.Now.ToString("yyyyMMdd_HHmmss_fff"); }

        static void Ensure() { try { if (!Directory.Exists(Dir)) Directory.CreateDirectory(Dir); } catch { } }

        public static GenRecord SaveImage(byte[] data, string prompt)
        {
            return SaveImage(data, prompt, null);
        }

        public static GenRecord SaveImage(byte[] data, string prompt, List<string> refs)
        {
            return SaveImage(data, prompt, refs, null);
        }

        public static GenRecord SaveImage(byte[] data, string prompt, List<string> refs, string model)
        {
            return SaveImage(data, prompt, refs, model, null);
        }

        public static GenRecord SaveImage(byte[] data, string prompt, List<string> refs, string model, string quality)
        {
            Ensure();
            string ext = ".png";
            try
            {
                using (MemoryStream ms = new MemoryStream(data))
                using (Image img = Image.FromStream(ms))
                    if (img.RawFormat.Guid == System.Drawing.Imaging.ImageFormat.Jpeg.Guid) ext = ".jpg";
            }
            catch { }
            string stamp = Stamp();
            string path = Path.Combine(Dir, stamp + ext);
            File.WriteAllBytes(path, data);
            try { File.WriteAllText(Path.Combine(Dir, stamp + ".txt"), prompt ?? "", new UTF8Encoding(false)); } catch { }
            GenRecord r = new GenRecord();
            r.FilePath = path;
            r.Failed = false;
            r.Prompt = prompt;
            r.Model = model;
            r.Quality = quality;
            r.Time = DateTime.Now;
            if (!string.IsNullOrEmpty(model))
            {
                try { File.WriteAllText(Path.Combine(Dir, stamp + ".model.txt"), model, new UTF8Encoding(false)); } catch { }
            }
            if (!string.IsNullOrEmpty(quality))
            {
                try { File.WriteAllText(Path.Combine(Dir, stamp + ".quality.txt"), quality, new UTF8Encoding(false)); } catch { }
            }
            if (refs != null && refs.Count > 0)
            {
                r.Refs = new List<string>(refs);
                try { File.WriteAllText(Path.Combine(Dir, stamp + ".refs.txt"), string.Join("\r\n", refs), new UTF8Encoding(false)); } catch { }
            }
            return r;
        }

        public static GenRecord SaveFail(string prompt, string reason)
        {
            return SaveFail(prompt, reason, null);
        }

        public static GenRecord SaveFail(string prompt, string reason, string model)
        {
            return SaveFail(prompt, reason, model, null);
        }

        public static GenRecord SaveFail(string prompt, string reason, string model, string quality)
        {
            return SaveFail(prompt, reason, model, quality, null, null, null);
        }

        // 失败记录也要带上参考图和"实际发出去的那段提示词"：
        // 失败卡里的「重新生成」要能不丢东西地再发一次，重启软件后也必须还能重发。
        public static GenRecord SaveFail(string prompt, string reason, string model, string quality,
                                         List<string> refs, string fullPrompt, string size)
        {
            Ensure();
            string stamp = Stamp();
            string path = Path.Combine(Dir, stamp + ".fail");
            try { File.WriteAllText(path, "prompt: " + (prompt ?? "") + "\r\n---\r\n" + (reason ?? ""), new UTF8Encoding(false)); } catch { }
            if (!string.IsNullOrEmpty(model))
            {
                try { File.WriteAllText(Path.Combine(Dir, stamp + ".model.txt"), model, new UTF8Encoding(false)); } catch { }
            }
            if (!string.IsNullOrEmpty(quality))
            {
                try { File.WriteAllText(Path.Combine(Dir, stamp + ".quality.txt"), quality, new UTF8Encoding(false)); } catch { }
            }
            if (!string.IsNullOrEmpty(size))
            {
                try { File.WriteAllText(Path.Combine(Dir, stamp + ".size.txt"), size, new UTF8Encoding(false)); } catch { }
            }
            if (!string.IsNullOrEmpty(fullPrompt))
            {
                try { File.WriteAllText(Path.Combine(Dir, stamp + ".sent.txt"), fullPrompt, new UTF8Encoding(false)); } catch { }
            }
            GenRecord r = new GenRecord();
            r.FilePath = path;
            r.Failed = true;
            r.Reason = reason;
            r.Prompt = prompt;
            r.Model = model;
            r.Quality = quality;
            r.Size = size;
            r.FullPrompt = fullPrompt;
            r.Time = DateTime.Now;
            if (refs != null && refs.Count > 0)
            {
                r.Refs = new List<string>(refs);
                try { File.WriteAllText(Path.Combine(Dir, stamp + ".refs.txt"), string.Join("\r\n", refs), new UTF8Encoding(false)); } catch { }
            }
            return r;
        }

        // 每条记录的"用了哪个模型"存在同名 .model.txt 里
        static string ReadModel(string stem)
        {
            try
            {
                string p = Path.Combine(Dir, stem + ".model.txt");
                if (File.Exists(p)) return File.ReadAllText(p, Encoding.UTF8).Trim();
            }
            catch { }
            return "";
        }

        // 每条记录的"出图质量（1K/2K/4K）"存在同名 .quality.txt 里
        static string ReadQuality(string stem)
        {
            try
            {
                string p = Path.Combine(Dir, stem + ".quality.txt");
                if (File.Exists(p)) return File.ReadAllText(p, Encoding.UTF8).Trim();
            }
            catch { }
            return "";
        }

        public static List<GenRecord> Load()
        {
            List<GenRecord> list = new List<GenRecord>();
            try
            {
                if (!Directory.Exists(Dir)) return list;
                string[] fs = Directory.GetFiles(Dir);
                List<string> keep = new List<string>();
                foreach (string f in fs)
                {
                    string ext = Path.GetExtension(f).ToLower();
                    if (ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".fail") keep.Add(f);
                }
                keep.Sort((a, b) => string.Compare(Path.GetFileName(b), Path.GetFileName(a), StringComparison.Ordinal));
                foreach (string f in keep)
                {
                    GenRecord r = new GenRecord();
                    r.FilePath = f;
                    r.Time = ParseStamp(Path.GetFileNameWithoutExtension(f), DateTime.Now);
                    r.Model = ReadModel(Path.GetFileNameWithoutExtension(f));
                    r.Quality = ReadQuality(Path.GetFileNameWithoutExtension(f));
                    if (Path.GetExtension(f).ToLower() == ".fail")
                    {
                        r.Failed = true;
                        try
                        {
                            string txt = File.ReadAllText(f, Encoding.UTF8);
                            int idx = txt.IndexOf("---");
                            if (idx >= 0)
                            {
                                r.Prompt = txt.Substring(0, idx).Replace("prompt:", "").Trim();
                                r.Reason = txt.Substring(idx + 3).Trim();
                            }
                            else r.Reason = txt.Trim();
                        }
                        catch { r.Reason = "无法读取失败原因"; }
                        // 失败记录旁边的参考图清单 / 实际发出去的提示词 / 尺寸，供「重新生成」原样重发
                        try
                        {
                            string stem = Path.GetFileNameWithoutExtension(f);
                            string refFile = Path.Combine(Dir, stem + ".refs.txt");
                            if (File.Exists(refFile))
                                foreach (string line in File.ReadAllLines(refFile, Encoding.UTF8))
                                    if (line.Trim().Length > 0) r.Refs.Add(line.Trim());
                            string sentFile = Path.Combine(Dir, stem + ".sent.txt");
                            if (File.Exists(sentFile)) r.FullPrompt = File.ReadAllText(sentFile, Encoding.UTF8);
                            string sizeFile = Path.Combine(Dir, stem + ".size.txt");
                            if (File.Exists(sizeFile)) r.Size = File.ReadAllText(sizeFile, Encoding.UTF8).Trim();
                        }
                        catch { }
                    }
                    else
                    {
                        // 成功记录：提示词存在同名 .txt 里，查看大图时要显示
                        try
                        {
                            string txt = Path.Combine(Dir, Path.GetFileNameWithoutExtension(f) + ".txt");
                            if (File.Exists(txt)) r.Prompt = File.ReadAllText(txt, Encoding.UTF8).Trim();
                            string refFile = Path.Combine(Dir, Path.GetFileNameWithoutExtension(f) + ".refs.txt");
                            if (File.Exists(refFile))
                                foreach (string line in File.ReadAllLines(refFile, Encoding.UTF8))
                                    if (line.Trim().Length > 0) r.Refs.Add(line.Trim());
                        }
                        catch { }
                    }
                    list.Add(r);
                }

                // 图片被删/移走、但同名提示词 .txt 还在：照样留一条记录，
                // 缩略图上显示黄色「图像丢失」，点开能看原因（不是悄悄从历史里消失）
                foreach (string t in fs)
                {
                    if (Path.GetExtension(t).ToLower() != ".txt") continue;
                    string stem = Path.GetFileNameWithoutExtension(t);
                    string low = stem.ToLower();
                    // .refs / .model / .quality 都是挂在图片记录旁边的附属信息，
                    // 不是"只剩提示词"的孤儿记录。以前没排掉它们，每次启动都会给每张图
                    // 凭空多出一张黄色「图像丢失」卡。
                    if (low.EndsWith(".refs") || low.EndsWith(".model") || low.EndsWith(".quality")
                        || low.EndsWith(".sent") || low.EndsWith(".size")) continue;
                    // 失败记录有同名 .fail，已经由上面那张红卡代表了，不要再补一张黄卡
                    if (File.Exists(Path.Combine(Dir, stem + ".fail"))) continue;
                    bool hasImg = false;
                    foreach (string ext in new string[] { ".png", ".jpg", ".jpeg" })
                        if (File.Exists(Path.Combine(Dir, stem + ext))) { hasImg = true; break; }
                    if (hasImg) continue;
                    GenRecord r = new GenRecord();
                    r.FilePath = Path.Combine(Dir, stem + ".png");
                    r.Time = ParseStamp(stem, DateTime.Now);
                    r.Model = ReadModel(stem);
                    r.Quality = ReadQuality(stem);
                    try { r.Prompt = File.ReadAllText(t, Encoding.UTF8).Trim(); } catch { }
                    string refFile2 = Path.Combine(Dir, stem + ".refs.txt");
                    try
                    {
                        if (File.Exists(refFile2))
                            foreach (string line in File.ReadAllLines(refFile2, Encoding.UTF8))
                                if (line.Trim().Length > 0) r.Refs.Add(line.Trim());
                    }
                    catch { }
                    list.Add(r);
                }
            }
            catch { }
            return list;
        }

        // 删除一条记录及其本地文件（图片 / 提示词 / 参考图清单 / 失败文本），彻底不残留
        public static bool Delete(GenRecord r)
        {
            if (r == null || string.IsNullOrEmpty(r.FilePath)) return false;
            bool ok = true;
            try
            {
                if (File.Exists(r.FilePath)) File.Delete(r.FilePath);
                string stamp = Path.GetFileNameWithoutExtension(r.FilePath);
                string[] side = { stamp + ".txt", stamp + ".refs.txt", stamp + ".fail", stamp + ".model.txt", stamp + ".quality.txt", stamp + ".sent.txt", stamp + ".size.txt" };
                foreach (string s in side)
                {
                    string p = Path.Combine(Dir, s);
                    if (File.Exists(p)) File.Delete(p);
                }
            }
            catch { ok = false; }
            return ok;
        }

        static DateTime ParseStamp(string name, DateTime fallback)
        {
            try
            {
                if (name.Length >= 19)
                {
                    int y = int.Parse(name.Substring(0, 4));
                    int mo = int.Parse(name.Substring(4, 2));
                    int d = int.Parse(name.Substring(6, 2));
                    int h = int.Parse(name.Substring(9, 2));
                    int mi = int.Parse(name.Substring(11, 2));
                    int se = int.Parse(name.Substring(13, 2));
                    return new DateTime(y, mo, d, h, mi, se);
                }
            }
            catch { }
            return fallback;
        }
    }

    // 历史网格里的单张缩略图：成功显示圆角方形缩略图，失败显示红色"生成失败"，点击查看真实原因
    public class ThumbItem : Control
    {
        public GenRecord Rec;
        Bitmap thumb;
        bool hover;
        bool missing;              // 记录还在，但本地那张图已经被删/移走了
        bool canCopy;              // 正常图才允许右键「复制图像」
        int Img = 168;
        // 多选模式：左上角出现勾选圈，点击＝选中/取消；普通模式：右上角悬停出现下载按钮
        public bool SelectMode;
        public bool Selected;
        public event EventHandler Toggled;
        public event EventHandler DownloadHit;
        bool hoverDown, suppressClick, downloaded;
        System.Windows.Forms.Timer doneTimer;

        Rectangle DownRect
        {
            get
            {
                int d = Math.Max(20, (int)(26 * Img / 168f));
                return new Rectangle(Img - d - (int)(8 * Img / 168f), (int)(8 * Img / 168f), d, d);
            }
        }

        Rectangle CheckRect
        {
            get
            {
                int d = Math.Max(20, (int)(24 * Img / 168f));
                return new Rectangle((int)(8 * Img / 168f), (int)(8 * Img / 168f), d, d);
            }
        }

        public ThumbItem(GenRecord r) : this(r, 168) { }

        public ThumbItem(GenRecord r, int img)
        {
            Rec = r;
            Img = img;
            Size = new Size(Img, Img + (int)(34 * Img / 168f));
            BackColor = Color.FromArgb(250, 251, 254);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;

            // 图片是不是还在磁盘上，得先判断出来
            if (!r.Failed)
            {
                try { missing = string.IsNullOrEmpty(r.FilePath) || !File.Exists(r.FilePath); } catch { missing = true; }
            }
            // 右键只给一个"复制图像"；失败卡、丢失卡右键什么都不弹，点了也没反应
            canCopy = !r.Failed && !missing;
            if (canCopy) LoadThumb();
        }

        // 从磁盘读缩略图（可能失败：文件损坏/被占）
        void LoadThumb()
        {
            if (thumb != null) { try { thumb.Dispose(); } catch { } thumb = null; }
            try
            {
                if (string.IsNullOrEmpty(Rec.FilePath) || !File.Exists(Rec.FilePath)) return;
                using (FileStream fs = new FileStream(Rec.FilePath, FileMode.Open, FileAccess.Read))
                using (Image src = Image.FromStream(fs))
                {
                    thumb = new Bitmap(Img, Img);
                    using (Graphics g = Graphics.FromImage(thumb))
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        g.Clear(Color.FromArgb(240, 243, 250));
                        float s = Math.Max((float)Img / src.Width, (float)Img / src.Height);
                        int w = (int)(src.Width * s) + 1, h = (int)(src.Height * s) + 1;
                        g.DrawImage(src, (Img - w) / 2, (Img - h) / 2, w, h);
                    }
                }
            }
            catch { thumb = null; }
        }

        // 磁盘上的图可能在我们看不见的时候被删/被移走/又放回来：
        // 每次轮询都重新问一遍 File.Exists，变了就当场换脸（正常图 ⇄ 黄色丢失卡）
        public bool RecheckMissing()
        {
            if (Rec.Failed) return false;
            bool m;
            try { m = string.IsNullOrEmpty(Rec.FilePath) || !File.Exists(Rec.FilePath); }
            catch { m = true; }
            if (m == missing) return false;
            missing = m;
            canCopy = !missing;
            if (missing)
            {
                if (thumb != null) { try { thumb.Dispose(); } catch { } thumb = null; }
                hoverDown = false;
            }
            else
            {
                LoadThumb();
            }
            Invalidate();
            return true;
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            hover = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            hover = false;
            hoverDown = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        CursorAction lastAction = (CursorAction)(-1);
        enum CursorAction { View, Download, Select }

        void SaveAs()        {
            using (SaveFileDialog sd = new SaveFileDialog())
            {
                sd.Title = "保存图片";
                sd.FileName = Path.GetFileName(Rec.FilePath);
                sd.Filter = "PNG 图片|*.png|JPEG 图片|*.jpg|所有文件|*.*";
                sd.InitialDirectory = Path.GetDirectoryName(Rec.FilePath);
                if (sd.ShowDialog(FindForm()) == DialogResult.OK)
                {
                    try { File.Copy(Rec.FilePath, sd.FileName, true); }
                    catch (Exception ex) { DesignDialog.ShowError("保存失败：" + ex.Message); }
                }
            }
        }

        // 本地那张图被删了/挪走了：点缩略图就弹出说明，别当成"图坏了"糊过去
        void ShowMissing()
        {
            string p = Rec.FilePath ?? "";
            string dir = "";
            try { dir = Path.GetDirectoryName(p) ?? ""; } catch { }
            DesignDialog.ShowWarn(
                "这张图已经不在本地了。\n\n" +
                "原因：文件被删除、被移动或改了名字（历史记录还留着，但磁盘上没有这个文件）。\n\n" +
                "原路径：\n" + p +
                (dir.Length > 0 ? "\n\n所在文件夹：\n" + dir + (Directory.Exists(dir) ? "（文件夹还在，可以点「在文件夹中显示」查一下）" : "（文件夹也没了）") : "") +
                "\n\n想恢复只能把原文件放回这个路径；否则这条记录只能删掉。");
        }

        // 点右上角下载按钮：直接落到「下载」文件夹，不弹选择框
        void DownloadNow()
        {
            try
            {
                string dst = ImageViewDialog.DownloadToDefault(Rec.FilePath);
                downloaded = true;
                Invalidate();
                if (doneTimer == null)
                {
                    doneTimer = new System.Windows.Forms.Timer { Interval = 1400 };
                    doneTimer.Tick += (s, e) =>
                    {
                        doneTimer.Stop();
                        downloaded = false;
                        if (!IsDisposed) Invalidate();
                    };
                }
                doneTimer.Stop();
                doneTimer.Start();
                if (DownloadHit != null) DownloadHit(this, EventArgs.Empty);
            }
            catch { }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            // 右键在 MouseDown 就压住点击：MouseUp 之后再压已经晚了，
            // 系统会把这个右键当成一次 Click，结果就是"右键打开了大图"。
            suppressClick = (e.Button == MouseButtons.Right);
            downPt = e.Location; dragging = false;
            if (!SelectMode && !Rec.Failed && hover && DownRect.Contains(e.Location))
            {
                suppressClick = true;
                DownloadNow();
            }
        }

        Point downPt;
        bool dragging;

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            bool onDown = hover && !SelectMode && !Rec.Failed && DownRect.Contains(e.Location);
            bool dirty = false;
            if (onDown != hoverDown) { hoverDown = onDown; dirty = true; }
            CursorAction act = SelectMode ? CursorAction.Select : (onDown ? CursorAction.Download : CursorAction.View);
            if (act != lastAction) { lastAction = act; dirty = true; }
            // 按住左键拖出一段距离：把这张图当成文件拖走，可落到参考图区/提示词框当参考图
            if (!dragging && canCopy && !SelectMode && e.Button == MouseButtons.Left)
            {
                int dx = Math.Abs(e.X - downPt.X), dy = Math.Abs(e.Y - downPt.Y);
                if (dx > 6 || dy > 6)
                {
                    dragging = true;
                    suppressClick = true;
                    try
                    {
                        DataObject d = new DataObject();
                        d.SetData(DataFormats.FileDrop, new string[] { Rec.FilePath });
                        DragDropEffects eff = DoDragDrop(d, DragDropEffects.Copy);
                        // 有些自绘容器收不到 OLE 落点（拖放目标不认），这里按松手时的屏幕坐标兜一手
                        if (eff == DragDropEffects.None && ImagePage.Current != null)
                            ImagePage.Current.DropRefAtScreen(Cursor.Position, Rec.FilePath);
                    }
                    catch { }
                    dragging = false;
                }
            }
            if (dirty) Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Right)
            {
                suppressClick = true;
                // 失败卡、丢失卡右键一律无反应；正常图只弹一条「复制图像」
                if (canCopy)
                {
                    string path = Rec.FilePath;
                    RoundMenu.Show(
                        new string[] { "复制图像", "使用参考图" },
                        Cursor.Position,
                        new Action[]
                        {
                            () => ImageViewDialog.CopyImage(path),
                            () => { if (ImagePage.Current != null) ImagePage.Current.AddRefFile(path); }
                        });
                }
            }
        }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            if (suppressClick) { suppressClick = false; return; }
            if (SelectMode)
            {
                Selected = !Selected;
                Invalidate();
                if (Toggled != null) Toggled(this, EventArgs.Empty);
                return;
            }
            if (Rec.Failed) ErrorDetailDialog.Show(Rec);
            else if (missing) ShowMissing();
            else ImageViewDialog.Show(Rec.FilePath, Rec.Prompt, Rec.Refs);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float k = Img / 168f;
            Rectangle rc = new Rectangle(0, 0, Img - 1, Img - 1);
            using (GraphicsPath p = Ui.Rounded(rc, (int)(12 * k)))
            {
                if (Rec.Failed)
                {
                    using (SolidBrush b = new SolidBrush(hover ? Color.FromArgb(253, 232, 232) : Color.FromArgb(250, 240, 241))) g.FillPath(b, p);
                    using (Pen pen = new Pen(Color.FromArgb(240, 200, 200))) g.DrawPath(pen, p);
                    int cx = Img / 2, cy = Img / 2 - (int)(12 * k);
                    int er = (int)(13 * k);
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(226, 116, 116))) g.FillEllipse(b, cx - er, cy - er, er * 2, er * 2);
                    using (Pen pen = new Pen(Color.White, 2.2f * k))
                    {
                        pen.StartCap = LineCap.Round;
                        pen.EndCap = LineCap.Round;
                        g.DrawLine(pen, cx, cy - (int)(6 * k), cx, cy + (int)(3 * k));
                        g.DrawLine(pen, cx, cy + (int)(7 * k), cx, cy + (int)(8 * k));
                    }
                    TextRenderer.DrawText(g, "生成失败", new Font("微软雅黑", 10F * k, FontStyle.Bold),
                        new Rectangle(0, cy + (int)(22 * k), Img, (int)(24 * k)), Color.FromArgb(210, 90, 90),
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                }
                else if (missing)
                {
                    // 黄色"图像丢失"：文件被删/移走，记录还在
                    using (SolidBrush b = new SolidBrush(hover ? Color.FromArgb(255, 246, 219) : Color.FromArgb(253, 248, 232))) g.FillPath(b, p);
                    using (Pen pen = new Pen(Color.FromArgb(238, 214, 150))) g.DrawPath(pen, p);
                    int cx = Img / 2, cy = Img / 2 - (int)(14 * k);
                    using (GraphicsPath tri = new GraphicsPath())
                    {
                        int r = (int)(16 * k);
                        tri.AddPolygon(new Point[] {
                            new Point(cx, cy - r),
                            new Point(cx + r, cy + r),
                            new Point(cx - r, cy + r) });
                        using (SolidBrush b = new SolidBrush(Color.FromArgb(228, 178, 62))) g.FillPath(b, tri);
                    }
                    using (Pen pen = new Pen(Color.White, 2.2f * k))
                    {
                        pen.StartCap = LineCap.Round; pen.EndCap = LineCap.Round;
                        g.DrawLine(pen, cx, cy - (int)(4 * k), cx, cy + (int)(4 * k));
                        g.DrawLine(pen, cx, cy + (int)(8 * k), cx, cy + (int)(9 * k));
                    }
                    TextRenderer.DrawText(g, "图像丢失", new Font("微软雅黑", 10F * k, FontStyle.Bold),
                        new Rectangle(0, cy + (int)(24 * k), Img, (int)(24 * k)), Color.FromArgb(176, 132, 28),
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                }
                else if (thumb != null)
                {
                    g.SetClip(p);
                    g.DrawImage(thumb, new Rectangle(0, 0, Img, Img));
                    g.ResetClip();
                    using (Pen pen = new Pen(Color.FromArgb(230, 235, 245))) g.DrawPath(pen, p);
                }
                else
                {
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(240, 243, 250))) g.FillPath(b, p);
                    TextRenderer.DrawText(g, "图片已损坏", new Font("微软雅黑", 9.5F * k), rc,
                        Color.FromArgb(170, 177, 195), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                }
            }
            // 选中态：整图描一圈淡紫，右上角下载按钮只在鼠标悬停时出现
            if (SelectMode)
            {
                using (GraphicsPath p = Ui.Rounded(rc, (int)(12 * k)))
                using (Pen pen = new Pen(Selected ? Color.FromArgb(112, 140, 245) : Color.FromArgb(226, 232, 245), Selected ? 2.4f : 1f))
                    g.DrawPath(pen, p);
                Rectangle ck = CheckRect;
                using (GraphicsPath cp = Ui.Rounded(ck, ck.Width / 2))
                {
                    if (Selected)
                    {
                        using (SolidBrush b = new SolidBrush(Color.FromArgb(112, 140, 245))) g.FillPath(b, cp);
                        using (Pen pen = new Pen(Color.White, 2.2f * k))
                        {
                            pen.StartCap = LineCap.Round; pen.EndCap = LineCap.Round;
                            int x0 = ck.X + ck.Width / 2, y0 = ck.Y + ck.Height / 2;
                            g.DrawLine(pen, x0 - (int)(5 * k), y0, x0 - (int)(1 * k), y0 + (int)(4 * k));
                            g.DrawLine(pen, x0 - (int)(1 * k), y0 + (int)(4 * k), x0 + (int)(5 * k), y0 - (int)(4 * k));
                        }
                    }
                    else
                    {
                        using (SolidBrush b = new SolidBrush(Color.FromArgb(190, 240, 242, 248))) g.FillPath(b, cp);
                        using (Pen pen = new Pen(Color.FromArgb(150, 158, 180), 1.4f * k)) g.DrawPath(pen, cp);
                    }
                }
            }
            else if (hover && !Rec.Failed && !missing)
            {
                Rectangle dr = DownRect;
                using (SolidBrush b = new SolidBrush(hoverDown ? Color.FromArgb(232, 108, 140, 245) : Color.FromArgb(204, 240, 242, 248)))
                using (GraphicsPath dp = Ui.Rounded(dr, dr.Width / 2))
                    g.FillPath(b, dp);
                using (Pen pen = new Pen(hoverDown ? Color.White : Color.FromArgb(72, 82, 108), 1.9f * Img / 168f))
                {
                    pen.StartCap = LineCap.Round; pen.EndCap = LineCap.Round;
                    int cx = dr.X + dr.Width / 2, cy = dr.Y + dr.Height / 2;
                    int s = (int)(7 * Img / 168f);
                    if (downloaded)
                    {
                        g.DrawLine(pen, cx - s, cy, cx - (int)(1 * k), cy + s - 2);
                        g.DrawLine(pen, cx - (int)(1 * k), cy + s - 2, cx + s, cy - s + 2);
                    }
                    else
                    {
                        g.DrawLine(pen, cx, cy - s, cx, cy + s);
                        g.DrawLine(pen, cx - (int)(5 * Img / 168f), cy + (int)(2 * Img / 168f), cx, cy + s);
                        g.DrawLine(pen, cx + (int)(5 * Img / 168f), cy + (int)(2 * Img / 168f), cx, cy + s);
                    }
                }
            }
            string timeText = Rec.Time.ToString("MM-dd HH:mm");
            if (Rec.Failed)
                TextRenderer.DrawText(g, timeText + " · 点击查看原因", new Font("微软雅黑", 8.5F * k),
                    new Rectangle(0, Img + (int)(4 * k), Img, (int)(24 * k)), Color.FromArgb(200, 110, 110),
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            else if (missing)
                // 黄色卡的时间行照红色卡的样子来：时间 · 点击查看原因
                TextRenderer.DrawText(g, timeText + " · 点击查看原因", new Font("微软雅黑", 8.5F * k),
                    new Rectangle(0, Img + (int)(4 * k), Img, (int)(24 * k)), Color.FromArgb(196, 150, 60),
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            else
                TextRenderer.DrawText(g, timeText, new Font("微软雅黑", 8.5F * k),
                    new Rectangle(0, Img + (int)(4 * k), Img, (int)(24 * k)), Color.FromArgb(150, 158, 180),
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            Control c = this;
            while (c != null && !(c is HistoryList)) c = c.Parent;
            if (c is HistoryList) { ((HistoryList)c).ScrollBy(e.Delta * 80 / 120); return; }
            base.OnMouseWheel(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (doneTimer != null) { doneTimer.Stop(); doneTimer.Dispose(); doneTimer = null; }
                if (thumb != null) { thumb.Dispose(); thumb = null; }
            }
            base.Dispose(disposing);
        }
    }

    // 生成中的占位卡：会转的进度环 + “正在生成…”，结果回来后原地换成真图 / 失败卡
    public class PendingItem : Control
    {
        System.Windows.Forms.Timer t;
        int ang;
        string promptText;

        public PendingItem(int w, int h, string prompt)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Color.FromArgb(250, 251, 254);
            promptText = prompt ?? "";
            Size = new Size(w, h);
            t = new System.Windows.Forms.Timer();
            t.Interval = 40;
            t.Tick += (s, e) => { ang = (ang + 12) % 360; Invalidate(); };
            t.Start();
        }

        public void ResizeTo(int w, int h)
        {
            if (Width != w || Height != h) Size = new Size(w, h);
        }

        public void StopSpin()
        {
            if (t != null) { t.Stop(); t.Dispose(); t = null; }
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int I = Width, IH = Height;
            float k = I / 168f;
            Rectangle rc = new Rectangle(0, 0, I - 1, I - 1);
            using (GraphicsPath p = Ui.Rounded(rc, (int)(12 * k)))
            {
                using (SolidBrush b = new SolidBrush(Color.FromArgb(246, 248, 253))) g.FillPath(b, p);
                using (Pen pen = new Pen(Color.FromArgb(232, 236, 246))) g.DrawPath(pen, p);

                int cx = I / 2, cy = I / 2 - (int)(12 * k);
                int r = Math.Max(8, (int)(17 * k));
                using (Pen bp = new Pen(Color.FromArgb(226, 232, 245), Math.Max(2f, 3f * k)))
                    g.DrawEllipse(bp, cx - r, cy - r, r * 2, r * 2);
                using (Pen ap = new Pen(Color.FromArgb(108, 140, 245), Math.Max(2f, 3f * k)))
                {
                    ap.StartCap = LineCap.Round;
                    ap.EndCap = LineCap.Round;
                    g.DrawArc(ap, cx - r, cy - r, r * 2, r * 2, ang, 100);
                }

                using (Font f = new Font("微软雅黑", 9.5F * k))
                    TextRenderer.DrawText(g, "正在生成…", f,
                        new Rectangle(0, cy + r + (int)(12 * k), I, (int)(24 * k)),
                        Color.FromArgb(96, 108, 138),
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }

            string one = promptText.Replace("\r", " ").Replace("\n", " ").Trim();
            if (one.Length > 12) one = one.Substring(0, 12) + "…";
            using (Font f = new Font("微软雅黑", 8.5F * k))
                TextRenderer.DrawText(g, one.Length > 0 ? one : "任务已提交", f,
                    new Rectangle(0, I + (int)(4 * k), I, (int)(24 * k)),
                    Color.FromArgb(150, 158, 180),
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            Control c = this;
            while (c != null && !(c is HistoryList)) c = c.Parent;
            if (c is HistoryList) { ((HistoryList)c).ScrollBy(e.Delta * 80 / 120); return; }
            base.OnMouseWheel(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) StopSpin();
            base.Dispose(disposing);
        }
    }

    // 内嵌容器：双缓冲，滚动时只移动它，避免逐个控件重定位闪烁
    public class DbPanel : Panel
    {
        public DbPanel()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Color.FromArgb(250, 251, 254);
        }
    }

    // 历史网格容器：只上下滚动，滚动条自绘（与整体风格统一），永远不出现横向滚动条
    public class HistoryList : Panel
    {
        List<ThumbItem> items = new List<ThumbItem>();
        List<PendingItem> pendings = new List<PendingItem>();   // 正在生成中的占位卡片
        List<Control> slots = new List<Control>();              // 网格真实顺序：占位卡和成品卡各自保留自己的位置
        DbPanel inner;
        Label empty;
        int ItemW = 168, ItemH = 202, GapX = 14, GapY = 14, Pad = 4;
        public float K = 1f;
        const int BarW = 5, BarPad = 5;
        int scrollY, maxScroll, needH;
        bool drag; int dragY, dragScroll;

        public int PendingCount { get { return pendings.Count; } }
        int TileCount { get { return slots.Count; } }
        Control TileAt(int i) { return (Control)slots[i]; }

        // 多选：批量下载 / 批量删除（删除会同时删掉本地文件）
        public bool SelectMode { get; set; }
        public event EventHandler SelectionChanged;

        List<ThumbItem> Sel { get { List<ThumbItem> l = new List<ThumbItem>(); foreach (ThumbItem it in items) if (it.Selected) l.Add(it); return l; } }
        public int SelectedCount { get { return Sel.Count; } }
        public List<GenRecord> SelectedRecords { get { List<GenRecord> l = new List<GenRecord>(); foreach (ThumbItem it in Sel) l.Add(it.Rec); return l; } }

        void Hook(ThumbItem it)
        {
            it.SelectMode = SelectMode;
            it.Toggled += (s, e) => { if (SelectionChanged != null) SelectionChanged(this, EventArgs.Empty); };
        }

        public void SetSelectMode(bool on)
        {
            SelectMode = on;
            foreach (ThumbItem it in items) { it.SelectMode = on; if (!on) it.Selected = false; it.Invalidate(); }
            if (SelectionChanged != null) SelectionChanged(this, EventArgs.Empty);
        }

        // 删除选中项：先抹掉本地文件，再从网格里摘掉
        public int DeleteSelected()
        {
            List<ThumbItem> sel = Sel;
            if (sel.Count == 0) return 0;
            List<GenRecord> keep = new List<GenRecord>();
            foreach (ThumbItem it in items)
            {
                if (it.Selected) HistoryStore.Delete(it.Rec);
                else keep.Add(it.Rec);
            }
            SetItems(keep);
            if (SelectionChanged != null) SelectionChanged(this, EventArgs.Empty);
            return sel.Count;
        }

        public HistoryList()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.Selectable, true);
            BackColor = Color.FromArgb(250, 251, 254);
            inner = new DbPanel();
            inner.Location = new Point(0, 0);
            Controls.Add(inner);
            // 在资源管理器里删/移/放回文件，程序收不到任何通知：只能自己隔一会儿问磁盘一次，
            // 变了就把那张缩略图当场换成黄色「图像丢失」卡（或换回来）
            watch = new System.Windows.Forms.Timer { Interval = 1200 };
            watch.Tick += (s, e) => RecheckAll();
            watch.Start();
        }

        System.Windows.Forms.Timer watch;

        public void RecheckAll()
        {
            if (IsDisposed) return;
            bool any = false;
            foreach (ThumbItem it in items)
            {
                try { if (it.RecheckMissing()) any = true; }
                catch { }
            }
            if (any) Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && watch != null) { watch.Stop(); watch.Dispose(); watch = null; }
            base.Dispose(disposing);
        }

        public void SetItems(List<GenRecord> list)
        {
            SuspendLayout();
            foreach (ThumbItem it in items) { inner.Controls.Remove(it); it.Dispose(); }
            items.Clear();
            slots.Clear();
            if (empty != null) { Controls.Remove(empty); empty.Dispose(); empty = null; }
            foreach (GenRecord r in list)
            {
                ThumbItem it = new ThumbItem(r, ItemW);
                Hook(it);
                items.Add(it);
                slots.Add(it);
                inner.Controls.Add(it);
            }
            for (int i = pendings.Count - 1; i >= 0; i--) slots.Insert(0, pendings[i]);
            foreach (PendingItem p in pendings) { p.ResizeTo(ItemW, ItemH); }
            scrollY = 0;
            if (slots.Count == 0)
            {
                empty = new Label();
                empty.Text = "还没有生成记录，输入提示词点右下角“生成图像”试试";
                empty.Font = new Font("微软雅黑", 10F * K);
                empty.ForeColor = Color.FromArgb(178, 186, 205);
                empty.BackColor = Color.FromArgb(250, 251, 254);
                empty.TextAlign = ContentAlignment.MiddleCenter;
                empty.Size = new Size(Math.Max(200, Width - 20), (int)(40 * K));
                empty.Location = new Point(10, Math.Max(20, (Height - empty.Height) / 2));
                Controls.Add(empty);
            }
            LayoutItems();
            ResumeLayout(true);
            Invalidate();
        }

        // 生成开始：在网格最前面插入一张会转圈的占位卡，等结果回来原地换成真图/失败卡
        public PendingItem AddPending(string prompt)
        {
            if (empty != null) { Controls.Remove(empty); empty.Dispose(); empty = null; }
            PendingItem p = new PendingItem(ItemW, ItemH, prompt);
            pendings.Insert(0, p);
            slots.Insert(0, p);
            inner.Controls.Add(p);
            scrollY = 0;
            LayoutItems();
            inner.Location = new Point(0, -scrollY);
            Invalidate();
            return p;
        }

        // 生成结束：占位卡摘掉，成品卡放回它原来占的那一格（失败也一样，不许被别人挤走）
        public void CompletePending(PendingItem p, GenRecord rec)
        {
            int at = p == null ? 0 : slots.IndexOf(p);
            if (at < 0) at = 0;
            if (p != null)
            {
                pendings.Remove(p);
                slots.Remove(p);
                inner.Controls.Remove(p);
                p.StopSpin();
                p.Dispose();
            }
            ThumbItem it = new ThumbItem(rec, ItemW);
            Hook(it);
            items.Insert(0, it);
            slots.Insert(Math.Min(at, slots.Count), it);
            inner.Controls.Add(it);
            LayoutItems();
            Invalidate();
        }

        // 窗口放大/缩小时按比例重排网格与缩略图
        public void Rescale(float f)
        {
            if (f < 0.5f) f = 0.5f;
            int nw = (int)(168 * f);
            if (nw < 96) nw = 96;
            if (nw == ItemW) return;
            K = nw / 168f;
            ItemW = nw;
            ItemH = (int)(202 * K);
            GapX = (int)(14 * K);
            GapY = (int)(14 * K);
            Pad = (int)(4 * K);
            List<GenRecord> recs = new List<GenRecord>();
            foreach (ThumbItem it in items) recs.Add(it.Rec);
            SetItems(recs);
        }

        public void InsertFirst(GenRecord r)
        {
            List<GenRecord> all = new List<GenRecord>();
            all.Add(r);
            foreach (ThumbItem it in items) all.Add(it.Rec);
            scrollY = 0;                       // 新记录置顶，回到顶部
            SetItems(all);
        }

        public void ScrollBy(int delta)
        {
            if (maxScroll <= 0) return;
            scrollY -= delta;
            if (scrollY < 0) scrollY = 0;
            if (scrollY > maxScroll) scrollY = maxScroll;
            inner.Location = new Point(0, -scrollY);
            Invalidate();
        }

        Rectangle ThumbRect()
        {
            int trackH = Height - BarPad * 2;
            int th = (int)((long)trackH * Height / Math.Max(1, needH));
            if (th < 40) th = 40;
            if (th > trackH) th = trackH;
            int top = BarPad;
            if (maxScroll > 0) top += (int)((long)(trackH - th) * scrollY / maxScroll);
            return new Rectangle(Width - BarPad - BarW, top, BarW, th);
        }

        public static GraphicsPath RR(Rectangle r, int rad)
        {
            GraphicsPath p = new GraphicsPath();
            int d = rad * 2;
            if (d > r.Width) d = r.Width;
            if (d > r.Height) d = r.Height;
            if (d < 2) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        void LayoutItems()
        {
            int n = TileCount;
            if (n == 0)
            {
                scrollY = 0; maxScroll = 0; needH = 0;
                inner.Size = new Size(Width, Height);
                inner.Location = new Point(0, 0);
                if (empty != null)
                {
                    empty.Size = new Size(Math.Max(200, Width - 20), (int)(40 * K));
                    empty.Location = new Point(10, Math.Max(20, (Height - empty.Height) / 2));
                }
                Invalidate();
                return;
            }
            // 缩略图尺寸与间距恒定，只按可用宽度算列数，并让每行左右空隙相等
            int barSpace = 0;
            int avail = Width - Pad * 2;
            int cols = Math.Max(1, (avail + GapX) / (ItemW + GapX));
            int rows = (n + cols - 1) / cols;
            int need = Pad * 2 + rows * ItemH + (rows - 1) * GapY;
            if (need > Height)                          // 需要竖向滚动条
            {
                barSpace = BarW + BarPad * 2 + 2;
                avail = Math.Max(ItemW, Width - Pad * 2 - barSpace);
                cols = Math.Max(1, (avail + GapX) / (ItemW + GapX));
                rows = (n + cols - 1) / cols;
                need = Pad * 2 + rows * ItemH + (rows - 1) * GapY;
            }
            needH = need;
            maxScroll = Math.Max(0, need - Height);
            if (scrollY > maxScroll) scrollY = maxScroll;
            if (scrollY < 0) scrollY = 0;

            inner.SuspendLayout();
            inner.Size = new Size(Width - barSpace, Math.Max(need, Height));
            inner.Location = new Point(0, -scrollY);
            int contentW = inner.Width - Pad * 2;
            // 每行按该行实际数量单独居中，避免末行不满时右侧空出一大块
            for (int r = 0; r < rows; r++)
            {
                int cnt = Math.Min(cols, n - r * cols);
                int rowW = cnt * ItemW + (cnt - 1) * GapX;
                int rowLeft = Pad + Math.Max(0, (contentW - rowW) / 2);
                for (int c = 0; c < cnt; c++)
                {
                    Control t = TileAt(r * cols + c);
                    if (t.Width != ItemW || t.Height != ItemH) t.Size = new Size(ItemW, ItemH);
                    t.Location = new Point(rowLeft + c * (ItemW + GapX), Pad + r * (ItemH + GapY));
                }
            }
            inner.ResumeLayout(true);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (maxScroll <= 0) return;
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle track = new Rectangle(Width - BarPad - BarW, BarPad, BarW, Height - BarPad * 2);
            using (GraphicsPath p = RR(track, BarW / 2))
            using (SolidBrush b = new SolidBrush(Color.FromArgb(233, 236, 245)))
                g.FillPath(b, p);
            Rectangle th = ThumbRect();
            using (GraphicsPath p = RR(th, BarW / 2))
            using (SolidBrush b = new SolidBrush(drag ? Color.FromArgb(130, 108, 140, 245) : Color.FromArgb(172, 122, 142, 245)))
                g.FillPath(b, p);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            ScrollBy(e.Delta * 80 / 120);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            if (maxScroll <= 0) return;
            if (e.X < Width - (BarW + BarPad * 2)) return;
            Rectangle th = ThumbRect();
            if (e.Y >= th.Top && e.Y <= th.Bottom)
            {
                drag = true; dragY = e.Y; dragScroll = scrollY; Capture = true;
            }
            else
            {
                int trackH = Height - BarPad * 2 - th.Height;
                if (trackH <= 0) return;
                int rel = e.Y - BarPad - th.Height / 2;
                scrollY = (int)((long)rel * maxScroll / trackH);
                if (scrollY < 0) scrollY = 0;
                if (scrollY > maxScroll) scrollY = maxScroll;
                inner.Location = new Point(0, -scrollY);
                Invalidate();
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!drag) return;
            Rectangle th = ThumbRect();
            int trackH = Height - BarPad * 2 - th.Height;
            if (trackH <= 0) return;
            scrollY = dragScroll + (int)((long)(e.Y - dragY) * maxScroll / trackH);
            if (scrollY < 0) scrollY = 0;
            if (scrollY > maxScroll) scrollY = maxScroll;
            inner.Location = new Point(0, -scrollY);
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            drag = false; Capture = false;
        }

        protected override void OnResize(EventArgs e) { base.OnResize(e); LayoutItems(); }
    }

    // 提示词输入：去掉原生滚动条，换成与整体一致的细滚动条（自绘，仅竖向）
    public class PromptEdit : Panel
    {
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr wp, IntPtr lp);
        const int EM_LINESCROLL = 0x00B6, WM_VSCROLL = 0x0115, WM_MOUSEWHEEL = 0x020A;
        const int BarW = 5, BarPad = 4;

        public TextBox Editor;
        int firstLine, totalLines, visLines, maxLines, lineH = 20;
        bool drag; int dragY, dragFirst;

        // ---- @ 引用参考图：文本框里插 "@图N" 记号，外面盖一颗胶囊按钮 ----
        public Dictionary<int, string> ChipMap = new Dictionary<int, string>();   // 图号 → 参考图路径
        public Func<List<string>> ChipSource;                                     // 取当前参考图列表（给 @ 弹窗用）
        public Action<string> ChipClicked;                                        // 点胶囊看大图
        public Action<string> ChipDeleted;                                        // 胶囊被删 → 那张参考图跟着移除（已停用：删胶囊不再删图）
        public Func<Image, string> PasteImage;                                    // 剪切板图片 → 落成参考图，返回路径
        public Action EnterSend;                                                   // 回车直接发送（由生成页挂上 OnGenerate）
        List<ChipPill> pills = new List<ChipPill>();

        // 每颗胶囊真实占用的矩形（面板坐标）与它在文本里的起止下标：
        // 光标只要落进这个矩形就说明"被胶囊压住了"，必须吸到左右边界上去。
        class ChipHit { public int start; public int rightIdx; public Rectangle rect; }
        List<ChipHit> hits = new List<ChipHit>();
        int hitUsed;

        // 剪切板里有图片就收进来：加进参考图区并在光标处插一颗胶囊。
        // 返回 true 表示这次粘贴已经被我们吃掉，不要再走 TextBox 的默认粘贴。
        public bool TryPasteFromClipboard()
        {
            try
            {
                if (PasteImage == null) return false;
                if (Clipboard.ContainsImage())
                {
                    using (Image img = Clipboard.GetImage())
                    {
                        if (img == null) return false;
                        string path = PasteImage(img);
                        if (path == null) return false;
                        // 只把图收进上方的参考图区，提示词框里一个字都不插：
                        // 用户只要这张图当参考，没打算在文本里 @ 它
                        return true;
                    }
                }
                // 剪切板是一批图片文件（在资源管理器里 Ctrl+C 的图）也一并当参考图收
                if (Clipboard.ContainsFileDropList())
                {
                    bool any = false;
                    foreach (string f in Clipboard.GetFileDropList())
                    {
                        string ext = Path.GetExtension(f ?? "").ToLowerInvariant();
                        if (ext != ".png" && ext != ".jpg" && ext != ".jpeg" && ext != ".bmp") continue;
                        string path;
                        try
                        {
                            using (Image img = Image.FromFile(f)) path = PasteImage(img);
                        }
                        catch { continue; }
                        if (path != null) { any = true; }
                    }
                    return any;
                }
            }
            catch { }
            return false;
        }

        void AddChipFor(string path)
        {
            if (ChipSource == null) return;
            List<string> cur = ChipSource();
            if (cur == null) return;
            int no = cur.IndexOf(path) + 1;
            if (no > 0) InsertChip(no, path);
        }

        class HookBox : TextBox
        {
            PromptEdit host;
            public HookBox(PromptEdit h) { host = h; }
            const int WM_PASTE = 0x0302;
            protected override void WndProc(ref Message m)
            {
                // 剪切板里是图片就当成一张参考图收进来（顺带插一颗胶囊）；
                // 是文字/文件路径就走 TextBox 自己的粘贴，行为不变。
                if (m.Msg == WM_PASTE && host.TryPasteFromClipboard())
                {
                    m.Result = IntPtr.Zero;
                    return;
                }
                // 只读/无滚动条的多行 TextBox 自己不理滚轮，这里统一接管，避免"只能拖条不能滚"
                if (m.Msg == WM_MOUSEWHEEL)
                {
                    int delta = (int)(((long)m.WParam >> 16) & 0xFFFF);
                    if (delta > 32767) delta -= 65536;
                    host.WheelBy(delta);
                    m.Result = IntPtr.Zero;
                    return;
                }
                base.WndProc(ref m);
                if (m.Msg == WM_VSCROLL) host.SyncScroll();
            }
        }

        public PromptEdit()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Color.White;
            Editor = new HookBox(this);
            Editor.Multiline = true;
            Editor.BorderStyle = BorderStyle.None;
            Editor.ScrollBars = ScrollBars.None;
            Editor.WordWrap = true;
            Editor.BackColor = Color.White;
            Editor.Location = new Point(6, 4);
            FitEditor();
            Editor.TextChanged += (s, e) => { RepairChips(); SyncScroll(); LayoutChips(); };
            Editor.KeyUp += (s, e) => { SnapCaret(); SyncScroll(); LayoutChips(); };
            Editor.KeyDown += EditorKeyDown;
            Editor.KeyPress += EditorKeyPress;
            Editor.MouseUp += (s, e) => { SnapCaret(); SyncScroll(); };
            Editor.Resize += (s, e) => SyncScroll();
            Editor.FontChanged += (s, e) => { FitEditor(); SyncScroll(); };
            Controls.Add(Editor);
            SyncScroll();
        }

        // 让文本框高度正好是整数行：否则窗口/卡片一改高度，最下面一行就被裁掉一半
        // 注意 Font.Height 并不等于 TextBox 实际行距（雅黑等字体行距更大），
        // 必须用 GetPositionFromCharIndex 量出真实行距，否则仍会裁掉最后一行。
        int LinePitch()
        {
            int lh = Editor.Font.Height + 1;   // TextBox 行距比 Font.Height 略大，+1 防止裁掉末行
            try
            {
                int i0 = Editor.GetFirstCharIndexFromLine(0);
                int i1 = Editor.GetFirstCharIndexFromLine(1);
                if (i0 >= 0 && i1 >= 0)
                {
                    int y0 = Editor.GetPositionFromCharIndex(i0).Y;
                    int y1 = Editor.GetPositionFromCharIndex(i1).Y;
                    int d = Math.Abs(y1 - y0);
                    if (d >= lh && d <= lh + 12) lh = d;
                }
            }
            catch { }
            if (lh < 10) lh = 10;
            return lh;
        }

        void FitEditor()
        {
            if (Editor == null) return;
            lineH = LinePitch();
            int n = Math.Max(1, (Height - 8) / lineH);
            Editor.Size = new Size(Math.Max(20, Width - 17), n * lineH + 4);
            Editor.Location = new Point(6, 4);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            FitEditor();
            SyncScroll();
        }

        public void SyncScroll()
        {
            if (Editor == null) return;
            try
            {
                totalLines = Editor.TextLength == 0 ? 1 : Editor.GetLineFromCharIndex(Editor.TextLength - 1) + 1;
                firstLine = Editor.GetLineFromCharIndex(Editor.GetCharIndexFromPosition(new Point(2, 2)));
                visLines = Math.Max(1, Editor.ClientSize.Height / Math.Max(1, lineH));
                maxLines = Math.Max(0, totalLines - visLines);
                if (firstLine < 0) firstLine = 0;
            }
            catch { }
            LayoutChips();
            Invalidate();
        }

        Rectangle ThumbRect()
        {
            int trackH = Math.Max(1, Height - BarPad * 2);
            int th = (int)((long)trackH * visLines / Math.Max(1, totalLines));
            if (th < 34) th = 34;
            if (th > trackH) th = trackH;
            int top = BarPad;
            if (maxLines > 0) top += (int)((long)(trackH - th) * Math.Min(firstLine, maxLines) / maxLines);
            return new Rectangle(Width - BarPad - BarW, top, BarW, th);
        }

        void ScrollToLine(int line)
        {
            if (line < 0) line = 0;
            if (line > maxLines) line = maxLines;
            int delta = line - firstLine;
            if (delta != 0) SendMessage(Editor.Handle, EM_LINESCROLL, IntPtr.Zero, (IntPtr)delta);
            SyncScroll();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (maxLines <= 0) return;
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle track = new Rectangle(Width - BarPad - BarW, BarPad, BarW, Height - BarPad * 2);
            using (GraphicsPath p = HistoryList.RR(track, BarW / 2))
            using (SolidBrush b = new SolidBrush(Color.FromArgb(233, 236, 245)))
                g.FillPath(b, p);
            Rectangle th = ThumbRect();
            using (GraphicsPath p = HistoryList.RR(th, BarW / 2))
            using (SolidBrush b = new SolidBrush(drag ? Color.FromArgb(130, 108, 140, 245) : Color.FromArgb(172, 122, 142, 245)))
                g.FillPath(b, p);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (maxLines <= 0) return;
            if (e.X < Width - (BarW + BarPad * 2)) { Editor.Focus(); return; }
            Rectangle th = ThumbRect();
            if (e.Y >= th.Top && e.Y <= th.Bottom)
            {
                drag = true; dragY = e.Y; dragFirst = firstLine; Capture = true;
            }
            else
            {
                int trackH = Height - BarPad * 2 - th.Height;
                if (trackH <= 0) return;
                ScrollToLine((int)((long)(e.Y - BarPad - th.Height / 2) * maxLines / trackH));
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!drag) return;
            Rectangle th = ThumbRect();
            int trackH = Height - BarPad * 2 - th.Height;
            if (trackH <= 0) return;
            ScrollToLine(dragFirst + (int)((long)(e.Y - dragY) * maxLines / trackH));
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            drag = false; Capture = false;
        }

        public void WheelBy(int delta)
        {
            int notches = delta / 120;
            if (notches == 0) notches = delta > 0 ? 1 : -1;
            ScrollToLine(firstLine - notches * 3);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            WheelBy(e.Delta);
        }

        // 光标像素落在某颗胶囊盖住的矩形里时，返回"胶囊右外侧"的下标；不在胶囊里返回 -1
        int ChipRightAt(int c, string txt)
        {
            if (Editor == null) return -1;
            try
            {
                Point pc = Editor.GetPositionFromCharIndex(Math.Min(c, txt.Length));
                for (int k = 0; k < hitUsed && k < hits.Count; k++)
                {
                    ChipHit hit = hits[k];
                    if (pc.Y < hit.rect.Top - 3 || pc.Y > hit.rect.Bottom + 3) continue;
                    if (pc.X < hit.rect.Left || pc.X > hit.rect.Right) continue;
                    return Math.Min(hit.rightIdx, txt.Length);
                }
            }
            catch { }
            return -1;
        }

        // ================= @ 引用参考图 =================
        void EditorKeyPress(object s, KeyPressEventArgs e)
        {
            // 胶囊是整颗的：光标若落在胶囊盖住的那片区域里，敲字先跳到胶囊右外侧，
            // 绝不允许把字打进胶囊内部（打进去就会被胶囊盖住，看着像吞字）
            int st, en, no;
            if (e.KeyChar >= ' ')
            {
                int rx = ChipRightAt(Editor.SelectionStart, Editor.Text);
                if (rx >= 0 && rx != Editor.SelectionStart)
                {
                    Editor.SelectionStart = rx;
                    LayoutChips();
                    e.Handled = true;
                    return;
                }
            }
            if (e.KeyChar >= ' ' && TokenAround(Editor.SelectionStart, Editor.Text, out st, out en, out no)
                && Editor.SelectionStart > st && Editor.SelectionStart < en)
            {
                Editor.SelectionStart = en;
                e.Handled = true;
                return;
            }
            // 先把 @ 落进文本，再弹选择框（否则弹出时文本还没这个字符，回删会错位）
            if (e.KeyChar == '@') BeginInvoke(new Action(ShowRefMenu));
        }

        void ShowRefMenu()
        {
            if (ChipSource == null) return;
            List<string> list = ChipSource();
            if (list == null || list.Count == 0) return;
            Point caret = Editor.GetPositionFromCharIndex(Editor.SelectionStart);
            Point scr = Editor.PointToScreen(new Point(caret.X, caret.Y + lineH + 2));
            RefPopup pop = new RefPopup(list, scr, Editor.Font);
            string picked;
            if (pop.ShowDialog(FindForm()) == DialogResult.OK)
            {
                picked = pop.PickedPath;
                if (picked != null)
                {
                    int no = list.IndexOf(picked) + 1;
                    if (no > 0) InsertChip(no, picked);
                }
            }
            else if (Editor.Text.EndsWith("@") && Editor.SelectionStart == Editor.TextLength)
            {
                // 取消就顺手把多余的 @ 收回去，别留个孤零零的符号
                Editor.Text = Editor.Text.Substring(0, Editor.TextLength - 1);
                Editor.SelectionStart = Editor.TextLength;
            }
            Editor.Focus();
        }

        // 把刚敲的 @ 换成"图N"记号，并记住 图号→路径
        void InsertChip(int no, string path)
        {
            string txt = Editor.Text;
            int caret = Editor.SelectionStart;
            string pre = txt.Substring(0, caret);
            if (pre.EndsWith("@")) pre = pre.Substring(0, pre.Length - 1);
            // 胶囊前面永远垫一个空格当作"真空档"：胶囊本体从"@"的位置开始画，
            // 光标停在胶囊左边时正好落在这一格里，不会被胶囊盖住（这也顺带把"胶囊左侧小黑点"消掉）
            string lead = (pre.Length == 0 || pre.EndsWith(" ")) ? "" : " ";
            string token = lead + "@图" + no + Tail(no);
            string post = txt.Substring(caret);
            ChipMap[no] = path;
            Editor.Text = pre + token + post;
            Editor.SelectionStart = pre.Length + token.Length;
            Editor.Focus();
            LayoutChips();
        }

        // 记号后面留四个空格：胶囊画得比字形胖，右缘比"@图N"还多探出几像素。
        // 四个空格（中文输入法下约 20px）扣掉探出部分后，后文/光标离胶囊边还有 6~10px，
        // 既不可能被胶囊盖住，也不至于远到像两段不相干的文字。
        string Tail(int no)
        {
            return "    ";
        }

        // 胶囊是一颗整的：退格/Delete 一律整颗删；左右方向键一次就跨过整颗，光标绝不落在胶囊中间
        void EditorKeyDown(object s, KeyEventArgs e)
        {
            // 回车直接生成；Shift+Enter 保留换行。输入法组合中的回车（VK_PROCESSKEY）
            // 交给输入法自己上屏，不能被当成发送。
            if (e.KeyCode == Keys.ProcessKey || e.KeyValue == 229) return;
            if (e.KeyCode == Keys.Enter)
            {
                if ((Control.ModifierKeys & Keys.Shift) == Keys.Shift) return;
                e.Handled = true;
                e.SuppressKeyPress = true;
                if (EnterSend != null) EnterSend();
                return;
            }
            if (Editor.SelectionLength != 0) return;
            if (e.KeyCode == Keys.Left || e.KeyCode == Keys.Right)
            {
                string t0 = Editor.Text;
                int c0 = Editor.SelectionStart;
                int s0, e0, n0;
                if (TokenAround(c0, t0, out s0, out e0, out n0))
                {
                    if (e.KeyCode == Keys.Right && c0 < e0)
                    {
                        e.Handled = true; e.SuppressKeyPress = true;
                        Editor.SelectionStart = e0; LayoutChips();
                        return;
                    }
                    if (e.KeyCode == Keys.Left && c0 >= s0)
                    {
                        e.Handled = true; e.SuppressKeyPress = true;
                        // 胶囊左侧那一格是"胶囊外面"，光标落那儿才露得出来
                        Editor.SelectionStart = s0 > 0 ? s0 - 1 : e0;
                        LayoutChips();
                        return;
                    }
                }
                // 光标正停在胶囊左外侧那一格，右移一次要整颗跨过去，不能一格一格蹭进胶囊
                if (e.KeyCode == Keys.Right)
                {
                    int s1, e1, n1;
                    if (TokenAround(c0 + 1, t0, out s1, out e1, out n1) && s1 == c0 + 1 && c0 + 1 <= t0.Length)
                    {
                        e.Handled = true; e.SuppressKeyPress = true;
                        Editor.SelectionStart = e1; LayoutChips();
                        return;
                    }
                }
                return;
            }
            if (e.KeyCode != Keys.Back && e.KeyCode != Keys.Delete) return;
            string txt = Editor.Text;
            int c = Editor.SelectionStart;
            int start, end, no;
            if (!TokenAround(c, txt, out start, out end, out no)) return;
            if (e.KeyCode == Keys.Back && c == start) return;       // 光标在胶囊左端，退格不该吃它
            if (e.KeyCode == Keys.Delete && c == end) return;       // 光标在胶囊右端，Delete 不该吃它
            e.Handled = true;
            e.SuppressKeyPress = true;
            RemoveToken(start, end);
        }

        // 鼠标点进胶囊中间时，把光标吸到最近的那一端：胶囊是一颗整的，
        // 绝不能让闪烁光标停在胶囊覆盖的字上（那样看着就是"光标被胶囊盖住"）
        public void SnapCaret()
        {
            if (Editor == null || Editor.SelectionLength != 0) return;
            int c = Editor.SelectionStart;
            string txt = Editor.Text;
            // 按"胶囊真实矩形"判：胶囊比 "图N" 宽，光标很容易落在它盖住的空格里，
            // 只看记号下标是判不出来的（那就是"光标被压成胶囊下方"的真正原因）。
            try
            {
                Point pc = Editor.GetPositionFromCharIndex(Math.Min(c, txt.Length));
                for (int k = 0; k < hitUsed && k < hits.Count; k++)
                {
                    ChipHit hit = hits[k];
                    if (pc.Y < hit.rect.Top - 3 || pc.Y > hit.rect.Bottom + 3) continue;
                    if (pc.X < hit.rect.Left || pc.X > hit.rect.Right) continue;
                    int right = Math.Min(hit.rightIdx, txt.Length);
                    // 左端退一格：胶囊前面永远有一格空格，光标停在那格（胶囊左侧空白上）才看得见
                    int left = hit.start > 0 ? hit.start - 1 : hit.start;
                    if ((pc.X - hit.rect.Left) < (hit.rect.Right - pc.X))
                        Editor.SelectionStart = left;
                    else
                        Editor.SelectionStart = right;
                    LayoutChips();
                    return;
                }
            }
            catch { }
            int st, en, no;
            if (!TokenAround(c, txt, out st, out en, out no)) return;
            if (c > st && c < en)
            {
                int mid = (st + en) / 2;
                Editor.SelectionStart = (c <= mid) ? (st > 0 ? st - 1 : en) : en;
                LayoutChips();
            }
            else if (c == st)
            {
                // 点在最前面那个"@"上：光标退到胶囊前面那格空格。
                // 胶囊前面一定有空格（EnsureChipSpacing 保证），所以这里能退得出去
                Editor.SelectionStart = st > 0 ? st - 1 : st;
                LayoutChips();
            }
        }

        // 光标落在某个 "@图N" 记号里（或正好在它的边界上）就返回这个记号的范围
        bool TokenAround(int caret, string txt, out int start, out int end, out int no)
        {
            start = 0; end = 0; no = -1;
            int i = 0;
            while (true)
            {
                i = txt.IndexOf("@图", i, StringComparison.Ordinal);
                if (i < 0) return false;
                int d = i + 2, e = d;
                while (e < txt.Length && char.IsDigit(txt[e])) e++;
                if (e == d) { i += 2; continue; }
                int tail = e;
                while (tail < txt.Length && txt[tail] == ' ') tail++;
                if (caret >= i && caret <= tail)
                {
                    start = i; end = tail;
                    try { no = int.Parse(txt.Substring(d, e - d)); } catch { no = -1; }
                    return true;
                }
                i = e;
            }
        }

        void RemoveToken(int start, int end)
        {
            string txt = Editor.Text;
            int s2 = start, e2 = end;
            if (end > txt.Length) end = txt.Length;
            string path = null;
            int no = -1;
            try
            {
                string frag = txt.Substring(s2, Math.Min(end, txt.Length) - s2).Trim();
                if (frag.StartsWith("@图")) no = int.Parse(frag.Substring(2));
                if (no > 0) path = ChipMap.ContainsKey(no) ? ChipMap[no] : null;
            }
            catch { }
            string before = txt.Substring(0, s2);
            string after = (end < txt.Length) ? txt.Substring(end) : "";
            // 记号连着尾随空格一起被删掉，前后两段字会粘在一起（"按不出"+"hello"）：
            // 两边都不是空格时补一个空格，句子读起来才正常
            string mid = "";
            if (before.Length > 0 && after.Length > 0 && !before.EndsWith(" ") && !after.StartsWith(" ")) mid = " ";
            Editor.Text = before + mid + after;
            Editor.SelectionStart = Math.Min(s2 + mid.Length, Editor.TextLength);
            if (no > 0) StripChipIfUnused(no);
            // 删胶囊只删记号本身：参考图区那张图照样留着，用户可以再 @ 引用一次
            LayoutChips();
        }

        void StripChipIfUnused(int no)
        {
            // 注意不能用 Contains("@图"+no)：那样 "@图1" 会被 "@图11" 误判成还有，
            // 这里按真实记号（数字段）逐个比，删掉一颗不影响同号的另一颗。
            if (AnyToken(no)) return;
            ChipMap.Remove(no);
        }

        bool AnyToken(int no)
        {
            string txt = Editor.Text;
            int i = 0;
            while (true)
            {
                i = txt.IndexOf("@图", i, StringComparison.Ordinal);
                if (i < 0) return false;
                int d = i + 2, e = d;
                while (e < txt.Length && char.IsDigit(txt[e])) e++;
                if (e > d)
                {
                    int n = -1;
                    try { n = int.Parse(txt.Substring(d, e - d)); }
                    catch { }
                    if (n == no) return true;
                }
                i = e > i ? e : i + 2;
            }
        }

        bool repairing;
        // 兜底修复：任何被敲坏/粘贴坏的记号（"@图"没数字、图号不在映射里）当场整段清掉，
        // 保证胶囊永远是完整一颗，不可能只剩 "@" 或半个数字
        void RepairChips()
        {
            if (repairing || Editor == null) return;
            string txt = Editor.Text;
            int i = 0;
            bool changed = false;
            StringBuilder sb = new StringBuilder(txt);
            while (true)
            {
                i = sb.ToString().IndexOf("@图", i, StringComparison.Ordinal);
                if (i < 0) break;
                string cur = sb.ToString();
                int d = i + 2, e = d;
                while (e < cur.Length && char.IsDigit(cur[e])) e++;
                if (e == d) { sb.Remove(i, 2); changed = true; continue; }
                int no = -1;
                try { no = int.Parse(cur.Substring(d, e - d)); } catch { }
                if (no <= 0)
                {
                    int tail = e;
                    while (tail < cur.Length && cur[tail] == ' ') tail++;
                    sb.Remove(i, tail - i);
                    changed = true;
                    continue;
                }
                // 有图号但当前映射里没有（例如"使用提示词"把历史提示词填回来）：
                // 保留原文即可，别把用户看不见就删掉的字从提示词里抠走
                i = e;
            }
            if (!changed) return;
            repairing = true;
            try
            {
                int caret = Editor.SelectionStart;
                Editor.Text = sb.ToString();
                Editor.SelectionStart = Math.Min(caret, Editor.TextLength);
            }
            finally { repairing = false; }
        }

        // 记号前后必须留够空格：胶囊比底下的"@图N"宽，多出来的那截只能从空格里借，
        // 借不够就会往两边挤——压住前后的字、压住光标，甚至把缩略图挤没。
        // 这里按真实像素算出每颗胶囊要借多少格，一次补齐。
        bool spacingFix;

        // 一个空格的像素宽（用"x x"和"xx"相减量，MeasureText 对纯空格会吃掉）
        int SpaceWidth()
        {
            Font f = Editor != null ? Editor.Font : Font;
            int a = TextRenderer.MeasureText("x x", f, Size.Empty, TextFormatFlags.NoPadding).Width;
            int b = TextRenderer.MeasureText("xx", f, Size.Empty, TextFormatFlags.NoPadding).Width;
            int w = a - b;
            return w < 3 ? 5 : w;
        }

        // 某颗胶囊要借多少格空格：按胶囊内容宽度和"@图N"字形宽度的差额算
        int ChipPadChars(int no)
        {
            Font f = Editor != null ? Editor.Font : Font;
            string lab = "图" + no;
            int textW = TextRenderer.MeasureText(lab, f, Size.Empty, TextFormatFlags.NoPadding).Width;
            int spanW = TextRenderer.MeasureText("@" + lab, f, Size.Empty, TextFormatFlags.NoPadding).Width;
            int h = Math.Min(24, Math.Max(19, LinePitch()));
            int thumbS = Math.Max(11, h - 6);
            int contentW = 5 + thumbS + 4 + textW + 5;
            // 后面至少留一格空格的距离，胶囊不跟后文贴在一起
            int padPx = contentW - spanW + SpaceWidth() + 3;
            int need = (int)Math.Ceiling(padPx / (double)SpaceWidth());
            if (need < 2) need = 2;
            if (need > 14) need = 14;                  // 再宽也不至于十几格，防止算飞
            return need;
        }

        void EnsureChipSpacing()
        {
            if (spacingFix || Editor == null) return;
            string txt = Editor.Text;
            if (txt.IndexOf("@图", StringComparison.Ordinal) < 0) return;
            StringBuilder sb = new StringBuilder(txt.Length + 16);
            int i = 0, caret = Editor.SelectionStart, shift = 0;
            bool changed = false;
            while (i < txt.Length)
            {
                if (txt[i] == '@' && i + 1 < txt.Length && txt[i + 1] == '图')
                {
                    int d = i + 2, j = d;
                    while (j < txt.Length && char.IsDigit(txt[j])) j++;
                    if (j > d)
                    {
                        int no = -1;
                        try { no = int.Parse(txt.Substring(d, j - d)); }
                        catch { }
                        int need = no > 0 ? ChipPadChars(no) : 4;
                        // 前面不是空格/行首 → 补一个；文本最前面也补一个，
                        // 否则胶囊贴着列 0，光标停在它前面时会被圆角压住，@ 的笔画还会漏出一个小黑点
                        if (sb.Length == 0 || (sb[sb.Length - 1] != ' ' && sb[sb.Length - 1] != '\n'))
                        {
                            sb.Append(' ');
                            changed = true;
                            if (i <= caret) shift++;
                        }
                        sb.Append(txt, i, j - i);
                        // 后面补齐到 need 格（不够才补，够了不动，避免每帧改文本）
                        int have = 0, k2 = j;
                        while (k2 < txt.Length && txt[k2] == ' ') { have++; k2++; }
                        if (have < need)
                        {
                            int add = need - have;
                            sb.Append(' ', add);
                            changed = true;
                            if (caret >= j) shift += add;
                        }
                        i = j;
                        continue;
                    }
                }
                sb.Append(txt[i]);
                i++;
            }
            if (!changed) return;
            spacingFix = true;
            try
            {
                Editor.Text = sb.ToString();
                Editor.SelectionStart = Math.Min(Math.Max(0, caret + shift), Editor.TextLength);
            }
            finally { spacingFix = false; }
        }

        // 图号 → 图片路径。映射表可能落后一步（删掉/重排过参考图），
        // 落后的那一步不能把胶囊当成普通文字：退回去按当前参考图顺序取。
        string PathForNo(int no)
        {
            string p = null;
            if (ChipMap != null && ChipMap.ContainsKey(no)) p = ChipMap[no];
            if (string.IsNullOrEmpty(p) && ChipSource != null)
            {
                List<string> cur = ChipSource();
                if (cur != null && no >= 1 && no <= cur.Count) p = cur[no - 1];
            }
            return string.IsNullOrEmpty(p) ? null : p;
        }

        // 取一颗可复用的胶囊：排版是每敲一个字就跑一遍，每次都销毁重建会整片闪
        ChipPill UsePill(int i)
        {
            if (i < pills.Count) return pills[i];
            ChipPill p = new ChipPill(1, null, Editor.Font);
            p.Clicked += (pp, ch) =>
            {
                // 必须先把焦点交回文本框、再异步弹大图：在大图弹出的同一拍里抢焦点，
                // 会让大图刚 Show 出来就被判定“失活”而立刻关掉（表现为点了胶囊没反应）。
                string cpath = ((ChipPill)pp).Path;
                Editor.Focus();
                BeginInvoke(new Action(() =>
                {
                    try { if (ChipClicked != null) ChipClicked(cpath); }
                    catch { }
                }));
            };
            Controls.Add(p);
            // WinForms 里 Add 是"压到最底层"，不 BringToFront 就会整颗藏在白色 Editor 后面，
            // 只在编辑器上边缘露出一条浅蓝横带（用户报的"胶囊只剩一条横带"就是这个）
            p.BringToFront();
            pills.Add(p);
            return p;
        }

        // 把所有 "图N" 记号上方盖一颗胶囊按钮（位置按当前排版/滚动实时算）
        public void LayoutChips()
        {
            if (Editor == null) return;
            if ((ChipMap == null || ChipMap.Count == 0) && ChipSource == null)
            {
                for (int k = 0; k < pills.Count; k++) if (pills[k].Visible) pills[k].Visible = false;
                hitUsed = 0;
                return;
            }
            EnsureChipSpacing();
            hitUsed = 0;
            string txt = Editor.Text;
            int idx = 0, used = 0;
            while (true)
            {
                idx = txt.IndexOf("@图", idx, StringComparison.Ordinal);
                if (idx < 0) break;
                int d = idx + 2, e = d;
                while (e < txt.Length && char.IsDigit(txt[e])) e++;
                int no = -1;
                try { if (e > d) no = int.Parse(txt.Substring(d, e - d)); }
                catch { }
                if (no > 0 && PathForNo(no) != null)
                {
                    string cpath0 = PathForNo(no);
                    string label = "@图" + no;
                    Point pt = Editor.GetPositionFromCharIndex(idx);
                    Point panelPt = PointToClient(Editor.PointToScreen(pt));
                    int tw = TextRenderer.MeasureText(label, Editor.Font, Size.Empty, TextFormatFlags.NoPadding).Width;
                    // 胶囊的宽度只量到"记号本体"为止（不含后面那个空格）：
                    // 量到空格就会把光标所在的那一格盖住，光标看着像被压在胶囊底下。
                    int span = 0;
                    if (e < txt.Length)
                    {
                        Point pend = Editor.GetPositionFromCharIndex(e);
                        if (pend.Y == pt.Y) span = pend.X - pt.X;
                    }
                    if (span <= 0) span = tw + 4;
                    if (span < tw + 2) span = tw + 2;
                    ChipPill pill = UsePill(used++);
                    pill.Reset(no, cpath0, Editor.Font);
                    // 胶囊高度贴着行高再收 2px：比字高一点点，又不顶到上下两行的文字
                    int h = Math.Min(24, Math.Max(19, lineH));
                    // 左缘跟字形左缘齐平，把 "@图N" 完整盖住（露 1~2px 就是那个"小黑点"）
                    int px = panelPt.X;
                    // 内容宽度 = 小缩略图 + 间隔 + "图N" + 两侧留白
                    int thumbS = Math.Max(11, h - 6);
                    int contentW = 5 + thumbS + 4 + pill.TextWidth + 5;
                    int wPill = Math.Max(contentW, span + 8);
                    // 绝不按"右边还剩多少"去压缩：一压缩略图就挤没了。
                    // 放不下就让整颗换到下一行（文本里补的尾随空格会自然换行）
                    int y = panelPt.Y + (lineH - h) / 2;
                    bool onScreen = (y + h > 0) && (y < Height);
                    bool moved = (pill.Width != wPill) || (pill.Height != h)
                        || (pill.Location.X != px) || (pill.Location.Y != y);
                    if (moved) pill.Size = new Size(wPill, h);
                    if (moved) pill.Location = new Point(px, y);
                    // 滚出可视区就藏起来（以前在这里把 y 夹回面板内，结果胶囊滚到顶就"定"在那儿不动了）
                    if (pill.Visible != onScreen) pill.Visible = onScreen;
                    if (!onScreen) { idx = e > idx ? e : idx + 2; continue; }
                    // 只有真的变了才重画：每敲一个字都全量 Invalidate 就是"胶囊一直闪"的来源
                    // 胶囊必须每拍都重画：它是叠在原生 TextBox 上的一层子窗口，
                    // 输入框每次重绘都会把这块盖掉，不跟着重画就整颗消失（只剩上沿一条横带）
                    pill.Invalidate();

                    // 记下这颗胶囊实际盖住的矩形，供光标吸附用
                    int tailEnd = e;
                    while (tailEnd < txt.Length && txt[tailEnd] == ' ') tailEnd++;
                    int rightIdx = e;
                    try
                    {
                        for (int k = e; k <= tailEnd; k++)
                        {
                            Point pk = Editor.GetPositionFromCharIndex(k);
                            if (pk.Y != pt.Y) { rightIdx = k; break; }
                            if (pk.X >= px + wPill) { rightIdx = k; break; }
                            rightIdx = k;
                        }
                    }
                    catch { rightIdx = tailEnd; }
                    ChipHit hit;
                    if (hitUsed < hits.Count) hit = hits[hitUsed];
                    else { hit = new ChipHit(); hits.Add(hit); }
                    hitUsed++;
                    hit.start = idx; hit.rightIdx = rightIdx; hit.rect = new Rectangle(px, y, wPill, h);
                }
                idx = e > idx ? e : idx + 2;
            }
            for (int k = used; k < pills.Count; k++) if (pills[k].Visible) pills[k].Visible = false;
        }

        // 参考图区变了（删了一张 / 拖拽换了顺序）之后，让文本框里的胶囊跟着对齐：
        // 图没了 → 对应胶囊整颗删掉；顺序变了 → 胶囊上的图号跟着改成新序号。
        // 必须照着"路径"认，不能照旧图号认，否则删第 1 张以后后面的全错位。
        public void SyncChipNumbers()
        {
            if (Editor == null || ChipSource == null) return;
            if (ChipMap.Count == 0) return;
            List<string> cur = ChipSource();
            if (cur == null) cur = new List<string>();
            string txt = Editor.Text;
            StringBuilder sb = new StringBuilder(txt.Length);
            Dictionary<int, string> map = new Dictionary<int, string>();
            int i = 0;
            while (i < txt.Length)
            {
                if (txt[i] == '@' && i + 1 < txt.Length && txt[i + 1] == '图')
                {
                    int d = i + 2, e = d;
                    while (e < txt.Length && char.IsDigit(txt[e])) e++;
                    if (e > d)
                    {
                        int no = -1;
                        try { no = int.Parse(txt.Substring(d, e - d)); } catch { }
                        string path;
                        if (no > 0 && ChipMap.TryGetValue(no, out path))
                        {
                            int nn = cur.IndexOf(path) + 1;
                            if (nn > 0)
                            {
                                sb.Append("@图").Append(nn);
                                map[nn] = path;
                                i = e;
                                continue;
                            }
                            // 这张参考图已经被删了：记号连同后面的空格一起清掉
                            i = e;
                            while (i < txt.Length && txt[i] == ' ') i++;
                            continue;
                        }
                    }
                }
                sb.Append(txt[i]);
                i++;
            }
            string res = sb.ToString();
            ChipMap.Clear();
            foreach (KeyValuePair<int, string> kv in map) ChipMap[kv.Key] = kv.Value;
            if (res != txt)
            {
                int caret = Editor.SelectionStart;
                int lenDelta = res.Length - txt.Length;
                Editor.Text = res;
                Editor.SelectionStart = Math.Max(0, Math.Min(caret + Math.Min(0, lenDelta), Editor.TextLength));
            }
            LayoutChips();
        }
    }

    // 提示词里的参考图胶囊：缩略图 + "图N"，点一下看大图
    public class ChipPill : Control
    {
        public string Path;
        int no;
        Bitmap thumb; int thumbFor = -1;
        public event EventHandler Clicked;

        public ChipPill(int no, string path, Font f)
        {
            this.no = no; Path = path;
            Font = f;
            Cursor = Cursors.Hand;
            TabStop = false;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);
        }

        public bool ResetNeeded(int newNo, string newPath) { return no != newNo || Path != newPath; }

        // 复用同一颗控件改图号/换图（排版每敲一个字都跑，销毁重建会整片闪）
        public void Reset(int newNo, string newPath, Font f)
        {
            bool dirty = (no != newNo) || (Path != newPath);
            no = newNo;
            if (Path != newPath)
            {
                Path = newPath;
                if (thumb != null) { try { thumb.Dispose(); } catch { } thumb = null; thumbFor = -1; }
            }
            if (f != null && !ReferenceEquals(Font, f)) Font = f;
            if (dirty) Invalidate();
        }

        // 胶囊里那个小缩略图的边长：先按高度算，块头不够宽时按宽度缩，实在放不下就不画图只留字
        public int ThumbSize
        {
            get
            {
                int byH = Math.Max(11, Height - 6);
                int byW = Width - 5 - 4 - TextWidth - 5;
                return Math.Min(byH, byW);
            }
        }

        // "图1" 这几个字的宽度：LayoutChips 也要用同一个值算胶囊宽度
        public int TextWidth
        {
            get
            {
                return TextRenderer.MeasureText(Label, Font, Size.Empty, TextFormatFlags.NoPadding).Width;
            }
        }

        public string Label { get { return "图" + no; } }

        // "图1" 这几个字的高度：LayoutChips 用它定胶囊高度（只比文字高一点点）
        public int TextHeight
        {
            get { return TextRenderer.MeasureText(Label, Font, Size.Empty, TextFormatFlags.NoPadding).Height; }
        }

        Bitmap Thumb()
        {
            int s = ThumbSize;
            if (thumb != null && thumbFor == s && thumb.Width == s) return thumb;
            if (thumb != null) { try { thumb.Dispose(); } catch { } thumb = null; }
            thumbFor = s;
            try
            {
                if (string.IsNullOrEmpty(Path) || !File.Exists(Path)) return null;
                using (FileStream fs = new FileStream(Path, FileMode.Open, FileAccess.Read))
                using (Image src = Image.FromStream(fs))
                {
                    thumb = new Bitmap(s, s);
                    using (Graphics g = Graphics.FromImage(thumb))
                    {
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        // 居中裁成正方形，缩略图不拉变形
                        float k = Math.Max(s / (float)src.Width, s / (float)src.Height);
                        int dw = (int)Math.Ceiling(src.Width * k), dh = (int)Math.Ceiling(src.Height * k);
                        g.DrawImage(src, new Rectangle((s - dw) / 2, (s - dh) / 2, dw, dh));
                    }
                }
            }
            catch { thumb = null; }
            return thumb;
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Left && Clicked != null) Clicked(this, EventArgs.Empty);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            Rectangle rc = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath p = Ui.Rounded(rc, Height / 2))
            using (SolidBrush b = new SolidBrush(Color.FromArgb(230, 237, 252)))
                g.FillPath(b, p);
            using (GraphicsPath p3 = Ui.Rounded(rc, Height / 2))
            using (Pen pen = new Pen(Color.FromArgb(186, 205, 244)))
                g.DrawPath(pen, p3);

            // 内容 = 小缩略图 + "图N"，整组在胶囊里严格居中（左右留白一样宽）
            int s = ThumbSize;
            bool withThumb = s >= 10;
            if (!withThumb) s = 0;
            int gap = withThumb ? 4 : 0;
            int tw = TextWidth;
            int total = s + gap + tw;
            int x = Math.Max(0, (Width - total) / 2);
            int y = (Height - s) / 2;

            if (withThumb)
            {
                Rectangle tr = new Rectangle(x, y, s, s);
                using (GraphicsPath tp = Ui.Rounded(tr, 4))
                {
                    Bitmap bmp = Thumb();
                    if (bmp != null)
                    {
                        // 裁进圆角里直接画：TextureBrush 平移后再 FillPath 只画得出一块，别用
                        Region old = g.Clip;
                        g.SetClip(tp, CombineMode.Intersect);
                        g.DrawImage(bmp, tr);
                        g.Clip = old;
                        if (old != null) old.Dispose();
                    }
                    else
                    {
                        using (SolidBrush b = new SolidBrush(Color.FromArgb(206, 214, 234)))
                            g.FillPath(b, tp);
                    }
                    using (Pen pen = new Pen(Color.FromArgb(200, 186, 205, 244)))
                        g.DrawPath(pen, tp);
                }
            }

            int lx = x + s + gap;
            TextRenderer.DrawText(g, Label, Font, new Rectangle(lx, 0, Width - lx, Height),
                Color.FromArgb(52, 84, 180), TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { try { if (thumb != null) thumb.Dispose(); } catch { } thumb = null; }
            base.Dispose(disposing);
        }
    }

    // 生成等待动画：一圈渐隐圆点旋转
    public class Spinner : Control
    {
        System.Windows.Forms.Timer t;
        int ang;
        public Spinner()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            Size = new Size(24, 24);
            BackColor = Color.FromArgb(246, 248, 252);
            Visible = false;
        }

        public void Start()
        {
            if (t == null)
            {
                t = new System.Windows.Forms.Timer();
                t.Interval = 34;
                t.Tick += (s, e) => { ang = (ang + 30) % 360; Invalidate(); };
            }
            Visible = true;
            t.Start();
            Invalidate();
        }

        public void Stop()
        {
            if (t != null) t.Stop();
            Visible = false;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float k = Width / 24f;
            int cx = Width / 2, cy = Height / 2;
            int r = (int)(7 * k);
            if (r < 4) r = 4;
            int dr = (int)(5 * k);
            if (dr < 3) dr = 3;
            for (int i = 0; i < 8; i++)
            {
                double a = (ang + i * 45) * Math.PI / 180;
                int x = cx + (int)(Math.Cos(a) * r);
                int y = cy + (int)(Math.Sin(a) * r);
                int alpha = 40 + (8 - i) * 24;
                if (alpha > 230) alpha = 230;
                using (SolidBrush b = new SolidBrush(Color.FromArgb(alpha, 108, 140, 245)))
                    g.FillEllipse(b, x - dr / 2, y - dr / 2, dr, dr);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && t != null) { t.Stop(); t.Dispose(); t = null; }
            base.Dispose(disposing);
        }
    }

    // 尺寸下拉：选项与常见模型尺寸一一对应，选中值直接作为请求里的 size 发给模型
    public class SizePicker : Control
    {
        public static readonly string[] Values = {
            "auto",
            "1024x1024","1672x941","941x1672","1443x1090","1090x1443",
            "1536x1024","1024x1536","1408x1120","1120x1408",
            "1920x832","832x1920","1792x896","896x1792"
        };
        static readonly string[] RatioNames = {
            "", "(1:1)","(16:9)","(9:16)","(4:3)","(3:4)","(3:2)","(2:3)","(5:4)","(4:5)","(21:9)","(9:21)","(2:1)","(1:2)"
        };

        public string Value = "auto";
        public event EventHandler ValueChanged;
        SizePopup popup;
        bool hover;

        public SizePicker()
        {
            Font = new Font("微软雅黑", 10F);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
        }

        public static string TextOf(string v)
        {
            for (int i = 0; i < Values.Length; i++)
                if (Values[i] == v) return i == 0 ? "自适应" : Values[i] + " " + RatioNames[i];
            return "自适应";
        }

        public void SetValue(string v)
        {
            if (v == null) v = "auto";
            Value = v;
            Invalidate();
            if (ValueChanged != null) ValueChanged(this, EventArgs.Empty);
        }

        // 画一个按比例的小方框图标：选什么比例一眼能看出来
        public static void DrawGlyph(Graphics g, Rectangle box, string v, Color c)
        {
            if (v == "auto")
            {
                using (Pen p = new Pen(c, 1.5f))
                {
                    p.StartCap = LineCap.Round; p.EndCap = LineCap.Round;
                    int cx = box.X + box.Width / 2, cy = box.Y + box.Height / 2;
                    int half = box.Width / 2 - 1;
                    g.DrawLine(p, cx - half, cy, cx + half - 2, cy);
                    g.DrawLine(p, cx + half, cy, cx + half - 4, cy - 3);
                    g.DrawLine(p, cx + half, cy, cx + half - 4, cy + 3);
                    g.DrawLine(p, cx - half, cy, cx - half + 4, cy - 3);
                    g.DrawLine(p, cx - half, cy, cx - half + 4, cy + 3);
                }
                return;
            }
            float w = 12f, h = 12f;
            int idx = 0;
            for (int i = 0; i < Values.Length; i++) if (Values[i] == v) { idx = i; break; }
            string name = RatioNames[idx];
            int a = 1, b = 1;
            try
            {
                string r = name.Trim('(', ')');
                string[] parts = r.Split(':');
                a = int.Parse(parts[0]); b = int.Parse(parts[1]);
            }
            catch { }
            if (a >= b) { h = Math.Max(6f, w * b / a); } else { w = Math.Max(6f, h * a / b); }
            RectangleF gr = new RectangleF(box.X + (box.Width - w) / 2f, box.Y + (box.Height - h) / 2f, w, h);
            using (Pen p = new Pen(c, 1.4f)) g.DrawRectangle(p, gr.X, gr.Y, gr.Width, gr.Height);
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            Rectangle rc = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = Ui.Rounded(rc, 10))
            {
                using (SolidBrush br = new SolidBrush(hover ? Color.FromArgb(242, 245, 252) : Color.FromArgb(246, 248, 252))) g.FillPath(br, path);
                using (Pen pen = new Pen(hover ? Color.FromArgb(198, 210, 240) : Color.FromArgb(226, 231, 242))) g.DrawPath(pen, path);
            }
            DrawGlyph(g, new Rectangle(14, (Height - 16) / 2, 16, 16), Value, Color.FromArgb(70, 84, 118));
            TextRenderer.DrawText(g, TextOf(Value), Font, new Rectangle(38, 0, Width - 38 - 26, Height),
                Color.FromArgb(31, 41, 55), TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
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
            popup = new SizePopup(this);
            popup.Show(FindForm());
        }
    }

    class SizePopup : Form
    {
        public SizePopup(SizePicker owner)
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            BackColor = Color.White;
            DoubleBuffered = true;
            int ih = 34, n = SizePicker.Values.Length;
            int w = 262;
            Size = new Size(w, n * ih + 12);
            for (int i = 0; i < n; i++)
            {
                SizeRow it = new SizeRow(SizePicker.Values[i]);
                it.Selected = (owner.Value == SizePicker.Values[i]);
                it.Location = new Point(6, 6 + i * ih);
                it.Size = new Size(w - 12, ih);
                string v = SizePicker.Values[i];
                it.Picked += (s, e) => { owner.SetValue(v); Close(); };
                Controls.Add(it);
            }
            Rectangle wa = Screen.FromPoint(owner.PointToScreen(new Point(0, owner.Height))).WorkingArea;
            Point pt = owner.PointToScreen(new Point(0, owner.Height + 6));
            if (pt.Y + Height > wa.Bottom) pt = owner.PointToScreen(new Point(0, 0));
            if (pt.Y < wa.Top) pt.Y = wa.Top;
            if (pt.Y + Height > wa.Bottom) pt.Y = Math.Max(wa.Top, wa.Bottom - Height);
            if (pt.X + Width > wa.Right) pt.X = wa.Right - Width - 6;
            Location = pt;
            Deactivate += (s, e) => Close();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath p = Ui.Rounded(new Rectangle(0, 0, Width - 1, Height - 1), 10))
            using (Pen pen = new Pen(Color.FromArgb(226, 231, 242))) g.DrawPath(pen, p);
        }
    }

    class SizeRow : Control
    {
        string val;
        bool hover;
        public bool Selected;
        public event EventHandler Picked;

        public SizeRow(string v)
        {
            val = v;
            Font = new Font("微软雅黑", 9.5F);
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); if (Picked != null) Picked(this, EventArgs.Empty); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            Rectangle rc = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = Ui.Rounded(rc, 8))
            {
                if (Selected) { using (SolidBrush br = new SolidBrush(Color.FromArgb(219, 236, 250))) g.FillPath(br, path); }
                else if (hover) { using (SolidBrush br = new SolidBrush(Color.FromArgb(241, 244, 252))) g.FillPath(br, path); }
            }
            SizePicker.DrawGlyph(g, new Rectangle(14, (Height - 16) / 2, 16, 16), val, Color.FromArgb(52, 74, 112));
            TextRenderer.DrawText(g, SizePicker.TextOf(val), Font, new Rectangle(40, 0, Width - 40 - 40, Height),
                Color.FromArgb(31, 41, 55), TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            if (Selected)
            {
                using (Pen pen = new Pen(Color.FromArgb(38, 84, 140), 2f))
                {
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;
                    g.DrawLine(pen, Width - 32, Height / 2, Width - 27, Height / 2 + 5);
                    g.DrawLine(pen, Width - 27, Height / 2 + 5, Width - 18, Height / 2 - 5);
                }
            }
        }
    }

    // 图像质量下拉：只有支持分档输出的模型才出现（1K / 2K / 4K）。
    // 出现在界面上就必须真的传下去：grsai 协议里是 imageSize 字段，选了 4K 服务端就按 4K 出图。
    public class QualityPicker : Control
    {
        public static readonly string[] Values = { "1K", "2K", "4K" };

        // 哪些模型显示这个下拉框
        public static bool AppliesTo(string model, out bool locked)
        {
            locked = false;
            string low = (model ?? "").Trim().ToLowerInvariant();
            if (low.Length == 0) return false;
            low = low.Replace("nono-banana", "nano-banana");     // 容错：用户/接口偶尔写成 nono
            // 名字里自带画质档位的型号，只能用它自己的那一档，不许改
            string fixedQ = FixedQuality(low);
            if (fixedQ.Length > 0) { locked = true; return true; }
            if (low.Contains("nano-banana")) return true;
            string[] exact = { "gpt-image-2-vip", "gpt-image-2.5-flare", "gpt-image-2.5-sunburst", "gpt-image-2.6-flare", "gpt-image-2.6-sunburst" };
            foreach (string e in exact) if (low == e) return true;
            return false;
        }

        // 型号名里锁定画质的三种：只能 2K / 只能 4K
        public static string FixedQuality(string model)
        {
            string low = (model ?? "").Trim().ToLowerInvariant().Replace("nono-banana", "nano-banana");
            if (low == "nano-banana-pro-4k-vip") return "4K";
            if (low == "nano-banana-2-2k-cl") return "2K";
            if (low == "nano-banana-2-4k-cl") return "4K";
            return "";
        }

        // 下拉框上直接显示选中档位的中文说法（4K 超清 / 2K 高清 / 1K 标准）
        public static string QualityName(string v)
        {
            if (v == "1K") return "标准";
            if (v == "2K") return "高清";
            return "超清";
        }

        public string Value = "2K";
        public bool Locked;                 // 固定质量：能看见、不能改
        public event EventHandler ValueChanged;
        QualityPopup popup;
        bool hover;

        public QualityPicker()
        {
            Font = new Font("微软雅黑", 9.5F);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
        }

        public void SetValue(string v, bool silent)
        {
            if (v != "1K" && v != "2K" && v != "4K") v = "2K";
            bool changed = v != Value;
            Value = v;
            Invalidate();
            if (!silent && changed && ValueChanged != null) ValueChanged(this, EventArgs.Empty);
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            Rectangle rc = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = Ui.Rounded(rc, 10))
            {
                using (SolidBrush br = new SolidBrush(hover ? Color.FromArgb(242, 245, 252) : Color.FromArgb(246, 248, 252))) g.FillPath(br, path);
                using (Pen pen = new Pen(hover ? Color.FromArgb(198, 210, 240) : Color.FromArgb(226, 231, 242))) g.DrawPath(pen, path);
            }
            // 左边一块小徽标，直接把当前档位写出来（4K 一眼可见）
            Rectangle badge = new Rectangle(12, (Height - 18) / 2, 30, 18);
            using (GraphicsPath bp = Ui.Rounded(badge, 5))
            using (SolidBrush bb = new SolidBrush(Color.FromArgb(226, 234, 250)))
                g.FillPath(bb, bp);
            TextRenderer.DrawText(g, Value, new Font("微软雅黑", 8F, FontStyle.Bold), badge,
                Color.FromArgb(70, 92, 160), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            // 不再写“图像质量”四个字：左边徽标是档位，右边直接是这一档的说法（4K 超清）
            TextRenderer.DrawText(g, Locked ? (QualityName(Value) + "（固定）") : QualityName(Value), Font, new Rectangle(48, 0, Width - 48 - 14, Height),
                Color.FromArgb(31, 41, 55), TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            if (!Locked)
            {
                using (Pen pen = new Pen(Color.FromArgb(120, 130, 152), 1.8f))
                {
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;
                    g.DrawLine(pen, Width - 22, Height / 2 - 3, Width - 15, Height / 2 + 1);
                    g.DrawLine(pen, Width - 15, Height / 2 + 1, Width - 8, Height / 2 - 3);
                }
            }
        }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            if (Locked) return;
            if (popup != null && popup.Visible) { popup.Close(); popup = null; return; }
            popup = new QualityPopup(this);
            popup.Show(FindForm());
        }
    }

    class QualityPopup : Form
    {
        public QualityPopup(QualityPicker owner)
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            BackColor = Color.White;
            DoubleBuffered = true;
            int ih = 34, n = QualityPicker.Values.Length;
            int w = 196;
            Size = new Size(w, n * ih + 12);
            for (int i = 0; i < n; i++)
            {
                QualityRow it = new QualityRow(QualityPicker.Values[i]);
                it.Selected = (owner.Value == QualityPicker.Values[i]);
                it.Location = new Point(6, 6 + i * ih);
                it.Size = new Size(w - 12, ih);
                string v = QualityPicker.Values[i];
                it.Picked += (s, e) => { owner.SetValue(v, false); Close(); };
                Controls.Add(it);
            }
            Rectangle wa = Screen.FromPoint(owner.PointToScreen(new Point(0, owner.Height))).WorkingArea;
            Point pt = owner.PointToScreen(new Point(0, owner.Height + 6));
            if (pt.Y + Height > wa.Bottom) pt = owner.PointToScreen(new Point(0, 0));
            if (pt.Y < wa.Top) pt.Y = wa.Top;
            if (pt.Y + Height > wa.Bottom) pt.Y = Math.Max(wa.Top, wa.Bottom - Height);
            if (pt.X + Width > wa.Right) pt.X = wa.Right - Width - 6;
            Location = pt;
            Deactivate += (s, e) => Close();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath p = Ui.Rounded(new Rectangle(0, 0, Width - 1, Height - 1), 10))
            using (Pen pen = new Pen(Color.FromArgb(226, 231, 242))) g.DrawPath(pen, p);
        }
    }

    class QualityRow : Control
    {
        string val;
        bool hover;
        public bool Selected;
        public event EventHandler Picked;

        public QualityRow(string v)
        {
            val = v;
            Font = new Font("微软雅黑", 9.5F);
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }

        static string Hint(string v)
        {
            if (v == "1K") return "标准";
            if (v == "2K") return "高清";
            return "超清";
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); if (Picked != null) Picked(this, EventArgs.Empty); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            Rectangle rc = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = Ui.Rounded(rc, 8))
            {
                if (Selected) { using (SolidBrush br = new SolidBrush(Color.FromArgb(219, 236, 250))) g.FillPath(br, path); }
                else if (hover) { using (SolidBrush br = new SolidBrush(Color.FromArgb(241, 244, 252))) g.FillPath(br, path); }
            }
            Rectangle badge = new Rectangle(12, (Height - 18) / 2, 30, 18);
            using (GraphicsPath bp = Ui.Rounded(badge, 5))
            using (SolidBrush bb = new SolidBrush(Color.FromArgb(226, 234, 250)))
                g.FillPath(bb, bp);
            TextRenderer.DrawText(g, val, new Font("微软雅黑", 8F, FontStyle.Bold), badge,
                Color.FromArgb(70, 92, 160), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            TextRenderer.DrawText(g, val + " · " + Hint(val), Font, new Rectangle(48, 0, Width - 48 - 34, Height),
                Color.FromArgb(31, 41, 55), TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            if (Selected)
            {
                using (Pen pen = new Pen(Color.FromArgb(38, 84, 140), 2f))
                {
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;
                    g.DrawLine(pen, Width - 30, Height / 2, Width - 25, Height / 2 + 5);
                    g.DrawLine(pen, Width - 25, Height / 2 + 5, Width - 16, Height / 2 - 5);
                }
            }
        }
    }

    // 模型下拉：清单只来自 API 自己回报的 /models，选中的字符串原样作为请求里的 model 发送，
    // 不做任何"猜模型名"的换算，避免选了 A 实际调 B。
    public class ModelPicker : Control
    {
        public string Value = "";
        public event EventHandler ValueChanged;
        public event EventHandler RefreshAsked;
        List<string> items = new List<string>();
        ModelPopup popup;
        bool hover;
        public string Hint = "";      // 没有模型时的占位提示

        public ModelPicker()
        {
            Font = new Font("微软雅黑", 9.5F);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
        }

        public List<string> Items { get { return items; } }

        public void AskRefresh()
        {
            if (RefreshAsked != null) RefreshAsked(this, EventArgs.Empty);
        }

        public void SetItems(List<string> list)
        {
            items.Clear();
            if (list != null) foreach (string s in list) if (!string.IsNullOrEmpty(s)) items.Add(s);
            if (items.Count > 0 && !items.Contains(Value)) Value = items[0];
            Invalidate();
        }

        public void SetValue(string v, bool silent)
        {
            Value = v ?? "";
            Invalidate();
            if (!silent && ValueChanged != null) ValueChanged(this, EventArgs.Empty);
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            Rectangle rc = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = Ui.Rounded(rc, 10))
            {
                using (SolidBrush br = new SolidBrush(hover ? Color.FromArgb(242, 245, 252) : Color.FromArgb(246, 248, 252))) g.FillPath(br, path);
                using (Pen pen = new Pen(hover ? Color.FromArgb(198, 210, 240) : Color.FromArgb(226, 231, 242))) g.DrawPath(pen, path);
            }
            // 小芯片图标
            using (GraphicsPath p2 = Ui.Rounded(new Rectangle(14, (Height - 16) / 2, 16, 16), 3))
            using (SolidBrush br2 = new SolidBrush(Color.FromArgb(226, 234, 250)))
                g.FillPath(br2, p2);
            using (Pen pn = new Pen(Color.FromArgb(108, 140, 245), 1.2f))
                g.DrawRectangle(pn, 17, (Height - 16) / 2 + 3, 10, 10);
            string txt = Value.Length > 0 ? Value : (Hint.Length > 0 ? Hint : "选择模型");
            TextRenderer.DrawText(g, txt, Font, new Rectangle(38, 0, Width - 38 - 26, Height),
                Value.Length > 0 ? Color.FromArgb(31, 41, 55) : Color.FromArgb(168, 176, 196),
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
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
            if (items.Count == 0)
            {
                if (RefreshAsked != null) RefreshAsked(this, EventArgs.Empty);
                return;
            }
            popup = new ModelPopup(this);
            popup.Show(FindForm());
        }
    }

    // 模型清单的滚动容器：自己画一条淡紫圆角滚动条，只有竖的，没有横的
    class ModelListHost : Panel
    {
        int contentH, offset;
        bool drag; int dragY, dragOff;
        const int BarW = 6, BarGap = 4;

        public ModelListHost()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Color.White;
        }

        public void Add(Control c) { Controls.Add(c); }

        public void SetContentHeight(int h)
        {
            contentH = h;
            Clamp();
            LayoutRows();
            Invalidate();
        }

        int MaxOffset { get { return Math.Max(0, contentH - Height); } }

        void Clamp()
        {
            if (offset > MaxOffset) offset = MaxOffset;
            if (offset < 0) offset = 0;
        }

        void LayoutRows()
        {
            foreach (Control c in Controls) c.Top = c.Top - 0; // 位置由 Scroll 统一换算
            ScrollBy(0);
        }

        public void ScrollBy(int by)
        {
            if (by != 0) { offset += by; Clamp(); }
            // 每行原始位置记在 Tag 上（第一次进来时抓一次）
            foreach (Control c in Controls)
            {
                if (c.Tag == null) c.Tag = c.Top;
                int baseTop = (int)c.Tag;
                c.Top = baseTop - offset;
            }
            Invalidate();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            ScrollBy(-Math.Sign(e.Delta) * 48);
        }

        // 滚动条的可用轨道
        Rectangle Track { get { return new Rectangle(Width - BarW - BarGap, 2, BarW, Height - 4); } }

        Rectangle Thumb
        {
            get
            {
                if (contentH <= Height) return Rectangle.Empty;
                Rectangle tr = Track;
                int th = Math.Max(28, (int)(tr.Height * (float)Height / contentH));
                int ty = tr.Y + (int)((tr.Height - th) * (float)offset / MaxOffset);
                return new Rectangle(tr.X, ty, tr.Width, th);
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Rectangle th = Thumb;
            if (!th.IsEmpty && th.Contains(e.Location))
            {
                drag = true; dragY = e.Y; dragOff = offset;
            }
            else if (!th.IsEmpty && Track.Contains(e.Location))
            {
                ScrollBy(e.Y < th.Y ? -Height : Height);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!drag) return;
            Rectangle tr = Track; int th = Thumb.Height;
            int span = Math.Max(1, tr.Height - th);
            int dy = e.Y - dragY;
            offset = dragOff + (int)(dy * (float)MaxOffset / span);
            Clamp(); ScrollBy(0);
        }

        protected override void OnMouseUp(MouseEventArgs e) { drag = false; base.OnMouseUp(e); }
        protected override void OnMouseLeave(EventArgs e) { if (!drag) base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.White);
            Rectangle tr = Track;
            using (GraphicsPath tp = Ui.Rounded(tr, BarW / 2))
            using (SolidBrush tb = new SolidBrush(Color.FromArgb(240, 243, 250)))
                g.FillPath(tb, tp);
            Rectangle th = Thumb;
            if (!th.IsEmpty)
            {
                using (GraphicsPath hp = Ui.Rounded(th, BarW / 2))
                using (LinearGradientBrush hb = new LinearGradientBrush(th, Color.FromArgb(172, 166, 250), Color.FromArgb(138, 152, 248), 90f))
                    g.FillPath(hb, hp);
            }
        }
    }

    class ModelPopup : Form
    {
        public ModelPopup(ModelPicker owner)
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            BackColor = Color.White;
            DoubleBuffered = true;
            List<string> list = owner.Items;
            int ih = 32, n = list.Count;
            int w = Math.Max(260, owner.Width + 80);
            Size = new Size(w, Math.Min(n * ih + 12, 460));
            // 只走自绘的竖向滚动条：原生 AutoScroll 会在下方再画一条灰色横向条，
            // 和主题完全不搭，也完全用不着（行宽永远不超过列表宽）
            ModelListHost host = new ModelListHost();
            host.Location = new Point(6, 6);
            host.Size = new Size(w - 12, Math.Min(n * ih, 448));
            host.BackColor = Color.White;
            Controls.Add(host);
            for (int i = 0; i < n; i++)
            {
                ModelRow it = new ModelRow(list[i]);
                it.Selected = (owner.Value == list[i]);
                it.Location = new Point(0, i * ih);
                it.Size = new Size(host.Width - 10, ih);
                string v = list[i];
                it.Picked += (s, e) => { owner.SetValue(v, false); Close(); };
                host.Add(it);
            }
            host.SetContentHeight(n * ih);
            // 底部刷新按钮
            Label re = new Label();
            re.Text = "从 API 重新获取模型清单";
            re.Font = new Font("微软雅黑", 8.5F);
            re.ForeColor = Color.FromArgb(108, 140, 245);
            re.TextAlign = ContentAlignment.MiddleLeft;
            re.Cursor = Cursors.Hand;
            re.Location = new Point(12, Height - 26);
            re.Size = new Size(w - 24, 20);
            re.Click += (s, e) => { Close(); owner.AskRefresh(); };
            Controls.Add(re);

            Rectangle wa = Screen.FromPoint(owner.PointToScreen(new Point(0, owner.Height))).WorkingArea;
            Point pt = owner.PointToScreen(new Point(0, owner.Height + 6));
            if (pt.Y + Height > wa.Bottom) pt = owner.PointToScreen(new Point(0, -Height - 6));
            if (pt.Y < wa.Top) pt.Y = wa.Top;
            if (pt.X + Width > wa.Right) pt.X = wa.Right - Width - 6;
            Location = pt;
            Deactivate += (s, e) => Close();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            foreach (Control c in Controls)
            {
                ModelListHost h = c as ModelListHost;
                if (h != null) { h.ScrollBy(-Math.Sign(e.Delta) * 48); return; }
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath p = Ui.Rounded(new Rectangle(0, 0, Width - 1, Height - 1), 10))
            using (Pen pen = new Pen(Color.FromArgb(226, 231, 242))) g.DrawPath(pen, p);
        }
    }

    class ModelRow : Control
    {
        string val;
        bool hover;
        public bool Selected;
        public event EventHandler Picked;

        public ModelRow(string v)
        {
            val = v;
            Font = new Font("微软雅黑", 9.5F);
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); if (Picked != null) Picked(this, EventArgs.Empty); }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            // 滚轮是由"有焦点的控件"收到的，行控件自己不处理就得往上递给滚动容器
            ModelListHost host = Parent as ModelListHost;
            if (host != null) host.ScrollBy(-Math.Sign(e.Delta) * 48);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            if (Selected || hover)
            {
                using (GraphicsPath path = Ui.Rounded(new Rectangle(0, 1, Width - 1, Height - 3), 8))
                using (SolidBrush br = new SolidBrush(Selected ? Color.FromArgb(219, 236, 250) : Color.FromArgb(241, 244, 252)))
                    g.FillPath(br, path);
            }
            TextRenderer.DrawText(g, val, Font, new Rectangle(12, 0, Width - 24, Height), Color.FromArgb(31, 41, 55),
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            if (Selected)
            {
                using (Pen pen = new Pen(Color.FromArgb(38, 84, 140), 2f))
                {
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;
                    g.DrawLine(pen, Width - 26, Height / 2, Width - 21, Height / 2 + 5);
                    g.DrawLine(pen, Width - 21, Height / 2 + 5, Width - 12, Height / 2 - 5);
                }
            }
        }
    }

    // 敲 @ 之后弹的参考图选择框：圆角、白底、缩略图 + 图号
    class RefPopup : Form
    {
        public string PickedPath;
        List<RefRow> rows = new List<RefRow>();

        public RefPopup(List<string> list, Point screenPt, Font f)
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            BackColor = Color.White;
            DoubleBuffered = true;
            int ih = 34, n = list.Count;
            int w = 250;
            Size = new Size(w, Math.Min(n * ih + 12, 380));
            for (int i = 0; i < n; i++)
            {
                RefRow r = new RefRow(i + 1, list[i], f);
                r.Location = new Point(6, 6 + i * ih);
                r.Size = new Size(w - 12, ih);
                string p = list[i];
                r.Picked += (s, e) => { PickedPath = p; DialogResult = DialogResult.OK; Close(); };
                Controls.Add(r);
                rows.Add(r);
            }
            Rectangle wa = Screen.FromPoint(screenPt).WorkingArea;
            Point pt = screenPt;
            if (pt.Y + Height > wa.Bottom) pt.Y = screenPt.Y - Height - (ih - 4);
            if (pt.X + Width > wa.Right) pt.X = wa.Right - Width - 4;
            Location = pt;
            Deactivate += (s, e) => { if (DialogResult == DialogResult.None) { DialogResult = DialogResult.Cancel; Close(); } };
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath p = Ui.Rounded(new Rectangle(0, 0, Width - 1, Height - 1), 10))
            using (Pen pen = new Pen(Color.FromArgb(222, 228, 240))) g.DrawPath(pen, p);
        }
    }

    class RefRow : Control
    {
        string path; int no;
        Bitmap thumb;
        bool hover;
        public event EventHandler Picked;

        public RefRow(int no, string path, Font f)
        {
            this.no = no; this.path = path;
            Font = f;
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            try
            {
                using (Image src = Image.FromFile(path))
                {
                    thumb = new Bitmap(22, 22);
                    using (Graphics g = Graphics.FromImage(thumb))
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.Clear(Color.FromArgb(232, 237, 248));
                        float k = Math.Max(22f / src.Width, 22f / src.Height);
                        int w = (int)(src.Width * k) + 1, h = (int)(src.Height * k) + 1;
                        g.DrawImage(src, (22 - w) / 2, (22 - h) / 2, w, h);
                    }
                }
            }
            catch { }
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); if (Picked != null) Picked(this, EventArgs.Empty); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            if (hover)
            {
                using (GraphicsPath p = Ui.Rounded(new Rectangle(0, 1, Width - 1, Height - 3), 8))
                using (SolidBrush b = new SolidBrush(Color.FromArgb(241, 244, 252))) g.FillPath(b, p);
            }
            int ty = (Height - 22) / 2;
            if (thumb != null) g.DrawImage(thumb, 6, ty, 22, 22);
            else
            {
                using (SolidBrush b = new SolidBrush(Color.FromArgb(226, 232, 246))) g.FillRectangle(b, 6, ty, 22, 22);
            }
            TextRenderer.DrawText(g, "图" + no, Font, new Rectangle(36, 0, 34, Height), Color.FromArgb(58, 92, 190),
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            TextRenderer.DrawText(g, Path.GetFileName(path), Font, new Rectangle(72, 0, Width - 78, Height), Color.FromArgb(96, 104, 124),
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }

    // 生成图像页：历史缩略图网格 + 参考图 + 提示词 + 生成按钮
    // 放大窗里那条参考图预览（只读，不能拖不能删）：和外面一样有缩略图 + 序号，
    // 让用户在大框里也知道 @图1 对应的是哪张图
    public class RefPreview : Control
    {
        public Func<List<string>> Source;
        int T = 56, Gap = 10;
        Dictionary<string, Bitmap> cache = new Dictionary<string, Bitmap>();

        public RefPreview()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Color.FromArgb(246, 248, 252);
        }

        Bitmap Thumb(string path)
        {
            Bitmap b;
            if (cache.TryGetValue(path, out b)) return b;
            try
            {
                using (Image src = Image.FromFile(path))
                {
                    b = new Bitmap(T, T);
                    using (Graphics g = Graphics.FromImage(b))
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        g.Clear(Color.FromArgb(238, 241, 248));
                        float s = Math.Max((float)T / src.Width, (float)T / src.Height);
                        int w = (int)(src.Width * s) + 1, h = (int)(src.Height * s) + 1;
                        g.DrawImage(src, (T - w) / 2, (T - h) / 2, w, h);
                    }
                }
            }
            catch { b = null; }
            cache[path] = b;
            return b;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            List<string> ps = Source != null ? Source() : null;
            if (ps == null || ps.Count == 0)
            {
                TextRenderer.DrawText(g, "还没有参考图", new Font("微软雅黑", 9F), new Rectangle(4, 0, Width, Height),
                    Color.FromArgb(176, 183, 200), TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
                return;
            }
            Font numFont = new Font("微软雅黑", 9F, FontStyle.Bold);
            int y = (Height - T) / 2;
            for (int i = 0; i < ps.Count; i++)
            {
                Rectangle rc = new Rectangle(i * (T + Gap), y, T, T);
                if (rc.Right > Width) break;
                Bitmap th = Thumb(ps[i]);
                using (GraphicsPath p = Ui.Rounded(rc, 10))
                {
                    if (th != null)
                    {
                        g.SetClip(p);
                        g.DrawImage(th, rc);
                        g.ResetClip();
                    }
                    else
                    {
                        using (SolidBrush b = new SolidBrush(Color.FromArgb(250, 240, 230))) g.FillPath(b, p);
                    }
                    using (Pen pen = new Pen(Color.FromArgb(222, 228, 242))) g.DrawPath(pen, p);
                }
                int bs = 18;
                Rectangle idx = new Rectangle(rc.X + 4, rc.Bottom - bs - 4, bs, bs);
                using (GraphicsPath bi = Ui.Rounded(idx, bs / 2))
                using (SolidBrush bb = new SolidBrush(Color.FromArgb(228, 234, 237, 245)))
                    g.FillPath(bb, bi);
                TextRenderer.DrawText(g, (i + 1).ToString(), numFont, idx, Color.FromArgb(52, 60, 84),
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
            numFont.Dispose();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { foreach (KeyValuePair<string, Bitmap> kv in cache) if (kv.Value != null) kv.Value.Dispose(); cache.Clear(); }
            base.Dispose(disposing);
        }
    }

    // 方形圆角图标按钮（只画一个放大镜，不写文字）：悬停淡紫底、按下更淡
    public class ZoomIconButton : Control
    {
        bool hover, down;
        public string Tip = "放大输入框";

        public ZoomIconButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            Size = new Size(38, 34);
            Cursor = Cursors.Hand;
            BackColor = Color.FromArgb(246, 248, 252);
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
                           : hover ? Color.FromArgb(238, 241, 253)
                                   : Color.FromArgb(248, 250, 255);
                using (SolidBrush b = new SolidBrush(fill)) g.FillPath(b, p);
                using (Pen pen = new Pen(hover ? Color.FromArgb(176, 194, 244) : Color.FromArgb(214, 222, 240), 1f))
                    g.DrawPath(pen, p);
            }
            Color c = down ? Color.FromArgb(74, 96, 190) : Color.FromArgb(96, 118, 190);
            // 图标＝一条对角线上两个相反方向的箭头（↖ ↘ 各自朝外），读作"放大/全屏"
            float cx = Width / 2f, cy = Height / 2f;
            float len = Math.Min(Width, Height) - 16f;
            using (Pen pen = new Pen(c, 1.8f))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                // 往右上的那支
                float ax = cx + len * 0.12f, ay = cy - len * 0.12f;
                float bx = cx + len * 0.40f, by = cy - len * 0.40f;
                g.DrawLine(pen, ax, ay, bx, by);
                g.DrawLine(pen, bx, by, bx - len * 0.20f, by);
                g.DrawLine(pen, bx, by, bx, by + len * 0.20f);
                // 往左下的那支
                float dx = cx - len * 0.12f, dy = cy + len * 0.12f;
                float ex = cx - len * 0.40f, ey = cy + len * 0.40f;
                g.DrawLine(pen, dx, dy, ex, ey);
                g.DrawLine(pen, ex, ey, ex + len * 0.20f, ey);
                g.DrawLine(pen, ex, ey, ex, ey - len * 0.20f);
            }
        }
    }

    public class ImagePage : PageBase
    {        RoundCard historyCard, card, inputBox;
        HistoryList history;
        RefStrip refs;
        PromptEdit prompt;
        Label ph, status;
        GradientButton btnGen, btnCfg, btnMulti, btnDlSel, btnDelSel;
        ZoomIconButton btnZoom;
        Spinner spin;
        SizePicker sizePicker;
        QualityPicker qualityPicker;
        ModelPicker modelPicker;
        int tasks;                 // 正在生成的并行任务数
        bool lastOk, lastFail;

        // 当前页面实例：历史缩略图/大图的右键「使用参考图」要靠它把图塞回参考图区
        public static ImagePage Current;

        public bool AddRefFile(string path)
        {
            try { return refs != null && refs.AddFile(path); }
            catch { return false; }
        }

        // 历史缩略图拖到参考图区/提示词框松手：按屏幕坐标判断落点，直接加成参考图
        public bool DropRefAtScreen(Point screen, string file)
        {
            if (refs == null || card == null || inputBox == null) return false;
            Rectangle a = refs.RectangleToScreen(refs.ClientRectangle);
            Rectangle b = inputBox.RectangleToScreen(inputBox.ClientRectangle);
            Rectangle c = card.RectangleToScreen(card.ClientRectangle);
            if (a.Contains(screen) || b.Contains(screen) || c.Contains(screen)) return AddRefFile(file);
            return false;
        }

        public ImagePage()
        {
            Current = this;
            int x = 96, w = 880;

            Controls.Add(Lbl("生成图像", x, 40, 300, 34, Color.FromArgb(28, 33, 50), 20F, FontStyle.Bold));
            Controls.Add(Lbl("输入提示词即可生成；结果自动保存到本地历史，下次打开仍可见", x + 2, 76, 680, 20, Color.FromArgb(140, 148, 168), 9F));

            btnCfg = Btn("配置 API", x + w - 132, 42, 132, 40);
            btnCfg.Click += (s, e) => OpenApi();
            Controls.Add(btnCfg);

            historyCard = new RoundCard();
            historyCard.Location = new Point(x, 108);
            historyCard.Size = new Size(w, 330);
            historyCard.Fill = Color.FromArgb(250, 251, 254);
            historyCard.EmptyText = null;
            Controls.Add(historyCard);

            history = new HistoryList();
            history.Location = new Point(14, 14);
            history.Size = new Size(w - 28, 302);
            historyCard.Controls.Add(history);

            card = new RoundCard();
            card.Location = new Point(x, 456);
            card.Size = new Size(w, 392);
            card.AllowDrop = true;
            card.DragEnter += (s, e) =>
            {
                if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy;
            };
            card.DragDrop += (s, e) =>
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files == null) return;
                int bad = 0;
                foreach (string f in files) if (!refs.AddFile(f)) bad++;
                if (bad > 0) DesignDialog.ShowWarn("只支持 png / jpg / jpeg / bmp 图片，已跳过 " + bad + " 个不支持的文件。");
            };
            Controls.Add(card);

            refs = new RefStrip();
            refs.Location = new Point(16, 12);
            refs.Size = new Size(w - 32, 76);
            card.Controls.Add(refs);

            // 右上角「放大」：方形圆角图标按钮（一个放大镜），点开把下面这个输入框原样放大成大窗口
            btnZoom = new ZoomIconButton();
            btnZoom.Size = new Size(38, 34);
            btnZoom.Location = new Point(w - 16 - 38, 14);
            btnZoom.Click += (s, e) => PromptZoomDialog.Show(prompt);
            card.Controls.Add(btnZoom);

            // 内嵌白色圆角输入框，让提示词区域有明确边界
            inputBox = new RoundCard();
            inputBox.Fill = Color.White;
            inputBox.BorderColor = Color.FromArgb(226, 231, 242);
            inputBox.Radius = 12;
            inputBox.Location = new Point(16, 100);
            inputBox.Size = new Size(w - 32, 152);
            card.Controls.Add(inputBox);

            prompt = new PromptEdit();
            prompt.EnterSend = () => OnGenerate();
            prompt.Editor.Font = new Font("微软雅黑", 10.5F);
            prompt.Editor.BackColor = Color.White;
            prompt.Editor.ForeColor = Color.FromArgb(31, 41, 55);
            prompt.Location = new Point(14, 14);
            prompt.Size = new Size(w - 32 - 26, 152 - 28);
            prompt.Editor.TextChanged += (s, e) => { ph.Visible = (prompt.Editor.TextLength == 0 && !prompt.Editor.Focused); };
            prompt.Editor.Enter += (s, e) => { ph.Visible = false; };
            prompt.Editor.Leave += (s, e) => { ph.Visible = (prompt.Editor.TextLength == 0); };
            // 文本框里敲 @ 就从已添加的参考图里挑一张，插成胶囊；点胶囊看大图
            prompt.ChipSource = () => new List<string>(refs.Paths);
            prompt.ChipClicked = path => OpenRefPreview(path);
            // 胶囊删了只删胶囊，参考图区那张图保留（用户可能还要再 @ 一次）
            prompt.ChipDeleted = null;
            // 点着提示词框 Ctrl+V：剪切板是图片就当参考图加进来，是文字就正常粘贴文字
            prompt.PasteImage = img => refs.AddImagePath(img);
            // 上方参考图被删掉/换了顺序：文本框里对应的胶囊立刻跟着消失或改号，
            // 不允许出现"图都没了、提示词里还挂着 @图1"的错位
            refs.Changed += (s, e) => { try { prompt.SyncChipNumbers(); } catch { } };
            inputBox.Controls.Add(prompt);

            // 从上面历史区把缩略图拖下来（拖的是文件），落到参考图区、文本框、整卡片都当参考图加进去
            Action<Control> hookDrop = tgt =>
            {
                if (tgt == null) return;
                tgt.AllowDrop = true;
                tgt.DragEnter += (s2, e2) =>
                {
                    if (e2.Data.GetDataPresent(DataFormats.FileDrop)) e2.Effect = DragDropEffects.Copy;
                };
                tgt.DragDrop += (s2, e2) =>
                {
                    string[] fs = e2.Data.GetData(DataFormats.FileDrop) as string[];
                    if (fs == null) return;
                    int bad = 0;
                    foreach (string f in fs) if (!refs.AddFile(f)) bad++;
                    if (bad > 0) DesignDialog.ShowWarn("只支持 png / jpg / jpeg / bmp 图片，已跳过 " + bad + " 个不支持的文件。");
                };
            };
            // card 和 refs 在构造函数里已经各自挂过 DragDrop，这里再挂一次就会同一次放下
            // 触发两个处理器、一下加进去两张图。只给还没挂过的输入框相关控件补挂。
            hookDrop(inputBox); hookDrop(prompt); hookDrop(prompt.Editor);

            ph = new Label();
            ph.Text = "在这里输入提示词，描述你想要的画面…";
            ph.Font = new Font("微软雅黑", 10.5F);
            ph.ForeColor = Color.FromArgb(172, 179, 198);
            ph.BackColor = Color.White;
            ph.Location = new Point(14, 12);
            ph.Size = new Size(w - 32 - 44, 24);
            ph.Cursor = Cursors.IBeam;
            ph.MouseDown += (s, e) => prompt.Editor.Focus();
            inputBox.Controls.Add(ph);
            ph.BringToFront();

            spin = new Spinner();
            spin.Location = new Point(18, 326);
            card.Controls.Add(spin);

            // 模型在左、尺寸在右，两个挨着排（模型宽一点，模型名长）
            CfgImage cfgNow = Program.Config.image;
            modelPicker = new ModelPicker();
            modelPicker.Location = new Point(16, 262);
            modelPicker.Size = new Size(250, 36);
            modelPicker.Hint = "暂无可用模型";
            if (cfgNow != null)
            {
                modelPicker.SetItems(cfgNow.models);
                bool hasKey = !string.IsNullOrEmpty(cfgNow.apiKey);
                if (hasKey)
                {
                    List<string> ms = cfgNow.models;
                    string m = cfgNow.model ?? "";
                    // 记着的模型如果不在接口给的清单里，就不能显示它（那是调不通的模型）；
                    // 清单还没拉到时也不能显示旧模型名（旧名字可能在服务端已下架，一调就是 400）
                    if (ms != null && ms.Count > 0)
                    {
                        if (!ms.Contains(m)) m = ms[0];
                        modelPicker.SetValue(m, true);
                    }
                    else
                    {
                        modelPicker.SetValue("", true);
                        modelPicker.Hint = "暂无可用模型（点右侧刷新）";
                    }
                }
                else
                {
                    modelPicker.SetValue("", true);      // 没配 API 就不显示旧模型名，免得以为能直接用
                    if (cfgNow.models != null && cfgNow.models.Count > 0) modelPicker.Hint = "选模型";
                }
                if (cfgNow.models == null || cfgNow.models.Count == 0) modelPicker.Hint = "暂无可用模型";
                modelPicker.Invalidate();
            }
            modelPicker.ValueChanged += (s, e) =>
            {
                CfgImage cfg = Program.Config.image;
                if (cfg != null) { cfg.model = modelPicker.Value; Program.SaveConfig(); }
                SyncQuality();
                if (tasks == 0 && !lastOk && !lastFail) UpdateStatus();
            };
            modelPicker.RefreshAsked += (s, e) => RefreshModels();
            card.Controls.Add(modelPicker);

            sizePicker = new SizePicker();
            sizePicker.Location = new Point(276, 262);
            sizePicker.Size = new Size(210, 36);
            CfgImage cfgNow2 = Program.Config.image;
            sizePicker.Value = (cfgNow2 != null && !string.IsNullOrEmpty(cfgNow2.size)) ? cfgNow2.size : "auto";
            sizePicker.ValueChanged += (s, e) =>
            {
                CfgImage cfg = Program.Config.image;
                if (cfg != null) { cfg.size = sizePicker.Value; Program.SaveConfig(); }
                if (tasks == 0 && !lastOk && !lastFail) UpdateStatus();
            };
            card.Controls.Add(sizePicker);

            qualityPicker = new QualityPicker();
            qualityPicker.Visible = false;
            qualityPicker.Location = new Point(496, 262);
            qualityPicker.Size = new Size(156, 36);
            CfgImage cfgNowQ = Program.Config.image;
            qualityPicker.SetValue((cfgNowQ != null && !string.IsNullOrEmpty(cfgNowQ.quality)) ? cfgNowQ.quality : "2K", true);
            qualityPicker.ValueChanged += (s, e) =>
            {
                CfgImage cfg = Program.Config.image;
                if (cfg != null) { cfg.quality = qualityPicker.Value; Program.SaveConfig(); }
                if (tasks == 0 && !lastOk && !lastFail) UpdateStatus();
            };
            card.Controls.Add(qualityPicker);

            status = new Label();
            status.Text = "";
            status.Font = new Font("微软雅黑", 9F);
            status.ForeColor = Color.FromArgb(140, 148, 168);
            status.BackColor = Color.FromArgb(246, 248, 252);
            status.Location = new Point(50, 320);
            status.Size = new Size(w - 220, 38);
            status.TextAlign = ContentAlignment.MiddleLeft;
            card.Controls.Add(status);

            btnGen = PrimaryBtn("生成图像", w - 152, 332, 136, 44);
            btnGen.Click += (s, e) => OnGenerate();
            card.Controls.Add(btnGen);

            // 多选模式：批量下载 / 批量删除（删除会连同本地文件一起删，删前二次确认）
            btnMulti = Btn("多选", 0, 0, 84, 40);
            btnMulti.Font = new Font("微软雅黑", 9.5F);
            btnMulti.Click += (s, e) => SetSelectMode(!history.SelectMode);
            Controls.Add(btnMulti);

            btnDlSel = Btn("下载所选", 0, 0, 116, 40);
            btnDlSel.Font = new Font("微软雅黑", 9.5F);
            btnDlSel.Visible = false;
            btnDlSel.Enabled = false;
            btnDlSel.Click += (s, e) => DownloadSelected();
            Controls.Add(btnDlSel);

            btnDelSel = Btn("删除所选", 0, 0, 116, 40);
            btnDelSel.Font = new Font("微软雅黑", 9.5F);
            btnDelSel.Visible = false;
            btnDelSel.Enabled = false;
            btnDelSel.Click += (s, e) => DeleteSelected();
            Controls.Add(btnDelSel);

            history.SelectionChanged += (s, e) => UpdateSelBar();

            history.SetItems(HistoryStore.Load());

            CfgImage c = Program.Config.image;
            if (c != null && !string.IsNullOrEmpty(c.apiKey))
            {
                status.ForeColor = Color.FromArgb(96, 168, 120);
                status.Text = "API 已配置：" + ShownModel(c) + " · 尺寸 " + (sizePicker != null ? SizePicker.TextOf(sizePicker.Value) : "自适应");
            }
            else
            {
                status.ForeColor = Color.FromArgb(140, 148, 168);
                status.Text = "尚未配置 API";
            }
            SyncQuality();
            ApplyLayout();
            // 有 Key 但还没模型清单：进页面就自己问一次接口（构造期还没句柄，等句柄建好再排队）
            if (c != null && !string.IsNullOrEmpty(c.apiKey) && (c.models == null || c.models.Count == 0))
            {
                if (IsHandleCreated) BeginInvoke(new Action(RefreshModels));
                else HandleCreated += (s2, e2) => BeginInvoke(new Action(RefreshModels));
            }
        }

        // 质量下拉只对支持分档的模型出现；出现就把当前值写回配置，生成时才能真正发出去
        void SyncQuality()
        {
            if (qualityPicker == null) return;
            string m = modelPicker != null ? modelPicker.Value : "";
            if (m.Length == 0 && Program.Config.image != null) m = Program.Config.image.model;
            bool locked;
            bool ok = QualityPicker.AppliesTo(m, out locked);
            qualityPicker.Locked = locked;
            if (ok && locked) qualityPicker.SetValue(QualityPicker.FixedQuality(m), true);
            qualityPicker.Visible = ok;
            CfgImage cfg = Program.Config.image;
            if (cfg != null) { cfg.quality = ok ? qualityPicker.Value : ""; Program.SaveConfig(); }
            qualityPicker.Invalidate();
        }

        // 窗口变化时只把“容器”撑大（卡片/历史区/输入框），字号与缩略图尺寸保持不变
        void ApplyLayout()
        {
            if (historyCard == null || card == null) return;
            int padX = 96;
            int w = Math.Max(600, Width - padX * 2);
            // 窗口变高时输入卡片也适度变高，让提示词框能容纳更多行；窗口变矮时卡片跟着压缩，避免超出页面
            int cardH = 392;
            if (Height > 808) cardH += (int)((Height - 808) * 0.24);
            else if (Height < 808) cardH = Math.Max(300, Height - 338);
            int cardY = Math.Max(400, Height - 12 - cardH);
            if (cardY + cardH > Height - 12) cardY = Math.Max(326, Height - 12 - cardH);
            int histH = Math.Max(180, cardY - 18 - 108);

            historyCard.Bounds = new Rectangle(padX, 108, w, histH);
            card.Bounds = new Rectangle(padX, cardY, w, cardH);
            if (history != null) history.Bounds = new Rectangle(14, 14, w - 28, histH - 28);
            if (refs != null) refs.Bounds = new Rectangle(16, 12, w - 32 - 90, 76);
            if (btnZoom != null) btnZoom.Location = new Point(w - 16 - 38, 14);
            int boxH = Math.Max(140, cardH - 240);
            if (inputBox != null) inputBox.Bounds = new Rectangle(16, 100, w - 32, boxH);
            if (prompt != null) prompt.Bounds = new Rectangle(14, 14, w - 32 - 26, boxH - 28);
            if (ph != null) ph.Bounds = new Rectangle(14, 12, w - 32 - 44, 24);
            if (modelPicker != null) modelPicker.Bounds = new Rectangle(16, 100 + boxH + 10, 250, 36);
            if (sizePicker != null) sizePicker.Bounds = new Rectangle(276, 100 + boxH + 10, 210, 36);
            if (qualityPicker != null) qualityPicker.Bounds = new Rectangle(496, 100 + boxH + 10, 176, 36);
            if (spin != null) spin.Location = new Point(18, cardH - 66);
            if (btnGen != null) btnGen.Location = new Point(w - 152, cardH - 60);
            if (btnCfg != null) btnCfg.Location = new Point(padX + w - 132, 32);

            // 右上角：配置 API 在最右，紧挨着它往左依次是多选 / 下载所选 / 删除所选（10px 间距）
            int topX = padX + w - 132;
            if (btnMulti != null) { topX -= 10 + 84; btnMulti.Location = new Point(topX, 32); btnMulti.Size = new Size(84, 40); }
            if (btnDlSel != null)
            {
                btnDlSel.Visible = history != null && history.SelectMode;
                if (btnDlSel.Visible) { topX -= 10 + 116; btnDlSel.Location = new Point(topX, 32); btnDlSel.Size = new Size(116, 40); }
            }
            if (btnDelSel != null)
            {
                btnDelSel.Visible = history != null && history.SelectMode;
                if (btnDelSel.Visible) { topX -= 10 + 116; btnDelSel.Location = new Point(topX, 32); btnDelSel.Size = new Size(116, 40); }
            }
            if (status != null) status.Bounds = new Rectangle(50, cardH - 72, Math.Max(180, w - 50 - 152 - 24), 38);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            ApplyLayout();
        }

        // 多选模式开关
        void SetSelectMode(bool on)
        {
            history.SetSelectMode(on);
            if (btnMulti != null) btnMulti.Text = on ? "完成" : "多选";
            ApplyLayout();
            UpdateSelBar();
        }

        void UpdateSelBar()
        {
            int n = history == null ? 0 : history.SelectedCount;
            bool on = history != null && history.SelectMode;
            if (btnDlSel != null)
            {
                btnDlSel.Text = n > 0 ? "下载所选 (" + n + ")" : "下载所选";
                btnDlSel.Enabled = n > 0;
                btnDlSel.Invalidate();
            }
            if (btnDelSel != null)
            {
                btnDelSel.Text = n > 0 ? "删除所选 (" + n + ")" : "删除所选";
                btnDelSel.Enabled = n > 0;
                btnDelSel.Invalidate();
            }
            if (on)
            {
                status.ForeColor = Color.FromArgb(120, 132, 160);
                status.Text = n > 0
                    ? "已选中 " + n + " 张 · 可批量下载或删除"
                    : "点缩略图左上角勾选要处理的图片";
            }
            else UpdateStatus();
        }

        void DownloadSelected()
        {
            List<GenRecord> recs = history.SelectedRecords;
            if (recs.Count == 0) return;
            int ok = 0, bad = 0;
            string dir = "";
            foreach (GenRecord r in recs)
            {
                if (r.Failed) { bad++; continue; }
                try { dir = Path.GetDirectoryName(ImageViewDialog.DownloadToDefault(r.FilePath)); ok++; }
                catch { bad++; }
            }
            status.ForeColor = Color.FromArgb(96, 168, 120);
            status.Text = "已下载 " + ok + " 张到「" + (string.IsNullOrEmpty(dir) ? "下载" : dir) + "」" + (bad > 0 ? "，失败 " + bad + " 张" : "");
        }

        void DeleteSelected()
        {
            int n = history.SelectedCount;
            if (n == 0) return;
            DesignDialog.ShowConfirm("确定删除选中的 " + n + " 张图片吗？\r\n本地文件会一起删除，删除后无法恢复。", "删除确认", () =>
            {
                int del = history.DeleteSelected();
                UpdateSelBar();
                status.ForeColor = Color.FromArgb(140, 148, 168);
                status.Text = "已删除 " + del + " 张及其本地文件";
                if (history.SelectedCount == 0) ApplyLayout();
            });
        }

        static string ModelName(CfgImage c)
        {
            string m = c.model;
            if (string.IsNullOrEmpty(m)) m = "默认模型";
            return m;
        }

        // 状态栏显示“当前下拉框里真正选中的模型”，而不是配置文件里的旧名字
        string ShownModel(CfgImage c)
        {
            string m = modelPicker != null ? modelPicker.Value : null;
            if (string.IsNullOrEmpty(m)) m = ModelName(c);
            if (string.IsNullOrEmpty(m)) m = "未选模型";
            return m;
        }

        void OpenRefPreview(string path)
        {
            // 从胶囊打开的大图只要纯图，不要右侧那栏详细信息
            try { if (File.Exists(path)) ImageViewDialog.Show(path); }
            catch { }
        }

        // 从 API 的 /models 端点拉可用模型，只留生图模型；拉不到就保留上次的清单
        public void RefreshModels()
        {
            CfgImage c = Program.Config.image;
            if (c == null || string.IsNullOrEmpty(c.apiKey))
            {
                DesignDialog.ShowWarn("先点“配置 API”填好地址和 Key，才能从接口识别可用模型。");
                return;
            }
            string baseUrl = c.baseUrl, key = c.apiKey;
            status.ForeColor = Color.FromArgb(120, 132, 160);
            status.Text = "正在从 API 获取可用模型清单…";
            ThreadPool.QueueUserWorkItem(_ =>
            {
                List<string> list = null;
                string err = null;
                // 中转站偶发连不上是常态，一轮就放弃会让用户看到"有概率取不到清单"，
                // 所以连拉三次（间隔递增），能拿到就拿到，拿不到才报错。
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    try
                    {
                        list = ImageApi.ListModels(baseUrl, key);
                        if (list != null && list.Count > 0) { err = null; break; }
                        err = "接口没有返回任何生图模型";
                    }
                    catch (Exception ex)
                    {
                        list = null;
                        err = ex.Message;
                    }
                    try { System.Threading.Thread.Sleep(600 * (attempt + 1)); }
                    catch { }
                }
                try
                {
                    BeginInvoke(new Action(() =>
                    {
                        if (list == null || list.Count == 0)
                        {
                            CfgImage cfgE = Program.Config.image;
                            if (cfgE != null && cfgE.models != null && cfgE.models.Count > 0)
                            {
                                // 上次成功拉到过清单：这次只是网络抖动没拉到，就继续用缓存的那份，
                                // 别把用户本来能用的模型清空。
                                string keepC = modelPicker.Value;
                                modelPicker.SetItems(cfgE.models);
                                if (!cfgE.models.Contains(keepC)) keepC = cfgE.models[0];
                                modelPicker.SetValue(keepC, true);
                                status.ForeColor = Color.FromArgb(220, 88, 88);
                                status.Text = "这次没连上，仍在用上次拉到的 " + cfgE.models.Count + " 个模型（"
                                             + err + "）· 点模型框右侧箭头可重试";
                                return;
                            }
                            // 从没拉到过清单：不能拿配置里记着的旧模型名去调（旧名字很可能已下架，
                            // 发出去就是 HTTP 400 model not register）
                            modelPicker.SetItems(null);
                            modelPicker.SetValue("", true);
                            modelPicker.Hint = "暂无可用模型（点右侧刷新）";
                            modelPicker.Invalidate();
                            if (cfgE != null) cfgE.model = "";
                            status.ForeColor = Color.FromArgb(220, 88, 88);
                            status.Text = "没取到模型清单" + (err == null ? "：接口没有返回任何生图模型" : "：" + err)
                                        + " · 点模型框右侧箭头重试";
                            return;
                        }
                        CfgImage cfg = Program.Config.image;
                        if (cfg != null) { cfg.models = list; Program.SaveConfig(); }
                        string keep = modelPicker.Value;
                        modelPicker.SetItems(list);
                        if (!list.Contains(keep)) modelPicker.SetValue(list[0], true);
                        else modelPicker.SetValue(keep, true);
                        if (cfg != null) { cfg.model = modelPicker.Value; Program.SaveConfig(); }
                        SyncQuality();
                        status.ForeColor = Color.FromArgb(96, 168, 120);
                        status.Text = "已识别 " + list.Count + " 个可用模型 · 当前：" + modelPicker.Value;
                    }));
                }
                catch { }
            });
        }

        void OpenApi()
        {
            Form owner = FindForm();
            ApiConfigDialog d = new ApiConfigDialog();
            Ui.OpenDialog(owner, d, () =>
            {
                CfgImage c = Program.Config.image;
                if (c != null && !string.IsNullOrEmpty(c.apiKey))
                {
                    status.ForeColor = Color.FromArgb(96, 168, 120);
                    status.Text = "API 已配置：" + ShownModel(c);
                    RefreshModels();          // 地址/Key 一改就重新问接口有哪些模型能用
                }
                else
                {
                    status.ForeColor = Color.FromArgb(140, 148, 168);
                    status.Text = "尚未配置 API";
                }
                SyncQuality();
            });
        }

        void OnGenerate()
        {
            CfgImage c = Program.Config.image;
            if (c == null || string.IsNullOrEmpty(c.apiKey))
            {
                DesignDialog.ShowWarn("请先点击右上角“配置 API”，填写接口地址与 API Key。");
                OpenApi();
                return;
            }
            string p = prompt.Editor.Text.Trim();
            if (p.Length == 0)
            {
                DesignDialog.ShowWarn("请先输入提示词。");
                prompt.Editor.Focus();
                return;
            }

            // 只用下拉里真实存在的模型名：绝不能拿配置里记着的旧名字去调（那多半是服务端已下架的名字）
            string baseUrl = c.baseUrl, key = c.apiKey, model = modelPicker != null ? modelPicker.Value : "";
            if (string.IsNullOrEmpty(model))
            {
                DesignDialog.ShowWarn("还没拿到可用模型清单，不能拿旧模型名硬调（会直接返回 400）。点模型框右侧的箭头重试一次，拉到清单后再生成。");
                RefreshModels();
                return;
            }
            string size = sizePicker != null ? sizePicker.Value : "auto";
            // 质量只有该模型支持、下拉框真的显示时才参与请求；没显示的模型绝不偷偷传
            string quality = (qualityPicker != null && qualityPicker.Visible) ? qualityPicker.Value : "";

            // @ 引用：把文本里的"图N"记号按出现顺序对上真正的参考图，提示词里也写清编号对应关系，
            // 这样模型拿到的第 1 张图就确实是 @图N 指的那张，不会张冠李戴。
            List<string> ordered = new List<string>();
            StringBuilder map = new StringBuilder();
            string raw = prompt.Editor.Text;
            int idx = 0, used = 0;
            while (true)
            {
                idx = raw.IndexOf("@图", idx, StringComparison.Ordinal);
                if (idx < 0) break;
                int d = idx + 2, e = d;
                while (e < raw.Length && char.IsDigit(raw[e])) e++;
                int no = -1;
                try { if (e > d) no = int.Parse(raw.Substring(d, e - d)); } catch { }
                string path;
                if (no > 0 && prompt.ChipMap.TryGetValue(no, out path) && File.Exists(path))
                {
                    ordered.Add(path);
                    used++;
                    map.Append("@图").Append(no).Append(" = 第").Append(used).Append("张输入参考图；");
                }
                idx = e > idx ? e : idx + 2;
            }
            foreach (string f in refs.Paths)
                if (!ordered.Contains(f)) ordered.Add(f);

            string text = raw;
            if (map.Length > 0)
                text = "【参考图编号对应】" + map.ToString()
                     + "请严格按编号引用对应参考图：画面里的人物、脸、服装、道具必须与被引用的那张图一致，不要混用。\n\n" + raw;

            List<string> refFiles = ordered;

            PendingItem tile = history.AddPending(p);
            tasks++;
            ph.Visible = false;
            spin.Start();
            lastFail = false;
            UpdateStatus();

            // 任务已经真正排进去了，输入的东西就归这次请求所有：提示词和参考图一起清空，
            // 免得下一张图还带着上一张的参考图（要复用得自己重新加回来）
            prompt.Editor.Text = "";
            prompt.ChipMap.Clear();
            prompt.LayoutChips();
            refs.Clear();

            ThreadPool.QueueUserWorkItem(_ =>
            {
                byte[] data = null;
                string err = null;
                try { data = ImageApi.Generate(baseUrl, key, model, text, refFiles, size, quality); }
                catch (Exception ex) { err = ex.Message; }
                try { BeginInvoke(new Action(() => Finish(tile, data, err, p, refFiles, model, quality, text, size))); }
                catch { }
            });
        }

        // 失败记录里的「重新生成」：用这条记录当时实际发出去的提示词 + 参考图 + 模型/质量/尺寸
        // 原封不动再发一次，不读当前输入框，也不清空用户正在写的东西。
        public void Regenerate(GenRecord rec)
        {
            if (rec == null || !rec.Failed) return;
            CfgImage c = Program.Config != null ? Program.Config.image : null;
            if (c == null || string.IsNullOrEmpty(c.apiKey))
            {
                DesignDialog.ShowWarn("请先点击右上角“配置 API”，填写接口地址与 API Key。");
                OpenApi();
                return;
            }
            string user = (rec.Prompt ?? "").Trim();
            if (user.Length == 0)
            {
                DesignDialog.ShowWarn("这条记录没有保存下提示词，无法重新生成。");
                return;
            }
            List<string> refFiles = new List<string>();
            if (rec.Refs != null)
                foreach (string f in rec.Refs)
                    if (!string.IsNullOrEmpty(f) && File.Exists(f)) refFiles.Add(f);

            string sent = rec.FullPrompt;
            if (string.IsNullOrEmpty(sent)) sent = BuildSentText(user, refFiles);

            string model = !string.IsNullOrEmpty(rec.Model) ? rec.Model : (modelPicker != null ? modelPicker.Value : "");
            if (string.IsNullOrEmpty(model)) { DesignDialog.ShowWarn("没有可用的模型名，无法重新生成。"); return; }
            string quality = rec.Quality ?? "";
            string size = !string.IsNullOrEmpty(rec.Size) ? rec.Size : (sizePicker != null ? sizePicker.Value : "auto");

            PendingItem tile = history.AddPending(user);
            tasks++;
            ph.Visible = false;
            spin.Start();
            lastFail = false;
            UpdateStatus();

            ThreadPool.QueueUserWorkItem(_ =>
            {
                byte[] data = null;
                string err = null;
                try { data = ImageApi.Generate(c.baseUrl, c.apiKey, model, sent, refFiles, size, quality); }
                catch (Exception ex) { err = ex.Message; }
                try { BeginInvoke(new Action(() => Finish(tile, data, err, user, refFiles, model, quality, sent, size))); }
                catch { }
            });
        }

        // 只有失败记录里没有存"实际发出去的完整提示词"（老版本留下的记录）时才重算：
        // 按 @图N 的出现顺序依次对上参考图，生成同样格式的编号对应说明。
        static string BuildSentText(string user, List<string> refs)
        {
            if (refs == null || refs.Count == 0) return user;
            StringBuilder map = new StringBuilder();
            List<int> seen = new List<int>();
            int used = 0, idx = 0;
            while (true)
            {
                idx = user.IndexOf("@图", idx, StringComparison.Ordinal);
                if (idx < 0) break;
                int d = idx + 2, e = d;
                while (e < user.Length && char.IsDigit(user[e])) e++;
                int no = -1;
                try { if (e > d) no = int.Parse(user.Substring(d, e - d)); } catch { }
                if (no > 0 && used < refs.Count && !seen.Contains(no))
                {
                    seen.Add(no);
                    used++;
                    map.Append("@图").Append(no).Append(" = 第").Append(used).Append("张输入参考图；");
                }
                idx = e > idx ? e : idx + 2;
            }
            if (map.Length == 0) return user;
            return "【参考图编号对应】" + map.ToString()
                 + "请严格按编号引用对应参考图：画面里的人物、脸、服装、道具必须与被引用的那张图一致，不要混用。\n\n" + user;
        }

        public bool PromptHasFocus
        {
            get { return prompt != null && prompt.Editor != null && prompt.Editor.Focused; }
        }

        // 大图/失败原因窗口里的「使用提示词」：把那条记录的提示词直接填回这个输入框
        public static Action<string> UsePromptHook;

        public void UsePromptText(string text)
        {
            if (prompt == null || prompt.Editor == null) return;
            if (text == null) text = "";
            // 提示词里的 @图N 只要当前参考图区有对应序号，就重新挂上映射，胶囊照旧显示（不然会变成纯文字）
            try
            {
                List<string> ps = refs != null ? new List<string>(refs.Paths) : new List<string>();
                int i = 0;
                while (true)
                {
                    i = text.IndexOf("@图", i, StringComparison.Ordinal);
                    if (i < 0) break;
                    int d = i + 2, e = d;
                    while (e < text.Length && char.IsDigit(text[e])) e++;
                    if (e > d)
                    {
                        int no = -1;
                        try { no = int.Parse(text.Substring(d, e - d)); } catch { }
                        if (no > 0 && no <= ps.Count) prompt.ChipMap[no] = ps[no - 1];
                    }
                    i = e > i ? e : i + 2;
                }
            }
            catch { }
            prompt.Editor.Text = text;
            prompt.Editor.SelectionStart = prompt.Editor.TextLength;
            prompt.LayoutChips();
            try { prompt.Editor.Focus(); } catch { }
        }

        // 从剪贴板粘贴图片到参考图（Ctrl+V 时由主窗体转发过来）
        public bool PasteClipboardImage()
        {
            if (!Visible) return false;
            try
            {
                if (!Clipboard.ContainsImage()) return false;
                Image img = Clipboard.GetImage();
                if (img == null) return false;
                bool ok;
                using (img) ok = refs.AddImage(img);
                if (ok)
                {
                    status.ForeColor = Color.FromArgb(96, 168, 120);
                    if (tasks == 0) status.Text = "已粘贴剪贴板图片作为参考图（共 " + refs.Count + " 张）";
                }
                return ok;
            }
            catch { return false; }
        }

        void UpdateStatus()
        {
            if (tasks > 0)
            {
                status.ForeColor = Color.FromArgb(120, 132, 160);
                status.Text = "正在生成 " + tasks + " 个任务 · 可继续点「生成图像」接着排队";
                return;
            }
            if (lastFail)
            {
                status.ForeColor = Color.FromArgb(220, 88, 88);
                status.Text = "生成失败 · 点红色缩略图看原因";
                return;
            }
            if (lastOk)
            {
                status.ForeColor = Color.FromArgb(96, 168, 120);
                status.Text = "生成完成 · 已保存到本地历史";
                return;
            }
            CfgImage c = Program.Config.image;
            if (c != null && !string.IsNullOrEmpty(c.apiKey))
            {
                status.ForeColor = Color.FromArgb(96, 168, 120);
                status.Text = "API 已配置：" + ShownModel(c) + " · 尺寸 " + (sizePicker != null ? SizePicker.TextOf(sizePicker.Value) : "自适应");
            }
            else
            {
                status.ForeColor = Color.FromArgb(140, 148, 168);
                status.Text = "尚未配置 API";
            }
        }

        void Finish(PendingItem tile, byte[] data, string err, string promptText, List<string> refFiles, string model, string quality, string sentText, string size)
        {
            if (IsDisposed) return;
            tasks--;
            if (tasks < 0) tasks = 0;
            if (tasks == 0) spin.Stop();

            if (err != null || data == null)
            {
                string reason = (string.IsNullOrEmpty(err) ? "未知错误：接口没有返回任何数据" : err);
                history.CompletePending(tile, HistoryStore.SaveFail(promptText, reason, model, quality, refFiles, sentText, size));
                lastFail = true;
                lastOk = false;
                // 模型名失效（400 model not register）几乎都是"清单过期"：顺手重拉一次清单，
                // 让用户直接选到一个服务商现在确实认的模型，而不是反复失败
                string low = reason.ToLower();
                if (low.Contains("not register") || low.Contains("model_not") || low.Contains("invalid model") || low.Contains("模型"))
                {
                    try { RefreshModels(); } catch { }
                }
            }
            else
            {
                try
                {
                    using (MemoryStream ms = new MemoryStream(data))
                    using (Image src = Image.FromStream(ms)) { }
                    history.CompletePending(tile, HistoryStore.SaveImage(data, promptText, refFiles, model, quality));
                    lastOk = true;
                    lastFail = false;
                }
                catch (Exception ex)
                {
                    history.CompletePending(tile, HistoryStore.SaveFail(promptText, "返回的数据不是有效图片：" + ex.Message, model, quality, refFiles, sentText, size));
                    lastFail = true;
                    lastOk = false;
                }
            }
            UpdateStatus();
        }
    }


    // 纯 HTTP 实现（无第三方依赖）：OpenAI 兼容 /images/*，或 Google Gemini generateContent
    public static class ImageApi
    {
        // 10 分钟：gpt-image-2-vip 这类 4K 型号 + 多张参考图，中转站排队加生成常常 5 分钟以上；
        // 之前 5 分钟会在服务端还在生成时就先判超时，白白丢掉已经扣费的一次请求。
        // 不翻墙也能用：请求默认直连（req.Proxy=null），只有连接层确实失败才回退系统代理。
        const int TimeoutMs = 600000;

        public static byte[] Generate(string baseUrl, string apiKey, string model, string prompt, List<string> refs)
        {
            return Generate(baseUrl, apiKey, model, prompt, refs, null);
        }

        public static byte[] Generate(string baseUrl, string apiKey, string model, string prompt, List<string> refs, string size)
        {
            return Generate(baseUrl, apiKey, model, prompt, refs, size, null);
        }

        public static byte[] Generate(string baseUrl, string apiKey, string model, string prompt, List<string> refs, string size, string quality)
        {
            string b = (baseUrl ?? "").Trim().TrimEnd('/');
            if (b.Length == 0) throw new Exception("API 地址为空，请先在“配置 API”里填写。");
            // 绝不拿硬编码的旧模型名去发请求：那正是"HTTP 400 model not register"的来源
            if (string.IsNullOrEmpty(model)) throw new Exception("还没选好模型：请先点模型框右侧箭头刷新清单，选一个再生成。");
            size = (size ?? "").Trim();
            if (size == "auto") size = null;      // 自适应＝不指定，由服务端按提示词自行决定
            string q = (quality ?? "").Trim().ToUpperInvariant();
            if (q != "1K" && q != "2K" && q != "4K") q = null;
            // 名字里锁死画质的型号（…-2-2K-cl / …-2-4K-cl / …-pro-4K-vip）：即使配置里残留别的档位，也按型号来
            string fixedQ = QualityPicker.FixedQuality(model);
            if (fixedQ.Length > 0) q = fixedQ;
            // 只吃像素的型号必须带档位（下拉框默认 2K）；万一配置没值，也别退化成比例串
            if (q == null && IsGptPixelOnly(model)) q = "2K";
            if (IsGemini(b)) return GeminiCall(b, apiKey, model, prompt, refs, size);
            bool banana = IsBanana(model);
            // grsai 的香蕉和 GPT Image 两族都在 POST /v1/api/generate 上（官方文档同一个端点），
            // 只是参数不同：香蕉发比例 + imageSize，gpt-image 发像素值的 aspectRatio + quality。
            if (IsGrsai(b) && banana) return GrsaiCall(b, apiKey, model, prompt, refs, size, q, "banana");
            if (IsGrsai(b) && IsGptImage(model)) return GrsaiCall(b, apiKey, model, prompt, refs, size, q, "gpt");
            // 其它中转站：质量档位换算成像素尺寸，走标准 /images/*（不传任何比例字段）
            string sendSize = size;
            if (q != null && !banana) sendSize = SizeForQuality(size, q);
            try { return OpenAiCall(b, apiKey, model, prompt, refs, sendSize); }
            catch (Exception ex)
            {
                // 别的中转站：/models 有、/images/* 却回 model not register，也按 grsai 的协议再试一次
                if (banana && ex.Message.IndexOf("not register", StringComparison.OrdinalIgnoreCase) >= 0)
                    return GrsaiCall(b, apiKey, model, prompt, refs, size, q, "banana");
                throw;
            }
        }

        static int EdgeOf(string q)
        {
            if (q == "4K") return 4096;
            if (q == "2K") return 2048;
            return 1024;
        }

        static bool IsGptImage(string model)
        {
            return (model ?? "").ToLowerInvariant().Contains("gpt-image");
        }

        // 只认像素尺寸的 5 个型号（文档：不支持比例格式）
        static bool IsGptPixelOnly(string model)
        {
            string low = (model ?? "").Trim().ToLowerInvariant();
            return low == "gpt-image-2-vip" || low == "gpt-image-2.5-flare" || low == "gpt-image-2.5-sunburst"
                || low == "gpt-image-2.6-flare" || low == "gpt-image-2.6-sunburst";
        }

        // 官方文档 gpt-image 系列的像素参考表：比例 → 1K/2K/4K 的像素尺寸
        // （vip/flare/sunburst 只收像素、不收 "3:4" 这种比例串）
        static string GptPixel(string ar, string q)
        {
            int k = q == "4K" ? 2 : (q == "2K" ? 1 : 0);
            string[] row;
            switch (ar)
            {
                case "16:9": row = new string[] { "1280x720", "2048x1152", "3840x2160" }; break;
                case "9:16": row = new string[] { "720x1280", "1152x2048", "2160x3840" }; break;
                case "4:3": row = new string[] { "1152x864", "2304x1728", "3264x2448" }; break;
                case "3:4": row = new string[] { "864x1152", "1728x2304", "2448x3264" }; break;
                case "3:2": row = new string[] { "1536x1024", "2048x1360", "3504x2336" }; break;
                case "2:3": row = new string[] { "1024x1536", "1360x2048", "2336x3504" }; break;
                case "5:4": row = new string[] { "1120x896", "2240x1792", "3200x2560" }; break;
                case "4:5": row = new string[] { "896x1120", "1792x2240", "2560x3200" }; break;
                case "21:9": row = new string[] { "1456x624", "2912x1248", "3840x1648" }; break;
                case "9:21": row = new string[] { "624x1456", "1248x2912", "1648x3840" }; break;
                case "1:2": row = new string[] { "768x1536", "1536x3072", "1920x3840" }; break;
                case "2:1": row = new string[] { "1536x768", "3072x1536", "3840x1920" }; break;
                case "1:3": row = new string[] { "688x2048", "688x2048", "1280x3840" }; break;
                case "3:1": row = new string[] { "2048x688", "2048x688", "3840x1280" }; break;
                default: row = new string[] { "1024x1024", "2048x2048", "2880x2880" }; break;   // 含 auto / 1:1
            }
            return row[k];
        }

        // 质量档位 → 像素尺寸（长边落在 1024/2048/4096，保持原宽高比，8 的倍数）
        static string SizeForQuality(string size, string q)
        {
            int edge = EdgeOf(q);
            int w = 0, h = 0;
            if (!string.IsNullOrEmpty(size))
            {
                int x = size.IndexOf('x');
                if (x > 0) { try { w = int.Parse(size.Substring(0, x)); h = int.Parse(size.Substring(x + 1)); } catch { } }
            }
            if (w <= 0 || h <= 0) return edge + "x" + edge;
            double s = (double)edge / Math.Max(w, h);
            w = (int)Math.Round(w * s / 8.0) * 8;
            h = (int)Math.Round(h * s / 8.0) * 8;
            if (w < 64) w = 64;
            if (h < 64) h = 64;
            return w + "x" + h;
        }

        static bool IsGrsai(string b)
        {
            string s = b.ToLowerInvariant();
            return s.Contains("grsai");
        }

        static bool IsBanana(string model)
        {
            string s = (model ?? "").ToLowerInvariant();
            return s.Contains("banana");
        }

        static bool IsGemini(string b)
        {
            string s = b.ToLower();
            return s.Contains("generativelanguage") || s.Contains("googleapis.com") || s.Contains("gemini");
        }

        // 拉接口自己的模型清单：只留生图模型。能用哪些由 API 说了算，绝不自己编模型名。
        public static List<string> ListModels(string baseUrl, string apiKey)
        {
            string b = (baseUrl ?? "").Trim().TrimEnd('/');
            if (b.Length == 0) throw new Exception("API 地址为空。");
            string url;
            if (IsGemini(b)) url = b + "/models?key=" + Uri.EscapeDataString(apiKey ?? "");
            else
            {
                // 用户可能填到具体端点，往上退到 /v1 一级再查 /models
                string root = b;
                if (root.ToLower().EndsWith("/images/generations") || root.ToLower().EndsWith("/images/edits"))
                    root = root.Substring(0, root.LastIndexOf('/'));
                url = root + "/models";
            }
            // grsai 的模型清单在站点根下的 /v1/models（不是 /models）：
            // GrsaiRoot 已经把末尾的 /v1 去掉了，这里必须补回 /v1/models，否则就是 404 page not found。
            byte[] raw = IsGrsai(b) ? GrsaiGet(b, "/v1/models", apiKey) : Get(url, apiKey);
            string s = Encoding.UTF8.GetString(raw);
            object obj = Ser().DeserializeObject(s);
            Dictionary<string, object> root2 = obj as Dictionary<string, object>;
            if (root2 == null) throw new Exception(Short(s));
            List<string> all = new List<string>();
            object dataObj;
            if (root2.TryGetValue("data", out dataObj))
            {
                object[] arr = dataObj as object[];
                if (arr != null)
                    foreach (object o in arr)
                    {
                        Dictionary<string, object> item = o as Dictionary<string, object>;
                        if (item == null) continue;
                        string id = GetStr(item, "id");
                        if (string.IsNullOrEmpty(id)) id = GetStr(item, "name");
                        if (!string.IsNullOrEmpty(id)) all.Add(id);
                    }
            }
            // 兼容 Gemini 的 models 数组
            if (all.Count == 0)
            {
                object mo;
                if (root2.TryGetValue("models", out mo))
                {
                    object[] arr2 = mo as object[];
                    if (arr2 != null)
                        foreach (object o in arr2)
                        {
                            Dictionary<string, object> item = o as Dictionary<string, object>;
                            if (item == null) continue;
                            string id = GetStr(item, "name");
                            if (id != null && id.StartsWith("models/")) id = id.Substring(7);
                            if (!string.IsNullOrEmpty(id)) all.Add(id);
                        }
                }
            }
            List<string> img = new List<string>();
            string[] keys = { "image", "banana", "dall-e", "dalle", "gpt-image", "seedream", "flux", "imagen", "sd3", "sdxl", "stable-diffusion", "ideogram", "recraft", "midjourney", "kolors", "qwen-image", "hunyuan-image", "doubao-seedream" };
            foreach (string id in all)
            {
                string low = id.ToLower();
                foreach (string k in keys) if (low.Contains(k)) { img.Add(id); break; }
            }
            if (img.Count == 0) img = all;      // 接口没按名字区分时，至少别给空清单
            return img;
        }

        static byte[] Get(string url, string key)
        {
            Exception last = null;
            for (int i = 0; i < 3; i++)
            {
                try { return GetOnce(url, key, false); }
                catch (WebException we)
                {
                    last = we;
                    if (!ConnLevel(we.Status)) throw Wrap(we);
                    if (i < 2) Thread.Sleep(600 * (i + 1));
                }
            }
            try { return GetOnce(url, key, true); }
            catch (WebException we) { throw Wrap(we); }
        }

        static byte[] GetOnce(string url, string key, bool useProxy)
        {
            HttpWebRequest req = NewRequest(url, useProxy);
            req.Method = "GET";
            req.Timeout = 30000;
            req.ReadWriteTimeout = 30000;
            if (!string.IsNullOrEmpty(key)) req.Headers["Authorization"] = "Bearer " + key;
            using (HttpWebResponse res = (HttpWebResponse)req.GetResponse())
            using (Stream s = res.GetResponseStream())
                return ReadAll(s);
        }

        static byte[] OpenAiCall(string b, string key, string model, string prompt, List<string> refs, string size)
        {
            byte[] resp = null;
            bool edit = refs != null && refs.Count > 0;
            string url = Endpoint(b, edit);
            try
            {
                if (edit)
                {
                    Dictionary<string, string> fields = new Dictionary<string, string>();
                    fields["model"] = model;
                    fields["prompt"] = prompt;
                    fields["n"] = "1";
                    if (!string.IsNullOrEmpty(size)) fields["size"] = size;
                    resp = PostMultipart(url, key, fields, refs);
                }
                else
                {
                    string json = "{\"model\":" + J(model) + ",\"prompt\":" + J(prompt) + ",\"n\":1"
                        + (string.IsNullOrEmpty(size) ? "" : ",\"size\":" + J(size)) + "}";
                    resp = PostJson(url, key, json);
                }
            }
            catch (Exception ex)
            {
                // "model not register" 是服务商那边说"这个模型名在我这没登记"。
                // 把本次真正发出去的地址/模型/参考图数量写进原因里，用户一看就知道该核对什么
                if (ex.Message.IndexOf("not register", StringComparison.OrdinalIgnoreCase) >= 0)
                    throw new Exception(ex.Message + "\r\n\r\n[本次实际请求] POST " + url
                        + "\r\nmodel = " + model
                        + "\r\n参考图 = " + (refs == null ? 0 : refs.Count) + " 张" + (edit ? "（带参考图走的是 /images/edits）" : ""));
                throw;
            }
            return ExtractOpenAi(resp);
        }

        // ---------------- grsai Nano Banana 系列（POST /v1/api/generate） ----------------
        // 文档：body {model,prompt,images[],aspectRatio,imageSize,replyType}，返回 {status,results:[{url}]}。
        // 这条线路的模型（nano-banana-pro 等）在 /images/edits 上没有登记，所以必须走这里。
        static byte[] GrsaiCall(string b, string key, string model, string prompt, List<string> refs, string size, string quality, string family)
        {
            string urlTail = "/v1/api/generate";
            StringBuilder sb = new StringBuilder();
            sb.Append("{\"model\":").Append(J(model));
            sb.Append(",\"prompt\":").Append(J(prompt));
            sb.Append(",\"images\":[");
            int n = 0;
            if (refs != null)
            {
                foreach (string f in refs)
                {
                    string du = DataUri(f);
                    if (du == null) continue;
                    if (n++ > 0) sb.Append(',');
                    sb.Append(J(du));
                }
            }
            sb.Append("]");
            if (family == "gpt")
            {
                // GPT Image 系列（官方文档）：vip/flare/sunburst 只收像素尺寸（"3:4" 会 400）；
                // gpt-image-2 / 2.5 / 2.6 支持比例串或 1K 像素。quality 只有前一类才带。
                if (quality != null && IsGptPixelOnly(model))
                {
                    sb.Append(",\"aspectRatio\":").Append(J(GptPixel(AspectOf(size), quality)));
                    sb.Append(",\"quality\":\"auto\"");
                }
                else
                {
                    string ar = AspectOf(size);
                    if (ar.Length > 0) sb.Append(",\"aspectRatio\":").Append(J(ar));
                }
            }
            else
            {
                string ar = AspectOf(size);
                if (ar.Length > 0) sb.Append(",\"aspectRatio\":").Append(J(ar));
                string lv = (quality == "1K" || quality == "2K" || quality == "4K") ? quality : ImageLevel(size);
                if (lv.Length > 0) sb.Append(",\"imageSize\":").Append(J(lv));
            }
            // 异步：提交只拿任务号，随后短连接轮询结果。
            // 同步 json 要在一条连接上等几分钟，弱网/中转抖一下就会「操作超时 / 无法连接」，
            // 而且客户端断了服务端还在跑，重发又可能重复扣费。异步任务号让结果在服务端等我们。
            sb.Append(",\"replyType\":\"async\"}");

            byte[] resp = GrsaiPost(b, urlTail, key, sb.ToString());
            Dictionary<string, object> o = Ser().DeserializeObject(Encoding.UTF8.GetString(resp)) as Dictionary<string, object>;
            if (o == null) throw new Exception("[HTTP 200] " + Short(Encoding.UTF8.GetString(resp)));

            string status = GetStr(o, "status");
            string err = GetStr(o, "error");
            string imgUrl = GrsaiImageUrl(o);
            if (!string.IsNullOrEmpty(imgUrl)) return FetchImage(imgUrl);

            string id = GetStr(o, "id");
            if (string.IsNullOrEmpty(id))
            {
                object dataObj;
                if (o.TryGetValue("data", out dataObj))
                {
                    Dictionary<string, object> data = dataObj as Dictionary<string, object>;
                    if (data != null) id = GetStr(data, "id");
                }
            }
            if (string.IsNullOrEmpty(id))
                throw new Exception("[HTTP 200] 接口没有返回任务号（status=" + (status ?? "空") + "）：" + Short(Encoding.UTF8.GetString(resp)));
            string st0 = (status ?? "").ToLowerInvariant();
            if (st0 == "failed" || st0 == "violation")
                throw new Exception("[HTTP 200] 生成失败（status=" + status + "）" + (string.IsNullOrEmpty(err) ? "" : "：" + err) + "（任务 id=" + id + "）");

            // 20 分钟总时限：4K + 多参考图本来就慢，但每次轮询都是短请求，断了可以重连
            DateTime deadline = DateTime.Now.AddMinutes(20);
            int netFail = 0;
            while (DateTime.Now < deadline)
            {
                Thread.Sleep(2500);
                try
                {
                    resp = GrsaiGet(b, "/v1/api/result?id=" + Uri.EscapeDataString(id), key);
                    netFail = 0;
                }
                catch (Exception ex)
                {
                    // 单次查询失败不代表任务失败：任务在服务端继续跑，连续失败三次才认输
                    if (++netFail >= 3) throw new Exception("生成任务已提交（id=" + id + "），但连续查询失败：" + ex.Message);
                    continue;
                }
                o = Ser().DeserializeObject(Encoding.UTF8.GetString(resp)) as Dictionary<string, object>;
                if (o == null) continue;
                status = GetStr(o, "status");
                err = GetStr(o, "error");
                imgUrl = GrsaiImageUrl(o);
                if (!string.IsNullOrEmpty(imgUrl)) return FetchImage(imgUrl);
                string st = (status ?? "").ToLowerInvariant();
                if (st == "failed" || st == "violation")
                    throw new Exception("[HTTP 200] 生成失败（status=" + status + "）" + (string.IsNullOrEmpty(err) ? "" : "：" + err) + "（任务 id=" + id + "）");
            }
            throw new Exception("[Timeout] 生成任务已提交（id=" + id + "），20 分钟内没等到结果。可稍后到服务商后台按任务号查询。");
        }

        // grsai 双节点：国内节点直连最稳，全球节点做备用。用户填的是哪个都自动补齐另一个。
        static string[] GrsaiRootList(string b)
        {
            string root = GrsaiRoot(b);
            string host = "";
            try { host = new Uri(root).Host.ToLowerInvariant(); } catch { }
            List<string> list = new List<string>();
            if (host.Contains("grsaiapi.com"))
            {
                list.Add("https://grsai.dakka.com.cn");
                list.Add(root);
            }
            else if (host.Contains("dakka"))
            {
                list.Add(root);
                list.Add("https://grsaiapi.com");
            }
            else list.Add(root);
            return list.ToArray();
        }

        // 换节点只在「请求没被服务端判为参数错误」时进行：400 是 body 写错了，换哪个节点都一样。
        static byte[] GrsaiPost(string b, string tail, string key, string json)
        {
            Exception last = null;
            foreach (string root in GrsaiRootList(b))
            {
                try { return PostJson(root + tail, key, json); }
                catch (Exception ex)
                {
                    last = ex;
                    if (ex.Message.StartsWith("[HTTP 400")) throw;
                }
            }
            throw last;
        }

        static byte[] GrsaiGet(string b, string tail, string key)
        {
            Exception last = null;
            foreach (string root in GrsaiRootList(b))
            {
                try { return Get(root + tail, key); }
                catch (Exception ex)
                {
                    last = ex;
                    if (ex.Message.StartsWith("[HTTP 400")) throw;
                }
            }
            throw last;
        }

        static string GrsaiImageUrl(Dictionary<string, object> o)
        {
            if (o == null) return null;
            object resultsObj;
            if (o.TryGetValue("results", out resultsObj))
            {
                object[] arr = resultsObj as object[];
                if (arr != null && arr.Length > 0)
                {
                    Dictionary<string, object> it = arr[0] as Dictionary<string, object>;
                    if (it != null)
                    {
                        string u = GetStr(it, "url");
                        if (!string.IsNullOrEmpty(u)) return u;
                    }
                }
            }
            string imgUrl = GetStr(o, "url");
            return string.IsNullOrEmpty(imgUrl) ? null : imgUrl;
        }

        static byte[] FetchImage(string imgUrl)
        {
            if (imgUrl.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                int comma = imgUrl.IndexOf(',');
                if (comma < 0) throw new Exception("返回的图片数据格式不认识");
                return Convert.FromBase64String(imgUrl.Substring(comma + 1));
            }
            return Get(imgUrl, null);      // 结果图是普通 https 链接，不需要再带 key
        }

        // baseUrl 可能是 https://grsai.ai、https://grsai.ai/v1、…/v1/api/generate，统一回到站点根
        static string GrsaiRoot(string b)
        {
            string s = (b ?? "").Trim().TrimEnd('/');
            string low = s.ToLowerInvariant();
            if (low.EndsWith("/v1/api/generate")) return s.Substring(0, s.Length - "/v1/api/generate".Length);
            if (low.EndsWith("/api/generate")) return s.Substring(0, s.Length - "/api/generate".Length);
            if (low.EndsWith("/images/generations")) s = s.Substring(0, s.Length - "/images/generations".Length);
            else if (low.EndsWith("/images/edits")) s = s.Substring(0, s.Length - "/images/edits".Length);
            low = s.ToLowerInvariant().TrimEnd('/');
            if (low.EndsWith("/v1")) s = s.Substring(0, s.Length - 3);
            return s.TrimEnd('/');
        }

        static string DataUri(string path)
        {
            try
            {
                byte[] d = File.ReadAllBytes(path);
                string ext = Path.GetExtension(path).ToLowerInvariant();
                string mime = ext == ".jpg" || ext == ".jpeg" ? "image/jpeg" : (ext == ".webp" ? "image/webp" : "image/png");
                return "data:" + mime + ";base64," + Convert.ToBase64String(d);
            }
            catch { return null; }
        }

        // 尺寸下拉的 "1024x1024" → aspectRatio；自适应/没填就不传，由模型自己定
        static string AspectOf(string size)
        {
            if (string.IsNullOrEmpty(size)) return "auto";
            string low = size.ToLowerInvariant().Replace(" ", "");
            if (low == "auto") return "auto";
            int colon = low.IndexOf(':');
            if (colon > 0)
            {
                // 尺寸下拉本身给的就是 "3:4" 这种比例串：直接按比例算，别当成像素值退回 auto，
                // 否则 4K 的 3:4 会被当 1:1 发成 2880x2880。
                int a = 0, b = 0;
                try { a = int.Parse(low.Substring(0, colon)); b = int.Parse(low.Substring(colon + 1)); } catch { }
                if (a > 0 && b > 0) return RatioOf((double)a / b);
            }
            int x = low.IndexOf('x');
            int w = 0, h = 0;
            if (x > 0)
            {
                try { w = int.Parse(low.Substring(0, x)); h = int.Parse(low.Substring(x + 1)); } catch { }
            }
            if (w <= 0 || h <= 0) return "auto";
            return RatioOf((double)w / h);
        }

        // 把任意宽高比吸附到接口认的候选比例上，取最接近的一个
        static string RatioOf(double r)
        {
            string[] cand = { "1:1", "16:9", "9:16", "4:3", "3:4", "3:2", "2:3", "5:4", "4:5", "21:9", "9:21", "1:3", "3:1", "2:1", "1:2" };
            double best = double.MaxValue; string bestS = "1:1";
            foreach (string c in cand)
            {
                int ci = c.IndexOf(':');
                double cr = double.Parse(c.Substring(0, ci)) / double.Parse(c.Substring(ci + 1));
                double d = Math.Abs(cr - r);
                if (d < best) { best = d; bestS = c; }
            }
            return bestS;
        }

        // grsai 只认 1K/2K/4K 三档，按长边换算
        static string ImageLevel(string size)
        {
            if (string.IsNullOrEmpty(size)) return "";
            string low = size.ToLowerInvariant().Replace(" ", "");
            int x = low.IndexOf('x'), w = 0, h = 0;
            if (x > 0) { try { w = int.Parse(low.Substring(0, x)); h = int.Parse(low.Substring(x + 1)); } catch { } }
            int longEdge = Math.Max(w, h);
            if (longEdge <= 0) return "";
            if (longEdge <= 1400) return "1K";
            if (longEdge <= 2400) return "2K";
            return "4K";
        }

        // 把 baseUrl 变成真正的接口地址：用户可能已经填到具体端点（如 /v1/api/generate），
        // 再拼 /images/generations 就会变成 .../generate/images/generations 这种不存在的位置。
        static string Endpoint(string b, bool edit)
        {
            string low = b.ToLowerInvariant();
            if (low.EndsWith("/images/generations") || low.EndsWith("/images/edits")) return b;
            if (low.EndsWith("/generate") || low.Contains("/api/generate") || low.Contains(":generatecontent")) return b;
            return b + (edit ? "/images/edits" : "/images/generations");
        }

        static byte[] GeminiCall(string b, string key, string model, string prompt, List<string> refs, string size)
        {
            string url = b + "/models/" + Uri.EscapeDataString(model) + ":generateContent?key=" + Uri.EscapeDataString(key ?? "");
            StringBuilder sb = new StringBuilder();
            sb.Append("{\"contents\":[{\"parts\":[{\"text\":").Append(J(prompt)).Append("}");
            if (refs != null)
            {
                foreach (string f in refs)
                {
                    string mime = MimeOf(f);
                    byte[] raw = File.ReadAllBytes(f);
                    sb.Append(",{\"inline_data\":{\"mime_type\":\"").Append(mime).Append("\",\"data\":\"")
                      .Append(Convert.ToBase64String(raw)).Append("\"}}");
                }
            }
            sb.Append("]}]");
            string ratio = RatioOf(size);
            if ((model ?? "").ToLower().Contains("image"))
            {
                sb.Append(",\"generationConfig\":{\"responseModalities\":[\"TEXT\",\"IMAGE\"]");
                if (ratio.Length > 0) sb.Append(",\"imageConfig\":{\"aspectRatio\":\"").Append(ratio).Append("\"}");
                sb.Append("}");
            }
            sb.Append("}");
            byte[] resp = PostJson(url, null, sb.ToString());
            return ExtractGemini(resp);
        }

        // 把 "1536x1024" 换算成 Gemini 认的宽高比写法（如 3:2）
        static string RatioOf(string size)
        {
            if (string.IsNullOrEmpty(size)) return "";
            string[] p = size.ToLower().Split('x');
            int w, h;
            if (p.Length != 2) return "";
            try { w = int.Parse(p[0].Trim()); h = int.Parse(p[1].Trim()); }
            catch { return ""; }
            if (w <= 0 || h <= 0) return "";
            int a = w, b2 = h;
            while (b2 != 0) { int t = a % b2; a = b2; b2 = t; }
            w /= a; h /= a;
            return w + ":" + h;
        }

        static string MimeOf(string f)
        {
            string e = Path.GetExtension(f).ToLower();
            if (e == ".png") return "image/png";
            if (e == ".bmp") return "image/bmp";
            return "image/jpeg";
        }

        static byte[] ExtractOpenAi(byte[] resp)
        {
            string s = Encoding.UTF8.GetString(resp);
            object obj = Ser().DeserializeObject(s);
            Dictionary<string, object> root = obj as Dictionary<string, object>;
            if (root == null) throw new Exception(Short(s));
            object dataObj;
            if (root.TryGetValue("data", out dataObj))
            {
                object[] arr = dataObj as object[];
                if (arr != null && arr.Length > 0)
                {
                    Dictionary<string, object> item = arr[0] as Dictionary<string, object>;
                    if (item != null)
                    {
                        string b64 = GetStr(item, "b64_json");
                        if (string.IsNullOrEmpty(b64)) b64 = GetStr(item, "b64");
                        if (!string.IsNullOrEmpty(b64)) return Convert.FromBase64String(b64);
                        string url = GetStr(item, "url");
                        if (!string.IsNullOrEmpty(url)) return Download(url);
                    }
                }
            }
            throw new Exception(Short(s));
        }

        static byte[] ExtractGemini(byte[] resp)
        {
            string s = Encoding.UTF8.GetString(resp);
            object obj = Ser().DeserializeObject(s);
            Dictionary<string, object> root = obj as Dictionary<string, object>;
            if (root != null)
            {
                object candsObj;
                if (root.TryGetValue("candidates", out candsObj))
                {
                    object[] cands = candsObj as object[];
                    if (cands != null && cands.Length > 0)
                    {
                        Dictionary<string, object> cand = cands[0] as Dictionary<string, object>;
                        object contentObj;
                        if (cand != null && cand.TryGetValue("content", out contentObj))
                        {
                            Dictionary<string, object> content = contentObj as Dictionary<string, object>;
                            object partsObj;
                            if (content != null && content.TryGetValue("parts", out partsObj))
                            {
                                object[] parts = partsObj as object[];
                                if (parts != null)
                                {
                                    foreach (object po in parts)
                                    {
                                        Dictionary<string, object> part = po as Dictionary<string, object>;
                                        if (part == null) continue;
                                        object inl;
                                        if (!part.TryGetValue("inlineData", out inl)) part.TryGetValue("inline_data", out inl);
                                        Dictionary<string, object> inline = inl as Dictionary<string, object>;
                                        if (inline == null) continue;
                                        string b64 = GetStr(inline, "data");
                                        if (!string.IsNullOrEmpty(b64)) return Convert.FromBase64String(b64);
                                    }
                                }
                            }
                        }
                    }
                }
            }
            throw new Exception(Short(s));
        }

        static byte[] PostJson(string url, string key, string json)
        {
            return Send(url, key, "application/json; charset=utf-8", Encoding.UTF8.GetBytes(json));
        }

        static byte[] PostMultipart(string url, string key, Dictionary<string, string> fields, List<string> files)
        {
            string boundary = "----AiToolBoundary" + Guid.NewGuid().ToString("N");
            string fieldName = files.Count > 1 ? "image[]" : "image";
            using (MemoryStream ms = new MemoryStream())
            {
                foreach (KeyValuePair<string, string> kv in fields)
                {
                    string head = "--" + boundary + "\r\nContent-Disposition: form-data; name=\"" + kv.Key + "\"\r\n\r\n";
                    byte[] hb = Encoding.UTF8.GetBytes(head);
                    ms.Write(hb, 0, hb.Length);
                    byte[] vb = Encoding.UTF8.GetBytes(kv.Value ?? "");
                    ms.Write(vb, 0, vb.Length);
                    byte[] crlf = Encoding.UTF8.GetBytes("\r\n");
                    ms.Write(crlf, 0, crlf.Length);
                }
                foreach (string f in files)
                {
                    string name = Path.GetFileName(f);
                    string head = "--" + boundary + "\r\nContent-Disposition: form-data; name=\"" + fieldName + "\"; filename=\"" + name + "\"\r\nContent-Type: " + MimeOf(f) + "\r\n\r\n";
                    byte[] hb = Encoding.UTF8.GetBytes(head);
                    ms.Write(hb, 0, hb.Length);
                    byte[] raw = File.ReadAllBytes(f);
                    ms.Write(raw, 0, raw.Length);
                    byte[] crlf = Encoding.UTF8.GetBytes("\r\n");
                    ms.Write(crlf, 0, crlf.Length);
                }
                byte[] tail = Encoding.UTF8.GetBytes("--" + boundary + "--\r\n");
                ms.Write(tail, 0, tail.Length);
                return Send(url, key, "multipart/form-data; boundary=" + boundary, ms.ToArray());
            }
        }

        static HttpWebRequest NewRequest(string url, bool useProxy)
        {
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
            req.Timeout = TimeoutMs;
            req.ReadWriteTimeout = TimeoutMs;
            // 不复用连接：中转站/图床经常一条连接只服务一次，复用会拿到已被对方关掉的 socket
            // （WebException "基础连接已经关闭"），下载结果图时尤其常见
            req.KeepAlive = false;
            req.ServicePoint.Expect100Continue = false;
            // 代理策略：①「配置 API」里填了代理就用它（http://127.0.0.1:7890 这类本地端口）；
            // ②没填则先直连（国内中转站如 grsai 可直连）；③直连在连接层失败时才回退系统代理。
            IWebProxy p = PickProxy(useProxy);
            req.Proxy = p;
            return req;
        }

        static IWebProxy PickProxy(bool systemProxy)
        {
            string manual = "";
            try { if (Program.Config != null && Program.Config.image != null) manual = (Program.Config.image.proxy ?? "").Trim(); }
            catch { }
            if (manual.Length > 0)
            {
                try { return new WebProxy(manual); } catch { }
            }
            return systemProxy ? WebRequest.DefaultWebProxy : null;
        }

        static bool ConnLevel(WebExceptionStatus st)
        {
            return st == WebExceptionStatus.SecureChannelFailure
                || st == WebExceptionStatus.ConnectFailure
                || st == WebExceptionStatus.NameResolutionFailure
                || st == WebExceptionStatus.ProxyNameResolutionFailure
                || st == WebExceptionStatus.Timeout
                || st == WebExceptionStatus.ConnectionClosed
                || st == WebExceptionStatus.KeepAliveFailure;
        }

        // 先直连，连接层抖一下就短退避重试；仍失败再回退系统代理。
        // 只有「请求根本没发出去」的连接错误才重发 POST（不会重复扣费）；超时可能是服务端已在跑，不重发。
        static byte[] Send(string url, string key, string contentType, byte[] body)
        {
            Exception last = null;
            for (int i = 0; i < 3; i++)
            {
                try { return SendOnce(url, key, contentType, body, false); }
                catch (WebException we)
                {
                    last = we;
                    if (!ConnLevel(we.Status)) throw Wrap(we);
                    if (we.Status == WebExceptionStatus.Timeout) throw Wrap(we);
                    if (i < 2) Thread.Sleep(700 * (i + 1));
                }
            }
            try { return SendOnce(url, key, contentType, body, true); }
            catch (WebException we) { throw Wrap(we); }
        }

        static byte[] SendOnce(string url, string key, string contentType, byte[] body, bool useProxy)
        {
            HttpWebRequest req = NewRequest(url, useProxy);
            req.Method = "POST";
            req.ContentType = contentType;
            req.ContentLength = body.Length;
            if (!string.IsNullOrEmpty(key)) req.Headers["Authorization"] = "Bearer " + key;
            using (Stream st = req.GetRequestStream()) st.Write(body, 0, body.Length);
            using (HttpWebResponse res = (HttpWebResponse)req.GetResponse())
            using (Stream s = res.GetResponseStream())
                return ReadAll(s);
        }

        // 异常统一带「连接层状态 / HTTP 状态码」标签，失败原因据此精确判定，不靠猜字符串
        static Exception Wrap(WebException we)
        {
            string detail = "";
            string tag = we.Status.ToString();
            try
            {
                if (we.Response != null)
                {
                    if (we.Response is HttpWebResponse) tag = "HTTP " + (int)((HttpWebResponse)we.Response).StatusCode;
                    using (Stream es = we.Response.GetResponseStream()) detail = Encoding.UTF8.GetString(ReadAll(es));
                }
            }
            catch { }
            if (string.IsNullOrEmpty(detail)) detail = we.Message;
            return new Exception("[" + tag + "] " + Short(detail));
        }

        static byte[] Download(string url)
        {
            Exception last = null;
            for (int i = 0; i < 3; i++)
            {
                try { return DownloadOnce(url, false); }
                catch (WebException we)
                {
                    last = we;
                    if (!ConnLevel(we.Status)) throw Wrap(we);
                    if (i < 2) Thread.Sleep(600 * (i + 1));
                }
            }
            try { return DownloadOnce(url, true); }
            catch (WebException we) { throw Wrap(we); }
        }

        static byte[] DownloadOnce(string url, bool useProxy)
        {
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
            req.Timeout = 60000;
            req.ReadWriteTimeout = 60000;
            req.Proxy = useProxy ? WebRequest.DefaultWebProxy : null;
            using (HttpWebResponse res = (HttpWebResponse)req.GetResponse())
            using (Stream s = res.GetResponseStream())
                return ReadAll(s);
        }

        static byte[] ReadAll(Stream s)
        {
            if (s == null) return new byte[0];
            using (MemoryStream ms = new MemoryStream())
            {
                byte[] buf = new byte[8192];
                int n;
                while ((n = s.Read(buf, 0, buf.Length)) > 0) ms.Write(buf, 0, n);
                return ms.ToArray();
            }
        }

        static JavaScriptSerializer Ser()
        {
            JavaScriptSerializer ser = new JavaScriptSerializer();
            ser.MaxJsonLength = 64 * 1024 * 1024;
            return ser;
        }

        static string J(string s) { return Ser().Serialize(s ?? ""); }

        static string GetStr(Dictionary<string, object> d, string key)
        {
            if (d == null) return null;
            object o;
            if (!d.TryGetValue(key, out o) || o == null) return null;
            return o as string;
        }

        static string Short(string s)
        {
            if (string.IsNullOrEmpty(s)) return "接口返回为空";
            s = s.Replace("\r\n", "\n").Replace("\r", "\n");
            return s.Length > 2000 ? s.Substring(0, 2000) + " …（已截断）" : s;
        }
    }
}