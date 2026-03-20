using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.Net.NetworkInformation;

namespace whatsapp
{
    public partial class Form1 : Form
    {
        // ─── Networking ───────────────────────────────────────────────
        private TcpListener _server;
        private TcpClient _client;
        private NetworkStream _stream;
        private Thread _listenThread;
        private Thread _receiveThread;
        private bool _isConnected = false;
        private const int PORT = 5000;

        // ─── UI Components ────────────────────────────────────────────
        private Panel panelSidebar;
        private Panel panelHeader;
        private Panel panelMessages;
        private Panel panelInput;
        private Panel panelConnect;
        private FlowLayoutPanel flowMessages;
        private TextBox txtMessage;
        private TextBox txtTargetIP;
        private TextBox txtMyName;
        private Button btnSend;
        private Button btnConnect;
        private Button btnListen;
        private Label lblStatus;
        private Label lblContactName;
        private Label lblContactStatus;
        private Label lblMyIP;
        private PictureBox picAvatar;
        private PictureBox picMyAvatar;

        // ─── State ────────────────────────────────────────────────────
        private string _myName = "Tú";
        private string _contactName = "Contacto";
        private string _myIP = "";

        // ─── Colors (WhatsApp palette) ────────────────────────────────
        private readonly Color WA_Dark = Color.FromArgb(17, 27, 33);
        private readonly Color WA_Panel = Color.FromArgb(31, 44, 52);
        private readonly Color WA_Chat = Color.FromArgb(11, 20, 26);
        private readonly Color WA_MsgOut = Color.FromArgb(5, 96, 98);
        private readonly Color WA_MsgIn = Color.FromArgb(31, 44, 52);
        private readonly Color WA_InputBg = Color.FromArgb(42, 57, 66);
        private readonly Color WA_Green = Color.FromArgb(0, 168, 132);
        private readonly Color WA_LightGreen = Color.FromArgb(37, 211, 102);
        private readonly Color WA_Text = Color.FromArgb(229, 221, 213);
        private readonly Color WA_SubText = Color.FromArgb(134, 150, 160);
        private readonly Color WA_Tick = Color.FromArgb(83, 175, 236);

        public Form1()
        {
            this.Text = "WhatsApp";
            this.Size = new Size(1000, 700);
            this.MinimumSize = new Size(800, 600);
            this.BackColor = WA_Dark;
            this.Font = new Font("Segoe UI", 9.5f);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.Sizable;

            // App Icon color bar at top (Windows title area trick)
            this.Icon = CreateAppIcon();

            _myIP = GetLocalIP();

            BuildUI();
            StartListening();
        }

        // ═══════════════════════════════════════════════════════════════
        //  UI BUILDING
        // ═══════════════════════════════════════════════════════════════

        private void BuildUI()
        {
            // ── Sidebar ──────────────────────────────────────────────
            panelSidebar = new Panel
            {
                Dock = DockStyle.Left,
                Width = 320,
                BackColor = WA_Panel
            };

            // Sidebar header (my profile)
            var sideHeader = new Panel
            {
                Dock = DockStyle.Top,
                Height = 59,
                BackColor = Color.FromArgb(42, 57, 66)
            };

            picMyAvatar = CreateAvatar(36, "Y", WA_Green);
            picMyAvatar.Location = new Point(12, 11);

            txtMyName = new TextBox
            {
                Text = "Mi Nombre",
                Location = new Point(56, 20),
                Width = 160,
                BackColor = Color.FromArgb(60, 80, 90),
                ForeColor = WA_Text,
                BorderStyle = BorderStyle.None,
                Font = new Font("Segoe UI Semibold", 10f)
            };
            txtMyName.TextChanged += (s, e) => { _myName = txtMyName.Text; };

            lblMyIP = new Label
            {
                Text = $"Tu IP: {_myIP}",
                Location = new Point(12, 65),
                Width = 296,
                Height = 18,
                ForeColor = WA_SubText,
                Font = new Font("Segoe UI", 8f),
                TextAlign = ContentAlignment.MiddleLeft,
                BackColor = Color.Transparent
            };

            sideHeader.Controls.AddRange(new Control[] { picMyAvatar, txtMyName });
            panelSidebar.Controls.Add(sideHeader);
            panelSidebar.Controls.Add(lblMyIP);
            lblMyIP.BringToFront();
            lblMyIP.Top = 64;

            // ── Connection Panel ─────────────────────────────────────
            panelConnect = new Panel
            {
                Location = new Point(0, 90),
                Width = 320,
                Height = 200,
                BackColor = WA_Panel
            };

            var lblIPTitle = MakeLabel("IP del destinatario:", 12, 8, WA_SubText, 8.5f);
            txtTargetIP = new TextBox
            {
                Location = new Point(12, 26),
                Width = 200,
                BackColor = WA_InputBg,
                ForeColor = WA_Text,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Segoe UI", 10f),
                Text = "192.168.1."
            };

            btnConnect = CreateWAButton("Conectar", 220, 24, WA_Green, Color.White);
            btnConnect.Location = new Point(12, 54);
            btnConnect.Click += BtnConnect_Click;

            btnListen = CreateWAButton("Escuchar", 220, 24, WA_MsgIn, WA_LightGreen);
            btnListen.Location = new Point(12, 86);
            btnListen.Click += (s, e) => AppendSystemMessage("⏳ Esperando conexión en puerto " + PORT + "...");

            lblStatus = new Label
            {
                Text = "⚪ Sin conexión",
                Location = new Point(12, 120),
                Width = 296,
                Height = 22,
                ForeColor = WA_SubText,
                Font = new Font("Segoe UI", 9f),
                BackColor = Color.Transparent
            };

            var separator = new Panel
            {
                Location = new Point(0, 290),
                Width = 320,
                Height = 1,
                BackColor = Color.FromArgb(37, 50, 58)
            };

            panelConnect.Controls.AddRange(new Control[]
            {
                lblIPTitle, txtTargetIP, btnConnect, btnListen, lblStatus
            });
            panelSidebar.Controls.Add(panelConnect);
            panelSidebar.Controls.Add(separator);

            // Fake contact list entry
            var contactEntry = CreateContactEntry();
            contactEntry.Top = 298;
            panelSidebar.Controls.Add(contactEntry);

            // ── Main Chat Area ────────────────────────────────────────
            var panelMain = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = WA_Chat
            };

            // Header
            panelHeader = new Panel
            {
                Dock = DockStyle.Top,
                Height = 59,
                BackColor = Color.FromArgb(42, 57, 66)
            };

            picAvatar = CreateAvatar(40, "C", WA_LightGreen);
            picAvatar.Location = new Point(12, 9);

            lblContactName = new Label
            {
                Text = "Contacto",
                Location = new Point(62, 10),
                Size = new Size(300, 20),
                ForeColor = WA_Text,
                Font = new Font("Segoe UI Semibold", 11f),
                BackColor = Color.Transparent
            };
            lblContactStatus = new Label
            {
                Text = "Sin conexión",
                Location = new Point(62, 30),
                Size = new Size(300, 16),
                ForeColor = WA_SubText,
                Font = new Font("Segoe UI", 8.5f),
                BackColor = Color.Transparent
            };

            // Three dots menu (decorative)
            var btnMenu = new Label
            {
                Text = "⋮",
                Dock = DockStyle.Right,
                Width = 40,
                ForeColor = WA_SubText,
                Font = new Font("Segoe UI", 18f),
                TextAlign = ContentAlignment.MiddleCenter,
                BackColor = Color.Transparent,
                Cursor = Cursors.Hand
            };

            panelHeader.Controls.AddRange(new Control[]
                { picAvatar, lblContactName, lblContactStatus, btnMenu });

            // ── Messages area (chat wallpaper effect) ─────────────────
            panelMessages = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = WA_Chat,
                Padding = new Padding(0, 0, 0, 0)
            };
            panelMessages.Paint += DrawChatWallpaper;

            flowMessages = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true,
                BackColor = Color.Transparent,
                Padding = new Padding(8, 8, 8, 8)
            };

            panelMessages.Controls.Add(flowMessages);

            // ── Input Bar ─────────────────────────────────────────────
            panelInput = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 62,
                BackColor = Color.FromArgb(31, 44, 52)
            };

            // Emoji button (decorative)
            var btnEmoji = new Label
            {
                Text = "😊",
                Location = new Point(10, 16),
                Size = new Size(30, 30),
                Font = new Font("Segoe UI Emoji", 16f),
                Cursor = Cursors.Hand,
                BackColor = Color.Transparent
            };

            // Attach button (decorative)
            var btnAttach = new Label
            {
                Text = "📎",
                Location = new Point(44, 16),
                Size = new Size(30, 30),
                Font = new Font("Segoe UI Emoji", 16f),
                Cursor = Cursors.Hand,
                BackColor = Color.Transparent
            };

            txtMessage = new TextBox
            {
                Location = new Point(80, 14),
                Height = 34,
                BackColor = WA_InputBg,
                ForeColor = WA_Text,
                BorderStyle = BorderStyle.None,
                Font = new Font("Segoe UI", 11f),
                Multiline = false
            };
            txtMessage.KeyDown += TxtMessage_KeyDown;

            // Microphone / Send button
            btnSend = new Button
            {
                Location = new Point(0, 11), // will be repositioned
                Size = new Size(40, 40),
                FlatStyle = FlatStyle.Flat,
                BackColor = WA_Green,
                ForeColor = Color.White,
                Text = "➤",
                Font = new Font("Segoe UI", 12f),
                Cursor = Cursors.Hand
            };
            btnSend.FlatAppearance.BorderSize = 0;
            btnSend.Click += BtnSend_Click;
            MakeCircular(btnSend);

            // Input box rounded bg
            var inputRound = new Panel
            {
                Location = new Point(76, 10),
                BackColor = WA_InputBg
            };

            panelInput.Controls.AddRange(new Control[]
                { btnEmoji, btnAttach, txtMessage, btnSend });

            panelInput.Resize += (s, e) => RepositionInputControls();
            RepositionInputControls();

            // ── Assemble ──────────────────────────────────────────────
            panelMain.Controls.Add(panelMessages);
            panelMain.Controls.Add(panelInput);
            panelMain.Controls.Add(panelHeader);

            this.Controls.Add(panelMain);
            this.Controls.Add(panelSidebar);

            // Initial welcome message
            AppendSystemMessage("👋 Bienvenido a WhatsApp Clone\n" +
                                 $"Tu IP es: {_myIP}  |  Puerto: {PORT}\n" +
                                 "Ingresa la IP del otro equipo y presiona Conectar,\n" +
                                 "o simplemente espera que el otro se conecte primero.");
        }

        private void RepositionInputControls()
        {
            int w = panelInput.Width;
            txtMessage.Width = w - 80 - 55;
            txtMessage.Left = 80;
            txtMessage.Top = 14;
            btnSend.Location = new Point(w - 50, 11);
        }

        // ═══════════════════════════════════════════════════════════════
        //  NETWORKING
        // ═══════════════════════════════════════════════════════════════

        private void StartListening()
        {
            _listenThread = new Thread(() =>
            {
                try
                {
                    _server = new TcpListener(IPAddress.Any, PORT);
                    _server.Start();
                    while (true)
                    {
                        var incoming = _server.AcceptTcpClient();
                        if (_isConnected) { incoming.Close(); continue; }

                        _client = incoming;
                        _stream = _client.GetStream();
                        _isConnected = true;
                        string remoteIP = ((IPEndPoint)_client.Client.RemoteEndPoint).Address.ToString();

                        Invoke((Action)(() =>
                        {
                            SetConnected(remoteIP);
                            AppendSystemMessage($"✅ {remoteIP} se conectó contigo.");
                        }));

                        StartReceiving();
                    }
                }
                catch { }
            });
            _listenThread.IsBackground = true;
            _listenThread.Start();
        }

        private void BtnConnect_Click(object sender, EventArgs e)
        {
            string ip = txtTargetIP.Text.Trim();
            if (string.IsNullOrEmpty(ip)) { ShowError("Ingresa una IP válida."); return; }
            if (_isConnected) { ShowError("Ya estás conectado."); return; }

            try
            {
                _client = new TcpClient();
                _client.Connect(ip, PORT);
                _stream = _client.GetStream();
                _isConnected = true;

                SetConnected(ip);
                AppendSystemMessage($"✅ Conectado a {ip}");
                StartReceiving();
            }
            catch (Exception ex)
            {
                ShowError($"No se pudo conectar a {ip}\n{ex.Message}");
            }
        }

        private void StartReceiving()
        {
            _receiveThread = new Thread(() =>
            {
                byte[] buffer = new byte[4096];
                try
                {
                    while (_isConnected)
                    {
                        int bytes = _stream.Read(buffer, 0, buffer.Length);
                        if (bytes == 0) break;
                        string msg = Encoding.UTF8.GetString(buffer, 0, bytes);
                        Invoke((Action)(() => AppendMessage(msg, false)));
                    }
                }
                catch { }
                finally
                {
                    Invoke((Action)(() =>
                    {
                        _isConnected = false;
                        SetDisconnected();
                        AppendSystemMessage("❌ Conexión cerrada.");
                    }));
                }
            });
            _receiveThread.IsBackground = true;
            _receiveThread.Start();
        }

        private void SendMessage(string text)
        {
            if (!_isConnected) { ShowError("No estás conectado a nadie."); return; }
            if (string.IsNullOrWhiteSpace(text)) return;

            try
            {
                byte[] data = Encoding.UTF8.GetBytes(text);
                _stream.Write(data, 0, data.Length);
                AppendMessage(text, true);
            }
            catch (Exception ex)
            {
                ShowError("Error al enviar: " + ex.Message);
                SetDisconnected();
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  MESSAGE RENDERING
        // ═══════════════════════════════════════════════════════════════

        private void AppendMessage(string text, bool isMine)
        {
            var bubble = CreateBubble(text, isMine);
            flowMessages.Controls.Add(bubble);
            flowMessages.ScrollControlIntoView(bubble);
        }

        private void AppendSystemMessage(string text)
        {
            var lbl = new Label
            {
                Text = text,
                AutoSize = false,
                Width = flowMessages.ClientSize.Width - 40,
                BackColor = Color.FromArgb(20, 134, 150, 160),
                ForeColor = WA_SubText,
                Font = new Font("Segoe UI", 8.5f),
                TextAlign = ContentAlignment.MiddleCenter,
                Padding = new Padding(8, 6, 8, 6),
                Margin = new Padding(4, 6, 4, 6),
            };

            // Center wrapper
            var wrapper = new Panel
            {
                Width = flowMessages.ClientSize.Width - 16,
                Height = 0,
                BackColor = Color.Transparent,
                Margin = new Padding(0, 4, 0, 4)
            };
            wrapper.Controls.Add(lbl);
            wrapper.Height = lbl.PreferredHeight + 16;
            lbl.Location = new Point((wrapper.Width - Math.Min(lbl.PreferredWidth + 20, wrapper.Width - 20)) / 2, 4);
            lbl.Width = Math.Min(lbl.PreferredWidth + 20, wrapper.Width - 20);

            flowMessages.Controls.Add(wrapper);
            flowMessages.ScrollControlIntoView(wrapper);
        }

        private Panel CreateBubble(string text, bool isMine)
        {
            int maxWidth = flowMessages.ClientSize.Width - 30;
            int bubbleMax = (int)(maxWidth * 0.65);

            var timeStr = DateTime.Now.ToString("HH:mm");

            // Measure text
            var tempLbl = new Label { Font = new Font("Segoe UI", 10.5f), Text = text, AutoSize = true };
            int textW = Math.Min(TextRenderer.MeasureText(text, tempLbl.Font).Width + 20, bubbleMax);
            int textH = TextRenderer.MeasureText(text, tempLbl.Font, new Size(textW - 20, 9999),
                            TextFormatFlags.WordBreak).Height;

            // Time label width
            int timeW = TextRenderer.MeasureText(timeStr + " ✓✓", new Font("Segoe UI", 7.5f)).Width + 6;

            int bubbleW = Math.Max(textW + 20, timeW + 20);
            int bubbleH = textH + 38; // text + time row

            var bubble = new Panel
            {
                Width = bubbleW,
                Height = bubbleH,
                BackColor = Color.Transparent,
                Margin = new Padding(
                    isMine ? (maxWidth - bubbleW) : 8,
                    2,
                    isMine ? 8 : (maxWidth - bubbleW),
                    2)
            };

            bubble.Paint += (s, e) =>
            {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                var rc = new Rectangle(0, 0, bubbleW - 1, bubbleH - 1);
                var path = RoundedRect(rc, 8);
                g.FillPath(new SolidBrush(isMine ? WA_MsgOut : WA_MsgIn), path);

                // Tail
                if (isMine)
                {
                    var tail = new Point[] {
                        new Point(bubbleW - 1, 10),
                        new Point(bubbleW + 7, 14),
                        new Point(bubbleW - 1, 20)
                    };
                    g.FillPolygon(new SolidBrush(WA_MsgOut), tail);
                }
                else
                {
                    var tail = new Point[] {
                        new Point(0, 10),
                        new Point(-7, 14),
                        new Point(0, 20)
                    };
                    g.FillPolygon(new SolidBrush(WA_MsgIn), tail);
                }
            };

            // Message text
            var lblText = new Label
            {
                Text = text,
                Location = new Point(10, 6),
                Width = bubbleW - 20,
                Height = textH + 4,
                ForeColor = WA_Text,
                Font = new Font("Segoe UI", 10.5f),
                BackColor = Color.Transparent
            };

            // Time + ticks
            var lblTime = new Label
            {
                Text = isMine ? timeStr + " ✓✓" : timeStr,
                Location = new Point(bubbleW - timeW - 8, bubbleH - 20),
                Width = timeW + 4,
                Height = 16,
                ForeColor = isMine ? WA_Tick : WA_SubText,
                Font = new Font("Segoe UI", 7.5f),
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleRight
            };

            bubble.Controls.AddRange(new Control[] { lblText, lblTime });
            return bubble;
        }

        // ═══════════════════════════════════════════════════════════════
        //  STATE HELPERS
        // ═══════════════════════════════════════════════════════════════

        private void SetConnected(string ip)
        {
            _contactName = ip;
            lblContactName.Text = ip;
            lblContactStatus.Text = "en línea";
            lblContactStatus.ForeColor = WA_LightGreen;
            lblStatus.Text = "🟢 Conectado con " + ip;
            lblStatus.ForeColor = WA_LightGreen;
            btnConnect.Enabled = false;
        }

        private void SetDisconnected()
        {
            _isConnected = false;
            lblContactStatus.Text = "Sin conexión";
            lblContactStatus.ForeColor = WA_SubText;
            lblStatus.Text = "⚪ Sin conexión";
            lblStatus.ForeColor = WA_SubText;
            btnConnect.Enabled = true;
            _client?.Close();
        }

        // ═══════════════════════════════════════════════════════════════
        //  EVENTS
        // ═══════════════════════════════════════════════════════════════

        private void TxtMessage_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter && !e.Shift)
            {
                e.SuppressKeyPress = true;
                BtnSend_Click(sender, e);
            }
        }

        private void BtnSend_Click(object sender, EventArgs e)
        {
            string msg = txtMessage.Text.Trim();
            if (string.IsNullOrEmpty(msg)) return;
            SendMessage(msg);
            txtMessage.Clear();
            txtMessage.Focus();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _isConnected = false;
            _stream?.Close();
            _client?.Close();
            _server?.Stop();
            base.OnFormClosing(e);
        }

        // ═══════════════════════════════════════════════════════════════
        //  DRAWING HELPERS
        // ═══════════════════════════════════════════════════════════════

        private void DrawChatWallpaper(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            var rc = panelMessages.ClientRectangle;
            // Subtle dark pattern background
            using (var brush = new SolidBrush(WA_Chat))
                g.FillRectangle(brush, rc);

            // Faint grid dots
            using (var dotBrush = new SolidBrush(Color.FromArgb(20, 255, 255, 255)))
            {
                for (int x = 20; x < rc.Width; x += 28)
                    for (int y = 20; y < rc.Height; y += 28)
                        g.FillEllipse(dotBrush, x, y, 2, 2);
            }
        }

        private PictureBox CreateAvatar(int size, string letter, Color bg)
        {
            var bmp = new Bitmap(size, size);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.FillEllipse(new SolidBrush(bg), 0, 0, size - 1, size - 1);
                var font = new Font("Segoe UI Semibold", size * 0.38f);
                var sz = g.MeasureString(letter, font);
                g.DrawString(letter, font, Brushes.White,
                    (size - sz.Width) / 2, (size - sz.Height) / 2);
            }
            var pic = new PictureBox
            {
                Size = new Size(size, size),
                Image = bmp,
                SizeMode = PictureBoxSizeMode.StretchImage
            };
            return pic;
        }

        private Panel CreateContactEntry()
        {
            var p = new Panel
            {
                Width = 320,
                Height = 72,
                BackColor = Color.Transparent,
                Cursor = Cursors.Hand
            };
            p.MouseEnter += (s, e) => p.BackColor = Color.FromArgb(42, 57, 66);
            p.MouseLeave += (s, e) => p.BackColor = Color.Transparent;

            var av = CreateAvatar(46, "C", WA_LightGreen);
            av.Location = new Point(12, 13);

            var name = new Label
            {
                Text = "Chat Local",
                Location = new Point(68, 14),
                ForeColor = WA_Text,
                Font = new Font("Segoe UI Semibold", 10.5f),
                BackColor = Color.Transparent,
                AutoSize = true
            };
            var last = new Label
            {
                Text = "Esperando mensajes...",
                Location = new Point(68, 34),
                ForeColor = WA_SubText,
                Font = new Font("Segoe UI", 8.5f),
                BackColor = Color.Transparent,
                AutoSize = true
            };

            var sep = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 1,
                BackColor = Color.FromArgb(37, 50, 58)
            };

            p.Controls.AddRange(new Control[] { av, name, last, sep });
            return p;
        }

        private Button CreateWAButton(string text, int width, int height, Color bg, Color fg)
        {
            var btn = new Button
            {
                Text = text,
                Width = width,
                Height = height,
                FlatStyle = FlatStyle.Flat,
                BackColor = bg,
                ForeColor = fg,
                Font = new Font("Segoe UI Semibold", 9.5f),
                Cursor = Cursors.Hand
            };
            btn.FlatAppearance.BorderSize = 0;
            return btn;
        }

        private Label MakeLabel(string text, int x, int y, Color color, float size)
        {
            return new Label
            {
                Text = text,
                Location = new Point(x, y),
                ForeColor = color,
                Font = new Font("Segoe UI", size),
                BackColor = Color.Transparent,
                AutoSize = true
            };
        }

        private void MakeCircular(Button btn)
        {
            btn.Region = new Region(RoundedRect(
                new Rectangle(0, 0, btn.Width, btn.Height), btn.Width / 2));
        }

        private GraphicsPath RoundedRect(Rectangle bounds, int radius)
        {
            int d = radius * 2;
            var path = new GraphicsPath();
            path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
            path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        private string GetLocalIP()
        {
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    foreach (var ip in ni.GetIPProperties().UnicastAddresses)
                    {
                        if (ip.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                            return ip.Address.ToString();
                    }
                }
            }
            catch { }
            return "127.0.0.1";
        }

        private Icon CreateAppIcon()
        {
            var bmp = new Bitmap(32, 32);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.FillEllipse(new SolidBrush(Color.FromArgb(37, 211, 102)), 2, 2, 28, 28);
                g.FillEllipse(Brushes.White, 7, 9, 18, 14);
                g.FillEllipse(new SolidBrush(Color.FromArgb(37, 211, 102)), 10, 12, 12, 8);
            }
            return Icon.FromHandle(bmp.GetHicon());
        }

        private void ShowError(string msg)
        {
            MessageBox.Show(msg, "WhatsApp Clone", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}