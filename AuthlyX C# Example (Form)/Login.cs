using AuthlyXSDK;
using Guna.UI2.WinForms;
using System;
using System.Windows.Forms;

namespace AuthlyX_CS_Example_Form
{
    public partial class Login : Form
    {
        public Login()
        {
            InitializeComponent();
        }

        private void deviceLogin_Click(object sender, EventArgs e)
        {
            SignIn(deviceLogin, done => Program.app.deviceLogin(done));
        }

        private void Login_Load(object sender, EventArgs e)
        {
            Enabled = false;
            Program.app.init(result =>
            {
                if (IsDisposed)
                {
                    return;
                }

                if (!result.ok)
                {
                    if (result.code == "UPDATE_REQUIRED" || result.code == "VERSION_MISMATCH")
                    {
                        Close();
                    }
                    else if (MessageBox.Show(result.message, "Could not connect", MessageBoxButtons.RetryCancel) == DialogResult.Retry)
                    {
                        Login_Load(sender, e);
                    }
                    else
                    {
                        Close();
                    }
                    return;
                }
                if (result.code == "OFFLINE_MODE")
                {
                    MessageBox.Show(result.message, "Offline");
                }
                Enabled = true;
            });
        }

        private void btn_Login_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(txtUser.Text) || string.IsNullOrEmpty(txtPass.Text))
            {
                MessageBox.Show("Please enter both username and password.", "AuthlyX");
                return;
            }
            SignIn(btn_Login, done => Program.app.login(txtUser.Text, txtPass.Text, done));
        }

        private void btn_simpleLogin_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(txt_Key.Text))
            {
                MessageBox.Show("Please enter a license key.", "AuthlyX");
                return;
            }
            SignIn(btn_simpleLogin, done => Program.app.login(txt_Key.Text, done));
        }

        private void SignIn(Guna2Button button, Action<Action<Result<SessionInfo>>> operation)
        {
            string title = button.Text;
            Enabled = false;
            button.Text = "Signing in...";
            operation(result =>
            {
                if (IsDisposed)
                {
                    return;
                }

                Enabled = true;
                button.Text = title;
                if (!result.ok)
                {
                    MessageBox.Show(result.message, "Sign-in failed");
                    return;
                }
                var home = new Home();
                home.FormClosed += (sender, args) =>
                {
                    if (!IsDisposed)
                    {
                        Show();
                        Activate();
                    }
                };
                home.Show();
                Hide();
            });
        }

        private void btn_Register_Click(object sender, EventArgs e)
        {
        }
        private void exit_Click(object sender, EventArgs e)
        {
            Enabled = false;
            Program.app.logout(result =>
            {
                if (!IsDisposed)
                {
                    Close();
                }
            });
        }
    }
}
