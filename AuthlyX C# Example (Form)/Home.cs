using System;
using System.Linq;
using System.Windows.Forms;
namespace AuthlyX_CS_Example_Form
{
    public partial class Home : Form
    {
        private const string ChannelName = "MAIN";
        private bool loadingChats;

        public Home()
        {
            InitializeComponent();
        }

        private void guna2ControlBox1_Click(object sender, EventArgs e)
        {
            Environment.Exit(0);
            Application.Exit();
        }

        private void Home_Load(object sender, EventArgs e)
        {
            var data = Login.AuthlyXApp.userData;
            username.Text = data.Username;
            email.Text = data.Email;
            license_key.Text = data.LicenseKey;
            ipaddress.Text = data.IpAddress;
            sub.Text = data.Subscription;
            registered_at.Text = data.RegisteredAt;
            expiry.Text = data.ExpiryDate;
            last_login.Text = data.LastLogin;
        }

        private void btn_GetVar_Click(object sender, EventArgs e)
        {
            string key = txtVarKey.Text;
            if (string.IsNullOrWhiteSpace(key))
            {
                MessageBox.Show("Please enter a variable key first.",
                    "Missing Key", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            btn_GetVar.Enabled = false;
            Login.AuthlyXApp.GetVariable(key, (value, response) =>
            {
                btn_GetVar.Enabled = true;

                if (!response.success)
                {
                    MessageBox.Show(response.message,
                        "Variable Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                txtVarValue.Text = value ?? string.Empty;
            });
        }

        private void btn_SetVar_Click(object sender, EventArgs e)
        {
            string key = txtVarKey.Text;
            string value = txtVarValue.Text;

            if (string.IsNullOrWhiteSpace(key))
            {
                MessageBox.Show("Please enter a variable key first.",
                    "Missing Key", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            btn_SetVar.Enabled = false;
            Login.AuthlyXApp.SetVariable(key, value, response =>
            {
                btn_SetVar.Enabled = true;

                if (!response.success)
                {
                    MessageBox.Show(response.message,
                        "Variable Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            });
        }

        private void btn_SendMSG_Click(object sender, EventArgs e)
        {
            string chat = txtMSG.Text;
            if (!string.IsNullOrEmpty(chat))
            {
                btn_SendMSG.Enabled = false;
                Login.AuthlyXApp.SendChat(chat, ChannelName, response =>
                {
                    btn_SendMSG.Enabled = true;

                    if (!response.success)
                    {
                        MessageBox.Show(response.message,
                            "Chat Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }

                    txtMSG.Clear();
                    RefreshChats();
                });
            }
        }

        private void timer1_Tick(object sender, EventArgs e)
        {
            RefreshChats();
        }

        private void RefreshChats()
        {
            if (loadingChats)
            {
                return;
            }

            loadingChats = true;
            Login.AuthlyXApp.GetChats(ChannelName, (chatJson, response) =>
            {
                loadingChats = false;

                if (!response.success)
                {
                    return;
                }

                richtextbox.Clear();

                if (Login.AuthlyXApp.chatMessages.Count <= 0)
                {
                    return;
                }

                var sortedMessages = Login.AuthlyXApp.chatMessages.Messages
                    .OrderBy(m => m.CreatedAtDateTime ?? DateTime.MinValue)
                    .ToList();

                foreach (var msg in sortedMessages)
                {
                    string timeStr = msg.CreatedAtDateTime?.ToString("HH:mm:ss") ?? msg.CreatedAt;
                    string formattedMsg = $"[{timeStr}] {msg.Username}: {msg.Message}\r\n";
                    richtextbox.AppendText(formattedMsg);
                }

                richtextbox.SelectionStart = richtextbox.Text.Length;
                richtextbox.ScrollToCaret();
            });
        }
    }
}
