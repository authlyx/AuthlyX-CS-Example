using AuthlyXSDK;
using Guna.UI2.WinForms;
using Newtonsoft.Json.Linq;
using System;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace AuthlyX_CS_Example_Form
{
    public partial class Home : Form
    {
        private static readonly Color Accent = Color.FromArgb(59, 110, 230);
        private static readonly Color Field = Color.FromArgb(22, 22, 22);
        private static readonly Color Muted = Color.FromArgb(150, 150, 150);
        private static readonly Color Good = Color.FromArgb(74, 200, 120);
        private static readonly Color Bad = Color.FromArgb(235, 90, 90);
        private bool signingOut;

        public Home()
        {
            InitializeComponent();
            Theme(this);
            btnSignOut.FillColor = Color.FromArgb(120, 32, 32);
            btnSignOut.HoverState.FillColor = Color.FromArgb(150, 40, 40);
        }

        private static void Theme(Control parent)
        {
            foreach (Control control in parent.Controls)
            {
                if (control is Guna2Button button)
                {
                    button.BorderRadius = 8;
                    button.FillColor = Accent;
                    button.HoverState.FillColor = Color.FromArgb(80, 130, 245);
                    button.DisabledState.FillColor = Color.FromArgb(40, 40, 40);
                    button.DisabledState.ForeColor = Muted;
                    button.Font = new Font("Segoe UI Semibold", 9.5F);
                    button.ForeColor = Color.White;
                    button.Cursor = Cursors.Hand;
                }
                else if (control is Guna2TextBox box)
                {
                    box.BorderRadius = 8;
                    box.BorderColor = Color.FromArgb(55, 55, 55);
                    box.FocusedState.BorderColor = Accent;
                    box.HoverState.BorderColor = Color.FromArgb(90, 90, 90);
                    box.FillColor = Field;
                    box.ForeColor = Color.White;
                    box.PlaceholderForeColor = Color.FromArgb(110, 110, 110);
                    box.DisabledState.FillColor = Field;
                    box.DisabledState.ForeColor = Color.White;
                    box.BackColor = Color.Transparent;
                    if (box.Font.Name != "Consolas")
                    {
                        box.Font = new Font("Segoe UI", 9.75F);
                    }
                }
                Theme(control);
            }
        }

        private void Home_Load(object sender, EventArgs e)
        {
            Program.app.SessionInvalidated += SessionInvalidated;
            var user = Program.app.userData;
            titleLabel.Text = "Signed in as " + DisplayName(user);
            subtitleLabel.Text = Show(user.Plan) + " plan  |  Expires " + Expiry(user);
            if (user.LoginType == "license")
            {
                Row("License key", user.LicenseKey);
            }
            else if (user.LoginType == "device")
            {
                Row("Device", user.SID);
            }
            else
            {
                Row("Username", user.Username);
                if (user.LicenseKey != "")
                {
                    Row("License key", user.LicenseKey);
                }
            }
            Row("Email", user.Email);
            Row("Plan", user.Plan);
            Row("Expires", Expiry(user));
            Row("Device limit", user.DeviceLimit == 0 ? "Unlimited" : user.DeviceLimit.ToString());
            Row("Registered", When(user.RegisteredAt));
            Row("Last sign-in", When(user.LastLogin));
            Row("System ID", user.SID);
            Row("IP address", user.IpAddress);
            Row("Country", user.Country);
            accountTable.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            accountTable.Controls.Add(new Label(), 0, accountTable.RowCount++);
            LoadFeatures();
        }

        private void Row(string label, string value)
        {
            accountTable.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            accountTable.Controls.Add(Cell(label, Muted), 0, accountTable.RowCount);
            var copy = Cell(Show(value), Color.White);
            copy.AutoEllipsis = true;
            accountTable.Controls.Add(copy, 1, accountTable.RowCount++);
        }

        private static Label Cell(string text, Color color) => new Label {
            Text = text, ForeColor = color, Dock = DockStyle.Fill, Font = new Font("Segoe UI", 10F),
            TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(0)
        };

        private void Status(string message, bool ok)
        {
            statusLabel.Text = message;
            statusLabel.ForeColor = ok ? Good : Bad;
        }

        private void Run<T>(Guna2Button button, Action<Action<Result<T>>> operation,
                            Action<Result<T>>? done = null)
        {
            button.Enabled = false;
            statusLabel.ForeColor = Muted;
            statusLabel.Text = "Working...";
            operation(result =>
            {
                if (IsDisposed)
                {
                    return;
                }
                button.Enabled = true;
                Status(result.message, result.ok);
                done?.Invoke(result);
            });
        }

        private void ReadVariable_Click(object sender, EventArgs e)
        {
            if (Required(txtVarName, "Enter a variable name."))
            {
                Run<string>(btnReadVar, callback => Program.app.getVariable(txtVarName.Text.Trim(), callback),
                            result => txtVarValue.Text = result.ok ? result.data : "");
            }
        }

        private void UpdateVariable_Click(object sender, EventArgs e)
        {
            if (Required(txtVarName, "Enter a variable name."))
            {
                Run<JObject>(btnUpdateVar, callback => Program.app.updateVariable(txtVarName.Text.Trim(), txtVarValue.Text, callback));
            }
        }

        private void RefreshFeatures_Click(object sender, EventArgs e) => LoadFeatures();

        private void LoadFeatures()
        {
            Run<JObject>(btnRefreshFeatures, Program.app.getFeatures, features =>
            {
                if (!features.ok)
                {
                    featuresBox.Text = features.message;
                    return;
                }
                Run<JObject>(btnRefreshFeatures, Program.app.getAllMetadata, metadata =>
                {
                    var text = new StringBuilder();
                    List(text, "FEATURES", features.data?["features"] as JObject, "No features on this plan.");
                    text.AppendLine();
                    List(text, "METADATA", metadata.ok ? metadata.data?["metadata"] as JObject : null,
                         metadata.ok ? "No metadata." : metadata.message);
                    featuresBox.Text = text.ToString();
                });
            });
        }

        private static void List(StringBuilder text, string heading, JObject? values, string empty)
        {
            text.AppendLine(heading);
            if (values == null || values.Count == 0)
            {
                text.AppendLine("  " + empty);
                return;
            }
            foreach (var item in values)
            {
                string value = item.Value == null || item.Value.Type == JTokenType.Null ? "-"
                               : item.Value.Type == JTokenType.String ? Show((string?)item.Value)
                               : item.Value.ToString(Newtonsoft.Json.Formatting.None);
                text.AppendLine("  " + item.Key.PadRight(24) + value);
            }
        }

        private void FetchScript_Click(object sender, EventArgs e)
        {
            if (Required(txtScriptName, "Enter a script name."))
            {
                Run<JObject>(btnFetchScript, callback => Program.app.getScript(txtScriptName.Text.Trim(), callback),
                             result => scriptBox.Text = result.ok
                                 ? ((string?)result.data?["content"] ?? "").Replace("\r\n", "\n").Replace("\n", Environment.NewLine)
                                 : "");
            }
        }

        private void SendLog_Click(object sender, EventArgs e)
        {
            if (Required(txtLog, "Type a message to send."))
            {
                Run<JObject>(btnSendLog, callback => Program.app.log(txtLog.Text.Trim(), callback), result =>
                {
                    if (result.ok)
                    {
                        txtLog.Clear();
                    }
                });
            }
        }

        private void CheckSession_Click(object sender, EventArgs e) =>
            Run<JObject>(btnCheckSession, Program.app.checkSession);

        private void SaveOffline_Click(object sender, EventArgs e) =>
            Run<JObject>(btnSaveOffline, Program.app.saveOfflineAccess);

        private void SignOut_Click(object sender, EventArgs e) => SignOut();

        private bool Required(Guna2TextBox box, string message)
        {
            if (!string.IsNullOrWhiteSpace(box.Text))
            {
                return true;
            }
            Status(message, false);
            box.Focus();
            return false;
        }

        private void Home_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (signingOut)
            {
                return;
            }
            e.Cancel = true;
            SignOut();
        }

        private void Home_FormClosed(object sender, FormClosedEventArgs e)
        {
            Program.app.SessionInvalidated -= SessionInvalidated;
        }

        private void SessionInvalidated(AuthlyXException error)
        {
            if (IsDisposed || !IsHandleCreated)
            {
                return;
            }
            BeginInvoke(new Action(() =>
            {
                if (IsDisposed)
                {
                    return;
                }
                signingOut = true;
                MessageBox.Show(error.Message, "Access ended");
                Close();
            }));
        }

        private void SignOut()
        {
            if (signingOut)
            {
                return;
            }
            signingOut = true;
            Enabled = false;
            statusLabel.ForeColor = Muted;
            statusLabel.Text = "Signing out...";
            Program.app.logout(result =>
            {
                if (IsDisposed)
                {
                    return;
                }
                if (!result.ok)
                {
                    MessageBox.Show(result.message, "Sign out");
                }
                Close();
            });
        }

        private static string Show(string? value) => string.IsNullOrEmpty(value) ? "-" : value!;

        private static string When(DateTimeOffset? value) =>
            value == null ? "-" : value.Value.ToUniversalTime().ToString("yyyy-MM-dd HH:mm") + " UTC";

        private static string DisplayName(UserData user) =>
            user.LoginType == "license" ? Show(user.LicenseKey)
            : user.LoginType == "device" ? "this device"
                                         : Show(user.Username);

        private static string Expiry(UserData user) =>
            user.IsLifetime || user.ExpiryDate == null ? "Never" : user.ExpiryDate.Value.ToString("yyyy-MM-dd");
    }
}
