using System;
using System.Windows.Forms;
using Bojalib.Security.Verification;

namespace Bojalib
{
    namespace Security
    {
        namespace Verification
        {
            public partial class SimpleVerification : Form
            {
                /// <summary>
                /// Takes a hashed password which can be later compared by btnVerify_Click function
                /// </summary>
                /// <param name="password"></param>
                public SimpleVerification()
                {
                    InitializeComponent();
                }

                /// <summary>
                /// Compares encrypted password in Verification.Password to input. Returns DialogResult.OK if verification was successful, otherwise returns DialogResult.No 
                /// </summary>
                /// <param name="sender"></param>
                /// <param name="e"></param>
                private void btnVerify_Click(object sender, EventArgs e)
                {
                    if (Verification.Password == Verification.HashPassword(this.tbPassw.Text))
                    {
                        this.DialogResult = DialogResult.OK;
                    }
                    else
                    {
                        this.DialogResult = DialogResult.No;
                    }
                    Close();
                }

                private void SimpleVerification_FormClosing(object sender, FormClosingEventArgs e)
                {
                    this.DialogResult = DialogResult.No;
                    this.Close();
                    this.Dispose();
                }
            }


        }
    }    
}
