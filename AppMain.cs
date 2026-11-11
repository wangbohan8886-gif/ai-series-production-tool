using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace AiAnimeTool
{
    public class MainForm : Form
    {
        Panel titleBar, sidebar, contentPanel;
        NavButton navProject, navRename, navImage;
        ProjectPage pageProject;
        RenamePage pageRename;
        ImagePage pageImage;
        TitleButton btnClose, btnMin, btnMax;
        Label verLabel;

        [DllImport("user32.dll")] static extern bool ReleaseCapture();
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);
        [DllImport("user32.dll")] static extern IntPtr LoadCursor(IntPtr inst, int id);
        [DllImport("user32.dll")] static extern IntPtr SetCursor(IntPtr cur);

        public const int Pad = 6;      // 窗口自己保留的抓边宽度：这 6px 不铺子控件，鼠标才打得中四边四角

        public MainForm()
        {
            Text = "AI系列生产工具";
            Size = new Size(Program.WinW, Program.WinH);
            MinimumSize = new Size(Program.WinW, Program.WinH);   // 最小就是默认尺寸，只能往大拉
            // 上次拖出来的尺寸永久记住：超出工作区就夹回来，小于最小值就按最小值
            try
            {
                CfgWindow cw = Program.Config != null ? Program.Config.window : null;
                if (cw != null && cw.w > 0 && cw.h > 0)
                {
                    Rectangle wa = Screen.PrimaryScreen.WorkingArea;
                    int w = Math.Max(Program.WinW, Math.Min(cw.w, wa.Width));
                    int h = Math.Max(Program.WinH, Math.Min(cw.h, wa.Height));
                    Size = new Size(w, h);
                }
            }
            catch { }
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.White;
            Font = new Font("微软雅黑", 10F);
            Opacity = 1;                 // 保持非分层：Windows 原生动画（AnimateWindow/SW_MINIMIZE）不支持分层窗口
            DoubleBuffered = true;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            BuildTitleBar();
            BuildSidebar();

            contentPanel = new Panel();
            contentPanel.Location = new Point(Pad + Program.SideW, Pad + Program.TitleH);
            contentPanel.Size = new Size(Program.WinW - Program.SideW - Pad * 2, Program.WinH - Program.TitleH - Pad * 2);
            contentPanel.BackColor = Color.White;
            pageProject = new ProjectPage();
            pageRename = new RenamePage();
            pageImage = new ImagePage();
            contentPanel.Controls.Add(pageProject);
            contentPanel.Controls.Add(pageImage);
            contentPanel.Controls.Add(pageRename);
            Controls.Add(contentPanel);
            Controls.Add(sidebar);
            Controls.Add(titleBar);
            Ui.SetDoubleBuffered(contentPanel);
            Ui.SetDoubleBuffered(sidebar);
            Ui.SetDoubleBuffered(titleBar);

            SwitchPage(1);   // 打开软件第一屏就是「生成图像」
            // 大图 / 生成失败原因窗口里的「使用提示词」：切到生成图像页并把提示词填回输入框
            ImagePage.UsePromptHook = t =>
            {
                SwitchPage(1);
                pageImage.UsePromptText(t);
            };
            // 前台时按 Ctrl+V：剪贴板里是图片且焦点不在提示词框，就直接贴进参考图
            KeyPreview = true;
            KeyDown += (s, e) =>
            {
                try
                {
                    if (!e.Control || e.KeyCode != Keys.V) return;
                    if (pageImage == null || !pageImage.Visible) return;
                    if (!Clipboard.ContainsImage()) return;
                    if (pageImage.PromptHasFocus) return;      // 提示词框里粘贴文字，不抢
                    if (pageImage.PasteClipboardImage()) { e.Handled = true; e.SuppressKeyPress = true; }
                }
                catch { }
            };
            Ui.ApplyWinFrame(this, 14);
            // 注意：主窗绝不能开 WS_EX_COMPOSITED。窗口一大（最大化 2560×1392）整窗合成就会拖死重绘，
            // 表现为点了最大化半天没反应、画面只画出左上角一块、其余露出桌面壁纸。
            // 主窗的闪烁已由「各控件双缓冲 + 取消遮罩快照 + Relayout 挂起布局」解决。
            Toast.Owner = this;
            _sh = Ui.AttachShadow(this, 40, 14);
            HookResize();
            Resize += (s, e) =>
            {
                if (WindowState == FormWindowState.Minimized) { _wasMin = true; _restoreMax = _lastMax; return; }
                if (WindowState == FormWindowState.Maximized) _lastMax = true;
                else if (WindowState == FormWindowState.Normal) { _lastMax = false; _lastNormal = Size; }
                // 手动拖边缩放时也要逐帧重排：否则拖的过程中内容是老的、右边露出一大块白底，
                // 看着就像“没跟上”。真正拖起来卡的是每帧重建阴影位图，那个已由 _sh.Animating 挡掉。
                Relayout();
                if (_wasMin && WindowState == FormWindowState.Normal)
                {
                    _wasMin = false;
                    // 恢复瞬间必须把整窗内容同步重绘完，否则未重绘的控件区域会透出背后的桌面（碎片条带）
                    Invalidate(true);
                    Refresh();
                    if (_sh != null)
                    {
                        _sh.Animating = true;                 // 系统恢复动画期间不重建/不显示阴影，避免阻塞与图层错位
                        System.Windows.Forms.Timer st = new System.Windows.Forms.Timer { Interval = 260 };
                        st.Tick += (a, b) =>
                        {
                            st.Stop();
                            if (_sh != null) { _sh.Animating = false; _sh.ReRender(); }
                            st.Dispose();
                        };
                        st.Start();
                    }
                }
            };
            FormClosing += (s, e) =>
            {
                // 记住这次拖出来的窗口大小（最大化时记的是恢复后的普通尺寸）
                try
                {
                    if (Program.Config != null)
                    {
                        if (Program.Config.window == null) Program.Config.window = new CfgWindow();
                        Size normal = WindowState == FormWindowState.Normal ? Size : (MinimumSize.Width > 0 && _lastNormal != Size.Empty ? _lastNormal : Size);
                        Program.Config.window.w = Math.Max(Program.WinW, normal.Width);
                        Program.Config.window.h = Math.Max(Program.WinH, normal.Height);
                    }
                }
                catch { }
                Program.SaveConfig();
            };
        }

        bool _wasMin;
        bool _lastMax;
        Size _lastNormal = Size.Empty;   // 上次普通状态下的窗口尺寸，用于关闭时记忆

        // ---- 自建窗口缩放（不依赖 Windows 的 WS_THICKFRAME，避免系统擅自加绘标题栏/边框）----
        int _rzEdge;                 // 0 无 / 1 左上 / 2 上 / 3 右上 / 4 右 / 5 右下 / 6 下 / 7 左下 / 8 左
        bool _rzDown;
        Point _rzStart;
        Rectangle _rzRect;

        int EdgeAt(Point p)
        {
            if (WindowState == FormWindowState.Maximized) return 0;
            bool l = p.X <= Pad, r = p.X >= ClientSize.Width - Pad;
            bool t = p.Y <= Pad, b = p.Y >= ClientSize.Height - Pad;
            if (t && l) return 1;
            if (t && r) return 3;
            if (b && r) return 5;
            if (b && l) return 7;
            if (t) return 2;
            if (r) return 4;
            if (b) return 6;
            if (l) return 8;
            return 0;
        }
        static Cursor CursorFor(int e)
        {
            switch (e)
            {
                case 1: case 5: return Cursors.SizeNWSE;
                case 3: case 7: return Cursors.SizeNESW;
                case 2: case 6: return Cursors.SizeNS;
                case 4: case 8: return Cursors.SizeWE;
            }
            return Cursors.Default;
        }
        void HookResize()
        {
            MouseMove += (s, e) =>
            {
                if (_rzDown) { DoDragResize(); return; }
                int edge = EdgeAt(e.Location);
                _rzEdge = edge;
                Cursor = CursorFor(edge);
            };
            MouseDown += (s, e) =>
            {
                if (e.Button != MouseButtons.Left) return;
                int edge = EdgeAt(e.Location);
                if (edge == 0) return;
                _rzEdge = edge;
                _rzDown = true;
                _rzStart = Cursor.Position;
                _rzRect = Bounds;
                Capture = true;
                // 拖拽期间绝不重建阴影位图（那块 1400×900 的高斯模糊每帧重画就是卡顿的根）
                if (_sh != null) _sh.Animating = true;
            };
            MouseUp += (s, e) =>
            {
                if (!_rzDown) return;
                _rzDown = false;
                Capture = false;
                Cursor = CursorFor(EdgeAt(e.Location));
                if (_sh != null)
                {
                    _sh.Animating = true;                 // 先跟到最终位置，再一次性重建阴影
                    System.Windows.Forms.Timer st = new System.Windows.Forms.Timer { Interval = 60 };
                    st.Tick += (a, b) =>
                    {
                        st.Stop();
                        if (_sh != null) { _sh.Animating = false; _sh.ReRender(); }
                        st.Dispose();
                    };
                    st.Start();
                }
            };
            MouseLeave += (s, e) => { if (!_rzDown) Cursor = Cursors.Default; };
        }
        void DoDragResize()
        {
            if (_rzEdge == 0) return;
            Rectangle max = Screen.FromPoint(_rzStart).WorkingArea;
            Point cur = Cursor.Position;
            int dx = cur.X - _rzStart.X, dy = cur.Y - _rzStart.Y;
            Rectangle r = _rzRect;
            int minW = Math.Max(Program.WinW, MinimumSize.Width);
            int minH = Math.Max(Program.WinH, MinimumSize.Height);
            if (_rzEdge == 1 || _rzEdge == 7 || _rzEdge == 8)     // 往左拉：宽度反向变化
            {
                r.Width = _rzRect.Width - dx;
                if (r.Width < minW) r.Width = minW;
                if (r.Width > max.Width) r.Width = max.Width;
                r.X = _rzRect.Right - r.Width;
            }
            if (_rzEdge == 3 || _rzEdge == 4 || _rzEdge == 5)     // 往右拉：宽度正向变化
            {
                r.Width = _rzRect.Width + dx;
                if (r.Width < minW) r.Width = minW;
                if (r.Width > max.Width) r.Width = max.Width;
            }
            if (_rzEdge == 1 || _rzEdge == 2 || _rzEdge == 3)     // 往上拉
            {
                r.Height = _rzRect.Height - dy;
                if (r.Height < minH) r.Height = minH;
                if (r.Height > max.Height) r.Height = max.Height;
                r.Y = _rzRect.Bottom - r.Height;
            }
            if (_rzEdge == 5 || _rzEdge == 6 || _rzEdge == 7)     // 往下拉
            {
                r.Height = _rzRect.Height + dy;
                if (r.Height < minH) r.Height = minH;
                if (r.Height > max.Height) r.Height = max.Height;
            }
            if (r != Bounds) Bounds = r;
        }

        ShadowHost _sh;

        [StructLayout(LayoutKind.Sequential)]
        struct MPoint { public int x; public int y; }
        [StructLayout(LayoutKind.Sequential)]
        struct MinMaxInfo
        {
            public MPoint ptReserved;
            public MPoint ptMaxSize;
            public MPoint ptMaxPosition;
            public MPoint ptMinTrackSize;
            public MPoint ptMaxTrackSize;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            // 抓边条带由窗口自己画：左侧跟侧栏同色，其余同页面白，视觉上不出现异色边框
            e.Graphics.Clear(Color.White);
            using (SolidBrush br = new SolidBrush(Color.FromArgb(250, 251, 253)))
                e.Graphics.FillRectangle(br, 0, 0, Pad, Height);
            base.OnPaint(e);
        }

        // 最大化尺寸限制为"工作区"，避免无边框窗口最大化后盖住任务栏；
        // 同时补上 WM_NCHITTEST：无边框窗也能拖四边四角改尺寸。
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0024 && m.LParam != IntPtr.Zero)
            {
                try
                {
                    MinMaxInfo mmi = (MinMaxInfo)Marshal.PtrToStructure(m.LParam, typeof(MinMaxInfo));
                    Screen sc = Screen.FromHandle(Handle);
                    Rectangle wa = sc.WorkingArea;
                    mmi.ptMaxPosition.x = wa.Left - sc.Bounds.Left;
                    mmi.ptMaxPosition.y = wa.Top - sc.Bounds.Top;
                    mmi.ptMaxSize.x = wa.Width;
                    mmi.ptMaxSize.y = wa.Height;
                    mmi.ptMaxTrackSize.x = wa.Width;     // 拖边最多也只能拉到最大化尺寸
                    mmi.ptMaxTrackSize.y = wa.Height;
                    mmi.ptMinTrackSize.x = Program.WinW; // 最小不能小于默认 1300×860
                    mmi.ptMinTrackSize.y = Program.WinH;
                    Marshal.StructureToPtr(mmi, m.LParam, false);
                }
                catch { }
            }

            // 从最小化恢复：先按目标尺寸摆好窗口再显示，避免 Windows 先弹出一个白色小窗再变大
            if (m.Msg == 0x0112)
            {
                int sc = (int)m.WParam & 0xFFF0;
                if (sc == 0xF120 && _restoreMax)
                {
                    _restoreMax = false;
                    RestoreToMaxNoFlash();
                    return;
                }
                if (sc == 0xF120)
                {
                    // 普通尺寸恢复：先藏阴影层，等窗口画好再让阴影回来（否则会先露出灰色/白色方块）
                    if (_sh != null) { _sh.Animating = true; _sh.Hide(); }
                    System.Windows.Forms.Timer st = new System.Windows.Forms.Timer { Interval = 320 };
                    st.Tick += (a, b) => { st.Stop(); if (_sh != null) { _sh.Animating = false; _sh.ReRender(); } st.Dispose(); };
                    st.Start();
                }
            }

            if (m.Msg == 0x0085)          // WM_NCPAINT：禁止 Windows 往非客户区画任何东西
            {
                m.Result = IntPtr.Zero;   // 否则带 WS_SYSMENU/WS_THICKFRAME 的无边框窗会偶发画出经典标题栏条带
                return;
            }

            if (m.Msg == 0x0083)           // WM_NCCALCSIZE：客户区=整窗（不论 wParam 是否为 0）
            {
                m.Result = IntPtr.Zero;
                return;
            }


            if (m.Msg == 0x0020 && m.WParam == Handle)          // WM_SETCURSOR：鼠标一进窗就按所在边设置形状，兜住 MouseMove 漏发的情况
            {
                int e0 = EdgeAt(PointToClient(Cursor.Position));
                if (e0 != 0)
                {
                    int id = (e0 == 1 || e0 == 5) ? 32642 : (e0 == 3 || e0 == 7) ? 32643 : (e0 == 2 || e0 == 6) ? 32645 : 32644;
                    SetCursor(LoadCursor(IntPtr.Zero, id));
                    m.Result = (IntPtr)1;
                    return;
                }
            }

            if (m.Msg == 0x0083 && m.WParam != IntPtr.Zero)     // WM_NCCALCSIZE：客户区=整窗，不留任何系统边框
            {
                m.Result = IntPtr.Zero;
                return;
            }

            // 注意：这里刻意不再返回 HTLEFT/HTRIGHT… 等缩放命中码。
            // 一旦返回，Windows 会把消息改成 WM_NCLBUTTONDOWN 并自己进缩放循环，
            // 顺带按“带壳窗口”去画非客户区（浅蓝标题栏条带就是这么冒出来的）。
            // 缩放手感完全由 HookResize() 的 MouseDown/MouseMove 负责。
            base.WndProc(ref m);
        }

        bool _restoreMax;

        /// <summary>最大化状态下被最小化后恢复：先把窗口尺寸设成工作区再 ShowWindow(SW_SHOWMAXIMIZED)，
        /// 跳过 Windows 默认的“先以普通尺寸显示、再最大化”带来的白色空窗闪烁。</summary>
        void RestoreToMaxNoFlash()
        {
            try
            {
                if (_sh != null) { _sh.Animating = true; _sh.Hide(); }
                Screen sc = Screen.FromHandle(Handle);
                Rectangle wa = sc.WorkingArea;
                SetWindowPos(Handle, IntPtr.Zero, wa.Left, wa.Top, wa.Width, wa.Height,
                    0x0004 | 0x0010 | 0x0001);      // SWP_NOZORDER|SWP_NOACTIVATE|SWP_NOCOPYBITS
                ShowWindow(Handle, 3);              // SW_SHOWMAXIMIZED
                Relayout();
                Invalidate(true);
                Update();
                if (_sh != null)
                {
                    System.Windows.Forms.Timer st = new System.Windows.Forms.Timer { Interval = 260 };
                    st.Tick += (a, b) => { st.Stop(); if (_sh != null) { _sh.Animating = false; _sh.ReRender(); } st.Dispose(); };
                    st.Start();
                }
            }
            catch { }
        }

        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int cmd);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);

        /// <summary>启动弹出：窗口仍隐藏时交给 Windows 播放中心展开动画，再同步内部可见状态。</summary>
        protected override void SetVisibleCore(bool value)
        {
            if (value && !_popped)
            {
                _popped = true;
                if (!IsHandleCreated) CreateHandle();
                base.SetVisibleCore(false);                 // 保持隐藏，供 AnimateWindow 播放"显示"动画
                bool ok = Ui.NativePop(Handle, 220);
                base.SetVisibleCore(true);                  // 动画已同步结束，同步 Visible/Shown，阴影随后挂上
                if (!ok) { Visible = true; Activate(); }    // 系统关闭了动画时的兜底
                return;
            }
            base.SetVisibleCore(value);
        }
        bool _popped;

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
        static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
        static extern IntPtr GetWindowLongPtr32(IntPtr hWnd, int nIndex);
        static IntPtr GetWinLong(IntPtr hWnd, int nIndex)
        { return IntPtr.Size == 8 ? GetWindowLongPtr64(hWnd, nIndex) : GetWindowLongPtr32(hWnd, nIndex); }
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
        static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
        [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
        static extern IntPtr SetWindowLongPtr32(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
        static IntPtr SetWinLong(IntPtr hWnd, int nIndex, IntPtr dwNewLong)
        { return IntPtr.Size == 8 ? SetWindowLongPtr64(hWnd, nIndex, dwNewLong) : SetWindowLongPtr32(hWnd, nIndex, dwNewLong); }

        /// <summary>
        /// 兜底：任何时刻（建句柄/显示/激活/尺寸变化/定时器）都把 WS_CAPTION / WS_THICKFRAME / WS_SYSMENU
        /// 这几位重新擦一遍。系统层面的状态有时会被自己悄悄加回来，只靠 CreateParams 一次性设置不够稳。
        /// 一旦这几位回来，Windows 就会在客户区顶端画那条经典标题栏（浅蓝横条 + 系统□✕）。
        /// </summary>
        void EnsureFrameless()
        {
            if (!IsHandleCreated || IsDisposed) return;
            try
            {
                int st = unchecked((int)GetWinLong(Handle, -16).ToInt64());
                int want = (st & ~(0x00C00000 | 0x000F0000)) | 0x00020000;
                if (want != st)
                {
                    SetWinLong(Handle, -16, new IntPtr(want));
                    SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010 | 0x0020);
                    // NOSIZE|NOMOVE|NOACTIVATE|FRAMECHANGED：让系统按新样式重算非客户区
                }
            }
            catch { }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            EnsureFrameless();
        }

        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);
            EnsureFrameless();
        }

        /// <summary>无边框窗口也必须带 WS_MINIMIZEBOX，否则点击任务栏上已激活的图标不会最小化。</summary>
        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                // 只留 WS_MINIMIZEBOX（任务栏点已激活的图标能最小化）。
                // 绝不能加 WS_THICKFRAME / WS_SYSMENU：加了之后 Windows 会把这窗体当成“带标题栏的壳”，
                // 偶发在客户区顶端画一条经典标题栏（浅蓝横条 + 黑白□✕），也就是用户看到的“不明程序框”。
                // 缩放改由本窗自己的 MouseDown/Move 拖边实现，不依赖系统非客户区。
                cp.Style |= 0x00020000;
                cp.Style &= ~0x00C00000;    // 不要 WS_CAPTION
                cp.Style &= ~0x00040000;    // 不要 WS_THICKFRAME
                cp.Style &= ~0x00080000;    // 不要 WS_SYSMENU
                return cp;
            }
        }

        void DoMinimize()
        {
            _restoreMax = (WindowState == FormWindowState.Maximized);   // 记住“最大化后被最小化”，恢复时走无闪烁路径
            if (_sh != null) { _sh.Animating = true; _sh.Hide(); }       // 最小化前先收掉阴影层，恢复时不会先闪一块方块
            Ui.NativeMinimize(this);                        // 交给 Windows：系统收进任务栏动画 + 真正 iconic（任务栏可恢复）
        }
        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            EnsureFrameless();
            Relayout();                       // 上次记下来的窗口尺寸是构造期设的，这里再按真实尺寸排一次版，内容才跟得上
            if (_sh != null) _sh.ReRender();
            BeginInvoke(new Action(() => { EnsureFrameless(); Relayout(); if (_sh != null) _sh.ReRender(); }));
            // 再多盯两拍：启动动画/首帧合成期间系统有可能把标题栏位加回来
            System.Windows.Forms.Timer guard = new System.Windows.Forms.Timer { Interval = 900 };
            guard.Tick += (s2, e2) => { guard.Stop(); guard.Dispose(); EnsureFrameless(); Relayout(); };
            guard.Start();

            // 启动后异步检查更新：没填地址/网络失败都静默跳过，不阻塞打开软件
            System.Windows.Forms.Timer up = new System.Windows.Forms.Timer { Interval = 4500 };
            up.Tick += (s3, e3) =>
            {
                up.Stop(); up.Dispose();
                UpdateService.Check(true);
            };
            up.Start();
        }

        void BuildTitleBar()
        {
            titleBar = new Panel();
            titleBar.Location = new Point(Pad, Pad);
            titleBar.Size = new Size(Program.WinW - Pad * 2, Program.TitleH);
            titleBar.BackColor = Color.White;
            titleBar.Paint += (s, e) =>
            {
                using (Pen pen = new Pen(Color.FromArgb(236, 239, 245)))
                    e.Graphics.DrawLine(pen, 0, Program.TitleH - 1, titleBar.Width, Program.TitleH - 1);
            };

            Panel logo = new Panel();
            logo.Location = new Point(18, 11);
            logo.Size = new Size(30, 30);
            logo.Paint += (s, e) =>
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                if (Icon != null)
                {
                    g.DrawIcon(Icon, new Rectangle(0, 0, 30, 30));
                }
                else
                {
                    Rectangle rc = new Rectangle(0, 0, 29, 29);
                    using (GraphicsPath path = Ui.Rounded(rc, 9))
                    using (LinearGradientBrush br = new LinearGradientBrush(rc, Color.FromArgb(108, 140, 245), Color.FromArgb(150, 118, 255), 35f))
                        g.FillPath(br, path);
                    TextRenderer.DrawText(g, "AI", new Font("微软雅黑", 10.5F, FontStyle.Bold), rc, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                }
            };
            titleBar.Controls.Add(logo);

            Label title = new Label();
            title.Text = "AI系列生产工具";
            title.Font = new Font("微软雅黑", 12F, FontStyle.Bold);
            title.ForeColor = Color.FromArgb(31, 41, 55);
            title.Location = new Point(60, 13);
            title.Size = new Size(320, 26);
            titleBar.Controls.Add(title);

            TitleButton btnClose2 = new TitleButton('x');
            btnClose = btnClose2;
            btnClose2.Location = new Point(Program.WinW - Pad - 48, 4);
            btnClose2.Click += (s, e) => Close();
            titleBar.Controls.Add(btnClose2);

            TitleButton btnMin2 = new TitleButton('m');
            btnMin = btnMin2;
            btnMin2.Location = new Point(Program.WinW - Pad - 136, 4);
            btnMin2.Click += (s, e) => DoMinimize();
            titleBar.Controls.Add(btnMin2);

            TitleButton btnMax2 = new TitleButton('f');
            btnMax = btnMax2;
            btnMax2.Location = new Point(Program.WinW - Pad - 92, 4);
            btnMax2.Click += (s, e) => ToggleMax();
            titleBar.Controls.Add(btnMax2);

            titleBar.MouseDown += TitleBarDrag;
            foreach (Control c in titleBar.Controls)
                if (!(c is TitleButton)) c.MouseDown += TitleBarDrag;
        }

        void TitleBarDrag(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                ReleaseCapture();
                SendMessage(Handle, 0xA1, 2, 0);
            }
        }

        void BuildSidebar()
        {
            sidebar = new Panel();
            sidebar.Location = new Point(Pad, Pad + Program.TitleH);
            sidebar.Size = new Size(Program.SideW, Program.WinH - Program.TitleH - Pad * 2);
            sidebar.BackColor = Color.FromArgb(250, 251, 253);
            sidebar.Paint += (s, e) =>
            {
                using (Pen pen = new Pen(Color.FromArgb(236, 239, 245)))
                    e.Graphics.DrawLine(pen, Program.SideW - 1, 0, Program.SideW - 1, sidebar.Height);
            };

            Label menuLabel = new Label();
            menuLabel.Text = "功能菜单";
            menuLabel.Font = new Font("微软雅黑", 9F);
            menuLabel.ForeColor = Color.FromArgb(150, 158, 180);
            menuLabel.Location = new Point(24, 22);
            menuLabel.Size = new Size(100, 20);
            sidebar.Controls.Add(menuLabel);

            // 菜单顺序：生成图像第一、新建项目第二、批量重命名第三
            navImage = new NavButton("生成图像", 2);
            navImage.Location = new Point(20, 52);
            navImage.Size = new Size(Program.SideW - 40, 50);
            navImage.Click += (s, e) => SwitchPage(1);
            sidebar.Controls.Add(navImage);

            navProject = new NavButton("新建项目", 0);
            navProject.Location = new Point(20, 112);
            navProject.Size = new Size(Program.SideW - 40, 50);
            navProject.Click += (s, e) => SwitchPage(0);
            sidebar.Controls.Add(navProject);

            navRename = new NavButton("批量重命名", 1);
            navRename.Location = new Point(20, 172);
            navRename.Size = new Size(Program.SideW - 40, 50);
            navRename.Click += (s, e) => SwitchPage(2);
            sidebar.Controls.Add(navRename);

            Label ver = new Label();
            ver.Text = "v3.2.0.65";
            ver.Font = new Font("微软雅黑", 8.5F);
            ver.ForeColor = Color.FromArgb(170, 177, 195);
            // 底部留出足够余量：原来按窗口底边直接减 34，实际会压到侧栏底边被裁掉几个像素
            ver.Location = new Point(0, Program.WinH - Program.TitleH - 44);
            ver.Size = new Size(Program.SideW, 22);
            ver.TextAlign = ContentAlignment.MiddleCenter;
            sidebar.Controls.Add(ver);
            verLabel = ver;

            // 左下角常驻入口：自动检查失败时不会打扰，用户想手动确认时点这里
            Label upd = new Label();
            upd.Text = "检查更新";
            upd.Font = new Font("微软雅黑", 9F);
            upd.ForeColor = Color.FromArgb(96, 110, 145);
            upd.Cursor = Cursors.Hand;
            upd.Location = new Point(0, ver.Location.Y - 30);
            upd.Size = new Size(Program.SideW, 24);
            upd.TextAlign = ContentAlignment.MiddleCenter;
            upd.MouseEnter += (s, e) => { upd.ForeColor = Color.FromArgb(70, 105, 220); };
            upd.MouseLeave += (s, e) => { upd.ForeColor = Color.FromArgb(96, 110, 145); };
            upd.Click += (s, e) => UpdateService.Check(false);
            sidebar.Controls.Add(upd);
            upd.BringToFront();

            // 侧栏拉高/最大化后，底部两行必须继续贴底，不能停在创建时的固定 Y 坐标
            Action placeBottom = () =>
            {
                int h = sidebar.ClientSize.Height;
                ver.Location = new Point(0, h - 32);
                upd.Location = new Point(0, h - 62);
            };
            sidebar.Resize += (s, e) => placeBottom();
            placeBottom();
        }

        void ToggleMax()
        {
            bool maxed = (WindowState == FormWindowState.Maximized);
            if (_sh != null) { _sh.Animating = true; _sh.Hide(); }
            if (maxed) Ui.NativeRestoreUp(this);      // 交给 Windows 播原生还原动画
            else Ui.NativeMaximize(this);             // 交给 Windows 播原生最大化动画
            System.Windows.Forms.Timer st = new System.Windows.Forms.Timer { Interval = 320 };
            st.Tick += (a, b) =>
            {
                st.Stop();
                st.Dispose();
                if (_sh != null) { _sh.Animating = false; _sh.ReRender(); }
            };
            st.Start();
        }

        // 窗口尺寸变化时，标题栏/侧栏/内容区与三个页面一起适配
        void Relayout()
        {
            int cw = ClientSize.Width, ch = ClientSize.Height;
            if (cw <= 10 || ch <= 10) return;
            SuspendLayout();                                  // 整体调整期间不逐控件重绘，避免中间态闪一下
            try
            {
            titleBar.Size = new Size(cw - Pad * 2, Program.TitleH);
            titleBar.Location = new Point(Pad, Pad);
            if (btnClose != null) btnClose.Location = new Point(cw - Pad - 48, 4);
            if (btnMax != null)
            {
                btnMax.Location = new Point(cw - Pad - 92, 4);
                btnMax.MaxState = (WindowState == FormWindowState.Maximized);
                btnMax.Invalidate();
            }
            if (btnMin != null) btnMin.Location = new Point(cw - Pad - 136, 4);
            sidebar.Size = new Size(Program.SideW, ch - Program.TitleH - Pad * 2);
            if (verLabel != null) verLabel.Location = new Point(0, sidebar.Height - 34);
            contentPanel.Location = new Point(Program.SideW + Pad, Pad + Program.TitleH);
            contentPanel.Size = new Size(cw - Program.SideW - Pad * 2, ch - Program.TitleH - Pad * 2);

            PageBase[] pages = new PageBase[] { pageProject, pageImage, pageRename };
            foreach (PageBase pg in pages)
            {
                if (pg == null) continue;
                pg.Bounds = contentPanel.ClientRectangle;
                pg.RecenterIfAuto();          // 尺寸没变时 Resize 不触发，这里显式按当前窗口居中
            }
            }
            finally { ResumeLayout(true); }
        }

        void SwitchPage(int idx)
        {
            navProject.Active = (idx == 0);
            navImage.Active = (idx == 1);
            navRename.Active = (idx == 2);
            pageProject.Visible = (idx == 0);
            pageImage.Visible = (idx == 1);
            pageRename.Visible = (idx == 2);
        }
    }
}
