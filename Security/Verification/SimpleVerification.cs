using System;
using System.Windows.Forms;

namespace Bojalib
{
    namespace Security
    {
        namespace Verification
        {
            public partial class SimpleVerification : Form
            {
                /// <summary>
                /// Takes a hashed password from static class Verification, which can be later compared by btnVerify_Click function.
                /// </summary>
                /// <param name="password"></param>
                public SimpleVerification()
                {
                    InitializeComponent();
                }
                public SimpleVerification(Verification v)
                {
                    InitializeComponent();
                    this.v = v;
                }
                private Verification v;
                /// <summary>
                /// Compares encrypted password in Verification.Password to input. Returns DialogResult.OK if verification was successful, otherwise returns DialogResult.No 
                /// </summary>
                /// <param name="sender"></param>
                /// <param name="e"></param>
                private void btnVerify_Click(object sender, EventArgs e)
                {
                    
                    
                    {
                        if (v.LocalPassword == Verification.HashPassword(this.tbPassw.Text))
                        {
                            this.DialogResult = DialogResult.OK;
                        }
                        else
                        {
                            this.DialogResult = DialogResult.No;
                        }
                    }
                    Close();
                }

            }


        }
    }
}
